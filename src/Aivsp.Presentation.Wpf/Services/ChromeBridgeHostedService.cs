using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Exceptions;
using Aivsp.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aivsp.Presentation.Wpf.Services;

public sealed class ChromeBridgeHostedService : IHostedService, IDisposable
{
    public const int Port = 18791;
    private const long MaximumRequestBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly BrowserDomAssistantService _assistant;
    private readonly AssistantOptions _options;
    private readonly INotificationService _notifications;
    private readonly ILogger<ChromeBridgeHostedService> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private HttpListener? _listener;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _acceptLoop;
    private bool _disposed;

    public ChromeBridgeHostedService(
        BrowserDomAssistantService assistant,
        AssistantOptions options,
        INotificationService notifications,
        ILogger<ChromeBridgeHostedService> logger)
    {
        _assistant = assistant;
        _options = options;
        _notifications = notifications;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

        try
        {
            listener.Start();
            _listener = listener;
            _lifetimeCts = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(listener, _lifetimeCts.Token);
            _logger.LogInformation(
                "Chrome bridge listening on loopback port {Port}. Enabled: {Enabled}.",
                Port,
                _options.BrowserIntegrationEnabled);
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
        {
            listener.Close();
            _logger.LogError(ex, "Chrome bridge could not bind loopback port {Port}.", Port);
            Notify(NotificationStatus.Warning, NotificationCode.BrowserBridgeUnavailable);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetimeCts?.Cancel();
        _listener?.Close();

        if (_acceptLoop != null)
        {
            try
            {
                await _acceptLoop.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Host shutdown cancellation is the expected terminal state.
            }
            catch (HttpListenerException)
            {
                // Closing HttpListener interrupts GetContextAsync.
            }
        }

        _acceptLoop = null;
        _listener = null;
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts?.Cancel();
        _listener?.Close();
        _lifetimeCts?.Dispose();
        // Request handlers are intentionally fire-and-forget so one slow inference
        // cannot stop the accept loop. Do not dispose the semaphore here: an active
        // handler may still release it while the host is shutting down.
    }

    private async Task AcceptLoopAsync(
        HttpListener listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (HttpListenerException) when (!listener.IsListening)
            {
                return;
            }

            _ = HandleContextSafelyAsync(context, cancellationToken);
        }
    }

    private async Task HandleContextSafelyAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (!IsAuthorized(context.Request))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }

