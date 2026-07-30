using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Aivsp.Application.BrowserIntegration;

public sealed partial class BrowserDomAssistantService
{
    private const int MaximumQuestionLength = 8_000;
    private const int MaximumOptionLength = 2_000;
    private const int MaximumOptionCount = 12;

    private readonly IInferenceService _inferenceService;
    private readonly AssistantOptions _options;
    private readonly ILogger<BrowserDomAssistantService> _logger;

    public BrowserDomAssistantService(
        IInferenceService inferenceService,
        AssistantOptions options,
        ILogger<BrowserDomAssistantService> logger)
    {
        _inferenceService = inferenceService;
        _options = options;
        _logger = logger;
    }

    public async Task<BrowserAssistanceResult> AssistAsync(
        BrowserQuestionRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        Validate(request);
        using var logScope = _logger.BeginScope(
            "CorrelationId: {CorrelationId}",
            correlationId);
        var stopwatch = Stopwatch.StartNew();
        var lines = BuildOcrLines(request);
        var detection = new AssistanceDetectionResult(request.Mode, lines, null!);
        var timeoutSeconds = _options.InferenceProvider == InferenceProvider.GeminiCli
            ? _options.BrowserGeminiInferenceTimeoutSeconds
            : _options.InferenceTimeoutSeconds;

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        _logger.LogInformation(
            "Procesando contexto DOM. Mode: {Mode}; OptionCount: {OptionCount}; Provider: {Provider}.",
            request.Mode,
            request.Options.Count,
            _options.InferenceProvider);

        var output = await _inferenceService.InferActionAsync(
            "Responde a la pregunta seleccionada en el navegador",
            detection,
            _options,
            timeoutCts.Token);
        timeoutCts.Token.ThrowIfCancellationRequested();
        stopwatch.Stop();

        return output switch
        {
            VisualIndicatorOutput visual => MapVisual(
                request,
                visual,
                correlationId,
                stopwatch.ElapsedMilliseconds),
            TextSuggestionOutput text => new BrowserAssistanceResult(
                correlationId,
                request.ContextId,
                request.Mode,
                text.SuggestedText,
                null,
                text.Confidence,
                stopwatch.ElapsedMilliseconds,
                _options.InferenceProvider,
                _options.OverlayProfile),
            _ => throw new InvalidOperationException(
                "The browser inference output type is unsupported.")
        };
    }

    private BrowserAssistanceResult MapVisual(
        BrowserQuestionRequest request,
        VisualIndicatorOutput visual,
        Guid correlationId,
        long durationMilliseconds)
    {
        var selectedKey = ExtractOptionKey(visual.SelectedText)
            ?? ExtractOptionKey(visual.Match.Candidate.Text);
        var option = selectedKey == null
            ? null
            : request.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, selectedKey, StringComparison.OrdinalIgnoreCase));

        if (option == null)
        {
            throw new InvalidOperationException(
                "The inferred browser option could not be mapped to the DOM context.");
        }

        return new BrowserAssistanceResult(
            correlationId,
            request.ContextId,
            request.Mode,
            option.Text,
            option.Key,
            visual.Confidence,
            durationMilliseconds,
            _options.InferenceProvider,
            _options.OverlayProfile);
    }

    private static IReadOnlyList<OcrResult> BuildOcrLines(BrowserQuestionRequest request)
    {
        var lines = new List<OcrResult>
        {
            Ocr(request.Question, 0)
        };

        if (request.Mode == AssistanceMode.MultipleChoice)
        {
            for (var index = 0; index < request.Options.Count; index++)
            {
                var option = request.Options[index];
                lines.Add(Ocr($"{option.Key}) {RemoveOptionPrefix(option.Text)}", index + 1));
            }
        }
        else
        {
            lines.Add(Ocr(
                string.IsNullOrWhiteSpace(request.InputPlaceholder)
                    ? "Escribe tu respuesta"
                    : request.InputPlaceholder,
                1));
        }

        return lines;
    }

    private static OcrResult Ocr(string text, int line) => new(
        text,
        new BoundingBox(20, 20 + (line * 40), 1_000, 30),
        1,
        OcrResultLevel.Line);

    private static string RemoveOptionPrefix(string text) =>
        OptionPrefixRegex().Replace(text.Trim(), string.Empty);

    private static string? ExtractOptionKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = OptionKeyRegex().Match(text);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static void Validate(BrowserQuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ContextId) || request.ContextId.Length > 128)
        {
            throw new ArgumentException("Browser context id is missing or too long.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Question) ||
            request.Question.Length > MaximumQuestionLength)
        {
            throw new ArgumentException("Browser question is missing or too long.", nameof(request));
        }
        if (request.Mode is not (AssistanceMode.MultipleChoice or AssistanceMode.OpenText))
        {
            throw new ArgumentException("Browser question mode is unsupported.", nameof(request));
        }
        if (request.Options == null)
        {
            throw new ArgumentException("Browser options collection is missing.", nameof(request));
        }
        if (request.Options.Count > MaximumOptionCount)
        {
            throw new ArgumentException("Browser question contains too many options.", nameof(request));
        }
        if (request.Options.Any(option =>
                string.IsNullOrWhiteSpace(option.Key) ||
                !OptionKeyValueRegex().IsMatch(option.Key) ||
                string.IsNullOrWhiteSpace(option.Text) ||
                option.Text.Length > MaximumOptionLength))
        {
            throw new ArgumentException("Browser question contains an invalid option.", nameof(request));
        }
        if (request.Options
            .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Browser question contains duplicate option keys.",
                nameof(request));
        }
        if (request.InputPlaceholder?.Length > 500)
        {
            throw new ArgumentException("Browser input placeholder is too long.", nameof(request));
        }
        if (request.Mode == AssistanceMode.MultipleChoice && request.Options.Count < 2)
        {
            throw new ArgumentException(
                "A browser multiple-choice question requires at least two options.",
                nameof(request));
        }
        if (request.Mode == AssistanceMode.OpenText && request.Options.Count != 0)
        {
            throw new ArgumentException(
                "A browser open-text question cannot include options.",
                nameof(request));
        }
    }

    [GeneratedRegex(@"^\s*[A-Za-z0-9]{1,8}\s*[\)\].:\-]\s*")]
    private static partial Regex OptionPrefixRegex();

    [GeneratedRegex(@"^\s*([A-Za-z0-9]{1,8})\s*[\)\].:\-]", RegexOptions.IgnoreCase)]
    private static partial Regex OptionKeyRegex();

    [GeneratedRegex(@"^[A-Za-z0-9]{1,8}$")]
    private static partial Regex OptionKeyValueRegex();
}
