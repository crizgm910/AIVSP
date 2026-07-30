using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.AutoPaste;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Detection;
using Aivsp.Application.Exceptions;
using Aivsp.Application.Notifications;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Aivsp.Application.UseCases;

public class MainAssistantUseCase
{
    private readonly IImageCaptureService _captureService;
    private readonly IOcrEngine _ocrEngine;
    private readonly IQuestionTypeDetector _questionTypeDetector;
    private readonly IInferenceService _inferenceService;
    private readonly IOverlayService _overlayService;
    private readonly IInputSimulationService _inputSimulator;
    private readonly IAutoPasteSecurityService _autoPasteSecurityService;
    private readonly INotificationService _notificationService;
    private readonly AssistantOptions _options;
    private readonly ILogger<MainAssistantUseCase> _logger;

    public MainAssistantUseCase(
        IImageCaptureService captureService,
        IOcrEngine ocrEngine,
        IQuestionTypeDetector questionTypeDetector,
        IInferenceService inferenceService,
        IOverlayService overlayService,
        IInputSimulationService inputSimulator,
        IAutoPasteSecurityService autoPasteSecurityService,
        INotificationService notificationService,
        AssistantOptions options,
        ILogger<MainAssistantUseCase> logger)
    {
        _captureService = captureService;
        _ocrEngine = ocrEngine;
        _questionTypeDetector = questionTypeDetector;
        _inferenceService = inferenceService;
        _overlayService = overlayService;
        _inputSimulator = inputSimulator;
        _autoPasteSecurityService = autoPasteSecurityService;
        _notificationService = notificationService;
        _options = options;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        string userPrompt,
        int? cursorX = null,
        int? cursorY = null,
        CancellationToken cancellationToken = default,
        Guid? correlationId = null)
    {
        var executionId = correlationId ?? Guid.NewGuid();
        using var logScope = _logger.BeginScope("CorrelationId: {CorrelationId}", executionId);

        if (_options.IsPaused)
        {
            _logger.LogInformation("El asistente está pausado. Se ignora la solicitud.");
            Notify(executionId, NotificationStatus.Warning, NotificationCode.AssistantPaused);
            return;
        }
        if (!_options.DesktopOcrEnabled)
        {
            _logger.LogInformation(
                "La fuente Apps de escritorio (OCR) está deshabilitada. Se ignora el hotkey.");
            Notify(
                executionId,
                NotificationStatus.Warning,
                NotificationCode.DesktopCaptureDisabled);
            return;
        }

        var inferenceTimeoutSeconds = _options.InferenceProvider == InferenceProvider.GeminiCli
            ? _options.GeminiInferenceTimeoutSeconds
            : _options.InferenceTimeoutSeconds;
        var operationToken = cancellationToken;

        var stage = ExecutionStage.Starting;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            // Avoid capturing a previous answer marker and stop any old animation before
            // processing a new request.
            _overlayService.Clear();

            var totalSw = Stopwatch.StartNew();
            _logger.LogInformation("Activando asistente visual.");

            stage = ExecutionStage.Capture;
            _logger.LogDebug("Capturando ventana...");
            using var capturedImage = await _captureService.CaptureActiveWindowAsync(operationToken);
            var captureContext = capturedImage?.Context ?? default;

            System.Collections.Generic.IReadOnlyList<OcrResult> ocrResults = new System.Collections.Generic.List<OcrResult>();

            if (!_options.UseVisionModel)
            {
                stage = ExecutionStage.Ocr;
                _logger.LogDebug("Ejecutando OCR...");
                ocrResults = await _ocrEngine.ExtractTextAsync(capturedImage!, _options, operationToken);

                ocrResults = QuestionContextSelector.Select(ocrResults, cursorY);
                _logger.LogInformation("Contexto aislado a la tarjeta de pregunta ({LineCount} líneas).", ocrResults.Count);

                if (ocrResults.Count == 0)
                {
                    _logger.LogWarning("No se detectó texto en la pantalla.");
                    Notify(executionId, NotificationStatus.Warning, NotificationCode.NoTextDetected);
                    return;
                }
            }

            stage = ExecutionStage.Detection;
            var detectionResult = _questionTypeDetector.Detect(ocrResults, capturedImage!)
                with
            { CursorX = cursorX, CursorY = cursorY };
            _logger.LogInformation("Modo detectado: {Mode}", detectionResult.Mode);

            if (detectionResult.Mode == AssistanceMode.Unknown)
            {
                _logger.LogWarning("Modo desconocido, el asistente no tomará acción.");
                Notify(executionId, NotificationStatus.Warning, NotificationCode.QuestionNotRecognized);
                return;
            }

            // Notify only after capture and OCR so AIVSP's own tray message cannot
            // become part of the image being analyzed.
            Notify(executionId, NotificationStatus.Processing, NotificationCode.ExecutionStarted);

            stage = ExecutionStage.Inference;
            _logger.LogInformation(
                "Consultando al proveedor de inferencia {InferenceProvider}. ¡Esto puede tomar unos segundos...!",
                _options.InferenceProvider);

            AssistanceOutput inferenceOutput;
            using (var inferenceCts =
                CancellationTokenSource.CreateLinkedTokenSource(operationToken))
            {
                if (inferenceTimeoutSeconds > 0)
                {
                    inferenceCts.CancelAfter(TimeSpan.FromSeconds(inferenceTimeoutSeconds));
                }

                try
                {
                    inferenceOutput = await _inferenceService.InferActionAsync(
                        userPrompt,
                        detectionResult,
                        _options,
                        inferenceCts.Token);
                    inferenceCts.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (
                    !operationToken.IsCancellationRequested &&
                    inferenceCts.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "La inferencia agotó el tiempo límite de {InferenceTimeoutSeconds}s para {InferenceProvider}.",
                        inferenceTimeoutSeconds,
                        _options.InferenceProvider);
                    Notify(
                        executionId,
                        NotificationStatus.Warning,
                        NotificationCode.InferenceTimedOut);
                    return;
                }
            }

            var autoPasteWasCancelled = false;
            if (inferenceOutput is VisualIndicatorOutput visualOutput)
            {
                stage = ExecutionStage.Overlay;
                _logger.LogInformation(
                    "Mostrando indicador visual. Confidence: {Confidence}; Bounds: {Bounds}.",
                    visualOutput.Confidence,
                    visualOutput.Match.Candidate.Bounds);
                await _overlayService.DrawIndicatorAsync(visualOutput.Match, _options.OverlayDurationMilliseconds);
            }
            else if (inferenceOutput is TextSuggestionOutput textOutput)
            {
                _logger.LogInformation(
                    "Sugerencia generada. Confidence: {Confidence}; CharacterCount: {CharacterCount}.",
                    textOutput.Confidence,
                    textOutput.SuggestedText.Length);

                if (_options.AutoPasteEnabled)
                {
                    var maximumContextAgeSeconds =
                        _options.InferenceProvider == InferenceProvider.GeminiCli
                            ? _options.GeminiAutoPasteMaximumContextAgeSeconds
                            : _options.AutoPasteMaximumContextAgeSeconds;
                    var validation = _autoPasteSecurityService.Validate(new AutoPasteRequest(
                        textOutput.SuggestedText,
                        textOutput.Confidence,
                        captureContext,
                        TimeSpan.FromSeconds(maximumContextAgeSeconds),
                        _options.MinimumAutoPasteConfidence));

                    if (!validation.CanPaste)
                    {
                        autoPasteWasCancelled = true;
                        _logger.LogWarning(
                            "AutoPasteCancelledReason: {AutoPasteCancelledReason}; HWND: {WindowHandle}; PID: {ProcessId}",
                            validation.CancelledReason,
                            captureContext.WindowHandle,
                            captureContext.ProcessId);
                        Notify(executionId, NotificationStatus.Cancelled, NotificationCode.AutoPasteCancelled);
                    }
                    else
                    {
                        stage = ExecutionStage.AutoPaste;
                        _logger.LogInformation("Intentando simular teclado para auto-pegar...");
                        await _inputSimulator.SimulatePasteAsync(
                            textOutput.SuggestedText,
                            textOutput.SuggestedX,
                            textOutput.SuggestedY,
                            operationToken);
                        _logger.LogInformation(
                            "AutoPasteSuccess; HWND: {WindowHandle}; PID: {ProcessId}",
                            captureContext.WindowHandle,
                            captureContext.ProcessId);
                    }
                }

                operationToken.ThrowIfCancellationRequested();

                stage = ExecutionStage.Overlay;
                await _overlayService.DrawTextSuggestionAsync(
                    textOutput,
                    _options.OverlayDurationMilliseconds);
            }
            else
            {
                _logger.LogWarning("La respuesta de inferencia no produjo un output compatible.");
                Notify(executionId, NotificationStatus.Error, NotificationCode.InvalidInferenceResponse);
                return;
            }

            totalSw.Stop();
            _logger.LogInformation("Flujo completado en {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
            if (!autoPasteWasCancelled)
            {
                Notify(executionId, NotificationStatus.Completed, NotificationCode.ExecutionCompleted);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("La ejecución fue cancelada.");
            Notify(executionId, NotificationStatus.Cancelled, NotificationCode.ExecutionCancelled);
            throw;
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                InferenceResponseException => NotificationCode.InvalidInferenceResponse,
                InferenceModelUnavailableException =>
                    NotificationCode.InferenceModelUnavailable,
                InferenceUnavailableException => _options.InferenceProvider == InferenceProvider.Ollama
                    ? NotificationCode.OllamaUnavailable
                    : NotificationCode.InferenceProviderUnavailable,
                _ when stage == ExecutionStage.Ocr => NotificationCode.OcrUnavailable,
                _ when stage == ExecutionStage.Inference =>
                    _options.InferenceProvider == InferenceProvider.Ollama
                        ? NotificationCode.OllamaUnavailable
                        : NotificationCode.InferenceProviderUnavailable,
                _ when stage == ExecutionStage.AutoPaste => NotificationCode.AutoPasteFailed,
                _ => NotificationCode.UnexpectedError
            };

            _logger.LogError(ex, "La ejecución falló durante la etapa {ExecutionStage}.", stage);
            if (stage == ExecutionStage.AutoPaste)
            {
                _logger.LogWarning("AutoPasteCancelledReason: InputFailure");
            }
            Notify(executionId, NotificationStatus.Error, code);
            throw;
        }
    }

    private void Notify(Guid correlationId, NotificationStatus status, NotificationCode code)
    {
        try
        {
            _notificationService.Notify(new UserNotification(correlationId, status, code));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo encolar la notificación {NotificationCode}.", code);
        }
    }

    private enum ExecutionStage
    {
        Starting,
        Capture,
        Ocr,
        Detection,
        Inference,
        AutoPaste,
        Overlay
    }
}