            var path = context.Request.Url?.AbsolutePath;
            if (context.Request.HttpMethod == "GET" && path == "/v1/status")
            {
                await WriteJsonAsync(
                    context.Response,
                    HttpStatusCode.OK,
                    new BridgeStatusResponse(
                        "AIVSP Chrome Bridge",
                        _options.BrowserIntegrationEnabled,
                        _options.InferenceProvider.ToString(),
                        _options.OverlayProfile.ToString()),
                    cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/v1/assist")
            {
                await HandleAssistRequestAsync(context, cancellationToken);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException)
        {
            _logger.LogDebug(ex, "Chrome bridge client disconnected before completion.");
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (ObjectDisposedException)
            {
                // The client or host already closed the response.
            }
        }
    }

    private async Task HandleAssistRequestAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.BrowserIntegrationEnabled)
        {
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.ServiceUnavailable,
                "browser_integration_disabled",
                null,
                cancellationToken);
            return;
        }
        if (context.Request.ContentLength64 is < 1 or > MaximumRequestBodyBytes)
        {
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.BadRequest,
                "invalid_request_size",
                null,
                cancellationToken);
            return;
        }
        if (!await _requestGate.WaitAsync(0, cancellationToken))
        {
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.TooManyRequests,
                "assistant_busy",
                null,
                cancellationToken);
            return;
        }

        var correlationId = Guid.NewGuid();
        try
        {
            var request = await JsonSerializer.DeserializeAsync<BrowserQuestionRequest>(
                context.Request.InputStream,
                JsonOptions,
                cancellationToken);
            if (request == null)
            {
                throw new ArgumentException("Browser request body was empty.");
            }

            Notify(
                NotificationStatus.Processing,
                NotificationCode.ExecutionStarted,
                correlationId);
            var result = await _assistant.AssistAsync(
                request,
                correlationId,
                cancellationToken);
            Notify(
                NotificationStatus.Completed,
                NotificationCode.ExecutionCompleted,
                correlationId);
            _logger.LogInformation(
                "Chrome DOM request completed. CorrelationId: {CorrelationId}; Mode: {Mode}; " +
                "Provider: {Provider}; DurationMs: {DurationMs}.",
                correlationId,
                result.Mode,
                result.Provider,
                result.DurationMilliseconds);
            await WriteJsonAsync(
                context.Response,
                HttpStatusCode.OK,
                result,
                cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "Chrome DOM request rejected. CorrelationId: {CorrelationId}.",
                correlationId);
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.BadRequest,
                "invalid_request",
                correlationId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown; the response will be closed by the caller.
        }
        catch (OperationCanceledException)
        {
            Notify(
                NotificationStatus.Warning,
                NotificationCode.InferenceTimedOut,
                correlationId);
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.GatewayTimeout,
                "inference_timeout",
                correlationId,
                CancellationToken.None);
        }
        catch (InferenceModelUnavailableException ex)
        {
            _logger.LogError(
                ex,
                "Chrome DOM model unavailable. CorrelationId: {CorrelationId}.",
                correlationId);
            Notify(
                NotificationStatus.Error,
                NotificationCode.InferenceModelUnavailable,
                correlationId);
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.ServiceUnavailable,
                "model_unavailable",
                correlationId,
                cancellationToken);
        }
        catch (InferenceUnavailableException ex)
        {
            _logger.LogError(
                ex,
                "Chrome DOM provider unavailable. CorrelationId: {CorrelationId}.",
                correlationId);
            Notify(
                NotificationStatus.Error,
                NotificationCode.InferenceProviderUnavailable,
                correlationId);
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.ServiceUnavailable,
                "provider_unavailable",
                correlationId,
                cancellationToken);
        }
        catch (InferenceResponseException ex)
        {
            _logger.LogError(
                ex,
                "Chrome DOM inference response invalid. CorrelationId: {CorrelationId}.",
                correlationId);
            Notify(
                NotificationStatus.Error,
                NotificationCode.InvalidInferenceResponse,
                correlationId);
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.BadGateway,
                "invalid_inference_response",
                correlationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Chrome DOM request failed. CorrelationId: {CorrelationId}.",
                correlationId);
            Notify(
                NotificationStatus.Error,
                NotificationCode.BrowserRequestFailed,
                correlationId);
            await WriteErrorAsync(
                context.Response,
                HttpStatusCode.InternalServerError,
                "unexpected_error",
                correlationId,
                cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var authorization = request.Headers["Authorization"];
        const string bearerPrefix = "Bearer ";
        if (authorization == null ||
            !authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedToken = authorization[bearerPrefix.Length..].Trim();
        var expected = Encoding.ASCII.GetBytes(_options.BrowserBridgeToken);
        var supplied = Encoding.ASCII.GetBytes(suppliedToken);
        return supplied.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static Task WriteErrorAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string error,
        Guid? correlationId,
        CancellationToken cancellationToken) =>
        WriteJsonAsync(
            response,
            statusCode,
            new BridgeErrorResponse(error, correlationId),
            cancellationToken);

    private static async Task WriteJsonAsync<T>(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
    }

    private void Notify(
        NotificationStatus status,
        NotificationCode code,
        Guid? correlationId = null)
    {
        try
        {
            _notifications.Notify(new UserNotification(
                correlationId ?? Guid.NewGuid(),
                status,
                code));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chrome bridge notification could not be queued.");
        }
    }

    private sealed record BridgeStatusResponse(
        string Service,
        bool Enabled,
        string Provider,
        string Overlay);

    private sealed record BridgeErrorResponse(string Error, Guid? CorrelationId);
}
