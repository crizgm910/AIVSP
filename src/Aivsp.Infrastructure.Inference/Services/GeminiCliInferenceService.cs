using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Exceptions;
using Aivsp.Domain.Entities;

namespace Aivsp.Infrastructure.Inference.Services;

public sealed class GeminiCliInferenceService : IInferenceService
{
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IGeminiCliProcessRunner _processRunner;

    public GeminiCliInferenceService(IGeminiCliProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<AssistanceOutput> InferActionAsync(
        string userPrompt,
        AssistanceDetectionResult detection,
        AssistantOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(options);

        if (options.UseVisionModel)
        {
            throw new InferenceResponseException(
                "Gemini CLI is available only for the OCR textual flow while VLM is disabled.");
        }

        var localResult = OllamaInferenceService.TryResolveLocally(detection, options);
        if (localResult != null)
        {
            return localResult;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var prompt = attempt == 0
                    ? OllamaInferenceService.BuildCompactPrompt(detection, options)
                    : OllamaInferenceService.BuildRetryPrompt(detection, options);

                var processResult = await _processRunner.RunAsync(
                    options.GeminiCliExecutablePath,
                    prompt,
                    options.GeminiModelName,
                    cancellationToken);

                if (processResult.ExitCode != 0)
                {
                    throw new InferenceUnavailableException(
                        $"Gemini CLI exited with code {processResult.ExitCode}. " +
                        SanitizeDiagnostic(processResult.StandardError, processResult.StandardOutput));
                }

                try
                {
                    var rawModelResponse = ExtractModelResponse(processResult.StandardOutput);
                    var structured = OllamaInferenceService.ParseStructuredResponse(
                        rawModelResponse,
                        detection.Mode,
                        "Gemini CLI");
                    if (OllamaInferenceService.IsUnrelatedLocalContextLeak(
                            structured,
                            detection,
                            options))
                    {
                        throw new InferenceResponseException(
                            "Gemini CLI answered with unrelated local AIVSP configuration.");
                    }
                    if (OllamaInferenceService.IsIrrelevantOpenAnswer(
                            structured,
                            detection))
                    {
                        throw new InferenceResponseException(
                            "Gemini CLI answered the open question with irrelevant or incomplete content.");
                    }

                    stopwatch.Stop();
                    return OllamaInferenceService.MapStructuredResponse(
                        rawModelResponse,
                        structured,
                        detection,
                        options,
                        stopwatch.Elapsed);
                }
                catch (InferenceResponseException) when (attempt == 0)
                {
                    // Retry once with a stricter JSON-only instruction.
                }
            }

            throw new InferenceResponseException(
                "Gemini CLI did not return a usable answer after retrying.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (InferenceResponseException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (InferenceUnavailableException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            throw new InferenceUnavailableException(
                "Failed to infer action because Gemini CLI is unavailable.",
                ex);
        }
    }

    private static string ExtractModelResponse(string cliOutput)
    {
        if (string.IsNullOrWhiteSpace(cliOutput))
        {
            throw new InferenceResponseException("Gemini CLI returned no standard output.");
        }

        try
        {
            using var document = JsonDocument.Parse(cliOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InferenceResponseException(
                    "Gemini CLI output envelope was not a JSON object.");
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : error.ToString();
                throw new InferenceUnavailableException(
                    $"Gemini CLI reported an error. {SanitizeDiagnostic(message)}");
            }

            if (!document.RootElement.TryGetProperty("response", out var response) ||
                response.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(response.GetString()))
            {
                throw new InferenceResponseException(
                    "Gemini CLI JSON envelope did not contain a response.");
            }

            return response.GetString()!;
        }
        catch (JsonException ex)
        {
            throw new InferenceResponseException(
                "Could not parse the Gemini CLI JSON envelope.",
                ex);
        }
    }

    private static string SanitizeDiagnostic(params string?[] diagnostics)
    {
        var lines = diagnostics
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => AnsiEscapeRegex.Replace(value!, string.Empty))
            .SelectMany(value => value.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        var text = lines.FirstOrDefault(line =>
                line.Contains("IneligibleTierError", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Error authenticating", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
        {
            return "No diagnostic was provided.";
        }

        const int maximumLength = 400;
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }
}
