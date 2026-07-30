using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Exceptions;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Infrastructure.Inference.Models;
using Aivsp.Infrastructure.Inference.Serialization;

namespace Aivsp.Infrastructure.Inference.Services;

public class OllamaInferenceService : IInferenceService
{
    private readonly HttpClient _httpClient;

    public OllamaInferenceService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<AssistanceOutput> InferActionAsync(string userPrompt, AssistanceDetectionResult detection, AssistantOptions options, CancellationToken cancellationToken)
    {
        var localResult = TryResolveLocally(detection, options);
        if (localResult != null)
        {
            return localResult;
        }

        var requestDto = new OllamaRequest
        {
            Model = options.ModelName,
            Stream = false,
            Format = "json", // Force JSON output for structural parsing
            KeepAlive = "10m",
            Think = false
        };

        if (options.UseVisionModel)
        {
            // VLM branch: send image and ask for coordinates directly
            requestDto.Prompt = $"You are a visual assistant. User prompt: {userPrompt}. Return a JSON object with 'mode': 'MultipleChoice' or 'OpenText', 'suggestedX' (number), 'suggestedY' (number), and 'confidence' (number from 0 to 1).";

            // Convert to base64
            string base64Image = Convert.ToBase64String(detection.Image.Data.ToArray());
            requestDto.Images = new[] { base64Image };
        }
        else
        {
            requestDto.System = BuildSystemPrompt(detection, options);
            requestDto.Prompt = BuildQuestionPrompt(detection, options);
            requestDto.Options = new OllamaRequestOptions
            {
                NumPredict = detection.Mode == AssistanceMode.MultipleChoice ? 48 : 96,
                Temperature = 0
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            OllamaResponse? responseDto = null;
            StructuredInferenceResponse? structured = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        requestDto.System = BuildRetrySystemPrompt(detection, options);
                        requestDto.Prompt = BuildQuestionPrompt(detection, options);
                    }

                    using var requestContent = JsonContent.Create(requestDto, OllamaJsonContext.Default.OllamaRequest);
                    using var response = await _httpClient.PostAsync($"{options.OllamaEndpoint.TrimEnd('/')}/api/generate", requestContent, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new InferenceModelUnavailableException(
                            "Ollama could not find the configured model or generation endpoint.");
                    }
                    response.EnsureSuccessStatusCode();

                    responseDto = await response.Content.ReadFromJsonAsync(OllamaJsonContext.Default.OllamaResponse, cancellationToken);
                    if (responseDto == null || string.IsNullOrWhiteSpace(responseDto.Response))
                    {
                        throw new InferenceResponseException("Ollama returned an empty response.");
                    }

                    structured = ParseStructuredResponse(
                        responseDto.Response,
                        detection.Mode,
                        "Ollama");
                    if (IsUnrelatedLocalContextLeak(structured, detection, options))
                    {
                        throw new InferenceResponseException(
                            "Ollama answered with unrelated local AIVSP configuration.");
                    }
                    if (IsIrrelevantOpenAnswer(structured, detection))
                    {
                        throw new InferenceResponseException(
                            "Ollama answered the open question with irrelevant or incomplete content.");
                    }

                    break;
                }
                catch (InferenceResponseException) when (attempt == 0)
                {
                    structured = null;
                    responseDto = null;
                }
            }

            stopwatch.Stop();
            if (responseDto == null || structured == null)
            {
                throw new InferenceResponseException("Ollama did not return a usable answer after retrying.");
            }

            return MapStructuredResponse(
                responseDto.Response,
                structured,
                detection,
                options,
                stopwatch.Elapsed);
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
        catch (InferenceModelUnavailableException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            throw new InferenceResponseException("Could not parse Ollama's HTTP response.", ex);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            throw new InferenceUnavailableException("Failed to infer action because Ollama is unavailable.", ex);
        }
    }

    internal static AssistanceOutput? TryResolveLocally(
        AssistanceDetectionResult detection,
        AssistantOptions options) =>
        TrySolveDeterministicMath(detection) ?? TrySelectVerifiedKnowledge(detection, options);

    internal static string BuildCompactPrompt(AssistanceDetectionResult detection, AssistantOptions options)
        => BuildSystemPrompt(detection, options) +
            "\n" +
            BuildQuestionPrompt(detection, options);

    private static string BuildSystemPrompt(
        AssistanceDetectionResult detection,
        AssistantOptions options)
    {
        if (detection.Mode == AssistanceMode.MultipleChoice)
        {
            return "Elige la opción correcta según el significado completo. Respeta las letras reales de las opciones. " +
                "Devuelve solamente su letra y confianza de 0 a 1 como JSON: " +
                "{\"answer\":\"A\",\"confidence\":0.95}.";
        }

        var languageInstruction = options.OcrLanguage.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Responde exclusivamente en español. No uses portugués. "
            : $"Answer exclusively in the language identified by locale {options.OcrLanguage}. ";

        return languageInstruction +
            "Contesta directamente la PREGUNTA visible. Identifica y cumple cada requisito explícito; " +
            "si pide dos elementos, incluye dos elementos concretos y distintos. " +
            "No hables del modelo, la aplicación, AIVSP, Ollama, endpoints ni configuración salvo que la pregunta los mencione. " +
            "El valor de answer debe ser una sola oración clara de máximo 45 palabras, sin listas, numeración, introducciones ni texto adicional. " +
            "Conserva exactamente los nombres técnicos. " +
            "Devuelve exactamente JSON: {\"answer\":\"respuesta\",\"confidence\":0.95}.";
    }

    private static string BuildQuestionPrompt(
        AssistanceDetectionResult detection,
        AssistantOptions options)
    {
        var lines = detection.OcrResults.Select(result => result.Text.Trim()).ToList();
        if (detection.Mode == AssistanceMode.MultipleChoice)
        {
            var groupedOptions = BuildOptionCandidates(detection.OcrResults);
            if (groupedOptions.Count > 0)
            {
                var firstOptionY = groupedOptions[0].Anchor.Bounds.Y;
                lines = detection.OcrResults
                    .Where(result => result.Bounds.Y < firstOptionY)
                    .Select(result => result.Text.Trim())
                    .Concat(groupedOptions.Select(option => option.Text))
                    .ToList();
            }
        }
        else
        {
            lines = lines
                .Where(line => !InputCueRegex.IsMatch(line))
                .ToList();
        }

        return
            BuildKnowledgeBlock(detection, options) +
            "PREGUNTA:\n" +
            string.Join("\n", lines);
    }

    private static string BuildKnowledgeBlock(
        AssistanceDetectionResult detection,
        AssistantOptions options)
    {
        var relevantSegments = GetRelevantKnowledgeSegments(detection, options);
        return relevantSegments.Count == 0
            ? string.Empty
            : "CONTEXTO LOCAL VERIFICADO (usa solamente estos datos relacionados):\n" +
                string.Join(" ", relevantSegments) + "\n";
    }

    internal static string BuildRetryPrompt(AssistanceDetectionResult detection, AssistantOptions options) =>
        BuildRetrySystemPrompt(detection, options) +
        "\n" +
        BuildQuestionPrompt(detection, options);

    private static string BuildRetrySystemPrompt(
        AssistanceDetectionResult detection,
        AssistantOptions options) =>
        "CORRECCIÓN: la salida anterior fue vacía, irrelevante o incompleta. " +
        "No copies estas instrucciones; responde la pregunta.\n" +
        BuildSystemPrompt(detection, options);

    internal static StructuredInferenceResponse ParseStructuredResponse(
        string rawResponse,
        AssistanceMode mode,
        string providerName)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new InferenceResponseException($"{providerName} returned an empty response.");
        }

        var jsonText = rawResponse.Trim();
        if (jsonText.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            jsonText = jsonText.Substring(7);
        }
        else if (jsonText.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            jsonText = jsonText.Substring(3);
        }

        if (jsonText.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            jsonText = jsonText.Substring(0, jsonText.Length - 3);
        }
        jsonText = jsonText.Trim();

        StructuredInferenceResponse? structured;
        try
        {
            structured = JsonSerializer.Deserialize<StructuredInferenceResponse>(
                jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new InferenceResponseException(
                $"Could not parse the JSON response from {providerName}.",
                ex);
        }

        if (structured == null)
        {
            throw new InferenceResponseException($"{providerName} JSON parsed to null.");
        }

        if (string.IsNullOrWhiteSpace(structured.Answer) &&
            string.IsNullOrWhiteSpace(structured.SuggestedText))
        {
            structured.Answer = ExtractKnownAnswerValue(jsonText);
        }

        if (IsInstructionEcho(structured.Answer) || IsInstructionEcho(structured.SuggestedText))
        {
            throw new InferenceResponseException(
                $"{providerName} echoed the prompt instead of answering.");
        }

        var hasUsableAnswer = !string.IsNullOrWhiteSpace(structured.Answer) ||
            !string.IsNullOrWhiteSpace(structured.SuggestedText) ||
            (mode == AssistanceMode.MultipleChoice && structured.OptionId.HasValue);
        if (!hasUsableAnswer)
        {
            throw new InferenceResponseException(
                $"{providerName} did not return a usable answer.");
        }

        return structured;
    }

    internal static AssistanceOutput MapStructuredResponse(
        string rawResponse,
        StructuredInferenceResponse structured,
        AssistanceDetectionResult detection,
        AssistantOptions options,
        TimeSpan duration)
    {
        if (detection.Mode == AssistanceMode.MultipleChoice ||
            (options.UseVisionModel && structured.Mode == "MultipleChoice"))
        {
            OcrResult? candidate = null;

            if (options.UseVisionModel)
            {
                candidate = detection.OcrResults.FirstOrDefault();
            }
            else
            {
                var optionCandidates = BuildOptionCandidates(detection.OcrResults);

                if (optionCandidates.Count > 0 && !string.IsNullOrWhiteSpace(structured.Answer))
                {
                    candidate = SelectOptionByAnswer(optionCandidates, structured.Answer).Anchor;
                }

                candidate ??= detection.OcrResults.ElementAtOrDefault(structured.OptionId ?? -1);

                if (optionCandidates.Count > 0 && (candidate == null || !IsOptionLine(candidate.Text)))
                {
                    candidate = null;
                }
            }

            if (candidate == null)
            {
                throw new InferenceResponseException(
                    "Could not determine the correct option candidate.");
            }

            return new VisualIndicatorOutput(
                rawResponse,
                structured.Confidence,
                duration,
                candidate.Text,
                new MatchResult(candidate, 1.0, 1.0, MatchMethod.Levenshtein));
        }

        var inputTarget = FindInputTarget(detection.OcrResults);
        double x = options.UseVisionModel && structured.SuggestedX.HasValue
            ? structured.SuggestedX.Value
            : inputTarget.X;
        double y = options.UseVisionModel && structured.SuggestedY.HasValue
            ? structured.SuggestedY.Value
            : inputTarget.Y;

        string textToShow = !string.IsNullOrWhiteSpace(structured.SuggestedText)
            ? structured.SuggestedText
            : structured.Answer ?? throw new InferenceResponseException(
                "The inference provider did not return an open-text answer.");

        return new TextSuggestionOutput(
            rawResponse,
            structured.Confidence,
            duration,
            textToShow,
            x,
            y,
            200,
            50);
    }

    private static OptionCandidate SelectOptionByAnswer(IReadOnlyList<OptionCandidate> options, string answer)
    {
        var trimmedAnswer = answer.Trim();
        if (trimmedAnswer.Length == 1 && char.IsLetter(trimmedAnswer[0]))
        {
            var prefix = char.ToUpperInvariant(trimmedAnswer[0]);
            var byLetter = options.FirstOrDefault(option => option.Label == prefix);
            if (byLetter != null) return byLetter;
        }

        return options
            .OrderBy(option => ComputeLevenshteinDistance(
                StripOptionPrefix(option.Text).ToLowerInvariant(),
                StripOptionPrefix(trimmedAnswer).ToLowerInvariant()))
            .First();
    }

    private static IReadOnlyList<OptionCandidate> BuildOptionCandidates(IReadOnlyList<OcrResult> results)
    {
        var candidates = new List<OptionCandidate>();
        OcrResult? anchor = null;
        var parts = new List<string>();

        foreach (var result in results.OrderBy(result => result.Bounds.Y))
        {
            if (IsOptionLine(result.Text))
            {
                if (anchor != null)
                {
                    var text = NormalizeOptionText(string.Join(" ", parts));
                    candidates.Add(new OptionCandidate(anchor, text, GetOptionLabel(text)));
                }

                anchor = result;
                parts.Clear();
                parts.Add(result.Text.Trim());
            }
            else if (anchor != null)
            {
                parts.Add(result.Text.Trim());
            }
        }

        if (anchor != null)
        {
            var text = NormalizeOptionText(string.Join(" ", parts));
            candidates.Add(new OptionCandidate(anchor, text, GetOptionLabel(text)));
        }

        return candidates;
    }

    private static VisualIndicatorOutput? TrySolveDeterministicMath(AssistanceDetectionResult detection)
    {
        if (detection.Mode != AssistanceMode.MultipleChoice) return null;

        var options = BuildOptionCandidates(detection.OcrResults);
        if (options.Count < 2) return null;

        var firstOptionY = options.Min(option => option.Anchor.Bounds.Y);
        var question = string.Join(" ", detection.OcrResults
            .Where(result => result.Bounds.Y < firstOptionY)
            .Select(result => result.Text.Trim()));
        var fullContext = string.Join(" ", detection.OcrResults
            .Select(result => result.Text.Trim()));

        double? expected = TrySolveRemainingFraction(question) ?? TrySolveExplicitExpression(fullContext);
        if (!expected.HasValue || double.IsNaN(expected.Value) || double.IsInfinity(expected.Value)) return null;

        var matchingOptions = options
            .Select(option => new { Option = option, Value = ExtractFirstNumber(StripOptionPrefix(option.Text)) })
            .Where(item => item.Value.HasValue && Math.Abs(item.Value.Value - expected.Value) < 0.000001)
            .ToList();
        if (matchingOptions.Count != 1) return null;

        var selected = matchingOptions[0].Option;
        var answer = selected.Label?.ToString() ?? selected.Anchor.Text;
        return new VisualIndicatorOutput(
            $"{{\"answer\":\"{answer}\",\"confidence\":1.0,\"source\":\"deterministic-math\"}}",
            1.0,
            TimeSpan.Zero,
            selected.Anchor.Text,
            new MatchResult(selected.Anchor, 1.0, 1.0, MatchMethod.Exact));
    }

    private static VisualIndicatorOutput? TrySelectVerifiedKnowledge(
        AssistanceDetectionResult detection,
        AssistantOptions options)
    {
        if (detection.Mode != AssistanceMode.MultipleChoice ||
            string.IsNullOrWhiteSpace(options.LocalKnowledgeContext))
        {
            return null;
        }

        var optionCandidates = BuildOptionCandidates(detection.OcrResults);
        if (optionCandidates.Count < 2) return null;

        var relevantSegments = GetRelevantKnowledgeSegments(detection, options);
        if (relevantSegments.Count == 0) return null;

        var normalizedKnowledge = NormalizeKnowledgeText(
            string.Join(" ", relevantSegments));
        var exactMatches = optionCandidates
            .Where(option =>
            {
                var optionText = NormalizeKnowledgeText(
                    StripOptionPrefix(option.Text));
                return GetSignificantTokens(optionText).Count >= 3 &&
                    normalizedKnowledge.Contains(
                        optionText,
                        StringComparison.Ordinal);
            })
            .ToList();
        if (exactMatches.Count == 1)
        {
            var selected = exactMatches[0];
            var exactAnswer = selected.Label?.ToString() ?? selected.Anchor.Text;
            return new VisualIndicatorOutput(
                $"{{\"answer\":\"{exactAnswer}\",\"confidence\":1.0,\"source\":\"verified-knowledge-exact\"}}",
                1.0,
                TimeSpan.Zero,
                selected.Anchor.Text,
                new MatchResult(selected.Anchor, 1.0, 1.0, MatchMethod.Exact));
        }

        var contextSegments = relevantSegments
            .Select(GetSignificantTokens)
            .Where(tokens => tokens.Count > 0)
            .ToList();
        if (contextSegments.Count == 0) return null;

        var ranked = optionCandidates
            .Select(option =>
            {
                var optionTokens = GetSignificantTokens(StripOptionPrefix(option.Text));
                var score = optionTokens.Count == 0
                    ? 0
                    : contextSegments.Max(context =>
                        optionTokens.Count(token => context.Contains(token)) / (double)optionTokens.Count);
                return new { Option = option, Score = score };
            })
            .OrderByDescending(item => item.Score)
            .ToList();

        var best = ranked[0];
        var secondScore = ranked.Count > 1 ? ranked[1].Score : 0;
        if (best.Score < 0.75 || best.Score - secondScore < 0.15) return null;

        var answer = best.Option.Label?.ToString() ?? best.Option.Anchor.Text;
        return new VisualIndicatorOutput(
            $"{{\"answer\":\"{answer}\",\"confidence\":{best.Score.ToString("0.00", CultureInfo.InvariantCulture)},\"source\":\"verified-knowledge\"}}",
            best.Score,
            TimeSpan.Zero,
            best.Option.Anchor.Text,
            new MatchResult(best.Option.Anchor, best.Score, best.Score - secondScore, MatchMethod.Exact));
    }

    private static HashSet<string> GetSignificantTokens(string text)
    {
        return KnowledgeTokenRegex.Matches(text)
            .Select(match => RemoveDiacritics(match.Value).ToLowerInvariant())
            .Where(token => token.Length > 1 && !KnowledgeStopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> GetRelevantKnowledgeSegments(
        AssistanceDetectionResult detection,
        AssistantOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LocalKnowledgeContext))
        {
            return Array.Empty<string>();
        }

        var questionTokens = GetSignificantTokens(string.Join(
            " ",
            detection.OcrResults.Select(result => result.Text)));
        if (questionTokens.Count == 0)
        {
            return Array.Empty<string>();
        }

        return Regex.Split(options.LocalKnowledgeContext, @"(?<=[\.!?])\s+")
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0)
            .Where(segment =>
            {
                var segmentTokens = GetSignificantTokens(segment);
                if (segmentTokens.Count == 0)
                {
                    return false;
                }
                var overlap = segmentTokens.Count(questionTokens.Contains);
                return overlap >= 2 &&
                    overlap / (double)segmentTokens.Count >= 0.25;
            })
            .ToList();
    }

    internal static bool IsUnrelatedLocalContextLeak(
        StructuredInferenceResponse response,
        AssistanceDetectionResult detection,
        AssistantOptions options)
    {
        if (GetRelevantKnowledgeSegments(detection, options).Count > 0)
        {
            return false;
        }

        var answer = string.Join(
            " ",
            new[] { response.Answer, response.SuggestedText }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var question = string.Join(
            " ",
            detection.OcrResults.Select(result => result.Text));
        return !LocalContextLeakRegex.IsMatch(question) &&
            LocalContextLeakRegex.IsMatch(answer);
    }

    internal static bool IsIrrelevantOpenAnswer(
        StructuredInferenceResponse response,
        AssistanceDetectionResult detection)
    {
        if (detection.Mode != AssistanceMode.OpenText)
        {
            return false;
        }

        var answer = string.Join(
            " ",
            new[] { response.Answer, response.SuggestedText }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
        if (answer.Length == 0)
        {
            return true;
        }

        var question = string.Join(
            " ",
            detection.OcrResults
                .Where(result => !InputCueRegex.IsMatch(result.Text))
                .Select(result => result.Text));
        if (!OpenAnswerMetaRegex.IsMatch(question) &&
            OpenAnswerMetaRegex.IsMatch(answer))
        {
            return true;
        }

        return RequiresTwoItemsRegex.IsMatch(question) &&
            !TwoItemSeparatorRegex.IsMatch(answer);
    }

    private static string NormalizeKnowledgeText(string text) =>
        Regex.Replace(
            RemoveDiacritics(text).ToLowerInvariant(),
            @"\s+",
            " ").Trim();

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        return new string(normalized
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static double? TrySolveRemainingFraction(string question)
    {
        if (!RemainingQuestionRegex.IsMatch(question) || ExplicitPerPersonRegex.IsMatch(question)) return null;

        var match = TotalThenFractionRegex.Match(question);
        if (!match.Success ||
            !TryParseNumber(match.Groups["total"].Value, out var total) ||
            !TryParseNumber(match.Groups["numerator"].Value, out var numerator) ||
            !TryParseNumber(match.Groups["denominator"].Value, out var denominator) ||
            denominator == 0 || numerator < 0 || numerator > denominator)
        {
            return null;
        }

        return total * (denominator - numerator) / denominator;
    }

    private static double? TrySolveExplicitExpression(string question)
    {
        var matches = ExplicitExpressionRegex.Matches(question);
        if (matches.Count != 1)
        {
            return null;
        }

        var match = matches[0];
        if (!TryParseNumber(match.Groups["left"].Value, out var left) ||
            !TryParseNumber(match.Groups["right"].Value, out var right))
        {
            return null;
        }

        return match.Groups["operator"].Value switch
        {
            "+" => left + right,
            "-" or "−" => left - right,
            "*" or "×" or "x" or "X" => left * right,
            "/" or "÷" when right != 0 => left / right,
            _ => null
        };
    }

    private static double? ExtractFirstNumber(string text)
    {
        var match = NumberRegex.Match(text);
        return match.Success && TryParseNumber(match.Value, out var value) ? value : null;
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);

    private static string? ExtractKnownAnswerValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (KnownAnswerProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool IsInstructionEcho(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;

        return (answer.Contains("return only json", StringComparison.OrdinalIgnoreCase) &&
                answer.Contains("confidence", StringComparison.OrdinalIgnoreCase)) ||
            (answer.Contains("devuelve solo json", StringComparison.OrdinalIgnoreCase) &&
                answer.Contains("confidence", StringComparison.OrdinalIgnoreCase)) ||
            answer.Contains(
                "la respuesta anterior fue vacía, irrelevante o incompleta",
                StringComparison.OrdinalIgnoreCase) ||
            answer.Contains(
                "devuelve solamente el objeto json",
                StringComparison.OrdinalIgnoreCase) ||
            (answer.Contains("response must be in spanish", StringComparison.OrdinalIgnoreCase) &&
                answer.Contains("maximum 25 words", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] KnownAnswerProperties =
        { "respuesta", "response", "result", "text", "output" };

    private sealed record OptionCandidate(OcrResult Anchor, string Text, char? Label);

    private static int FindTargetQuestionLineIndex(IReadOnlyList<OcrResult> results, int? cursorY)
    {
        var numberedQuestion = results
            .Select((result, index) => new { result, index })
            .FirstOrDefault(item => QuestionStartRegex.IsMatch(item.result.Text));
        if (numberedQuestion != null) return numberedQuestion.index;
        if (!cursorY.HasValue || results.Count == 0) return 0;

        return results
            .Select((result, index) => new
            {
                index,
                distance = Math.Abs(result.Bounds.Y + result.Bounds.Height / 2.0 - cursorY.Value)
            })
            .OrderBy(item => item.distance)
            .First().index;
    }

    private static (double X, double Y) FindInputTarget(IReadOnlyList<OcrResult> results)
    {
        // The same wording can appear in the question and in the field placeholder
        // (for example, "Explica brevemente"). Inside an isolated question card the
        // actual input cue is the lower match, after the question text.
        var cue = results
            .Where(result => IsInputCue(result.Text))
            .OrderByDescending(result => result.Bounds.Y)
            .FirstOrDefault();
        if (cue != null)
        {
            return (cue.Bounds.X + Math.Max(20, cue.Bounds.Width / 2.0),
                cue.Bounds.Y + Math.Max(10, cue.Bounds.Height / 2.0));
        }

        var question = results.FirstOrDefault(result => QuestionStartRegex.IsMatch(result.Text))
            ?? results.FirstOrDefault();
        return question == null
            ? (0, 0)
            : (question.Bounds.X + 40, question.Bounds.Y + question.Bounds.Height + 45);
    }

    private static bool IsOptionLine(string text) => OptionRegex.IsMatch(text);

    private static bool IsInputCue(string text) => InputCueRegex.IsMatch(text);

    private static string StripOptionPrefix(string text) => OptionRegex.Replace(NormalizeOptionText(text), string.Empty, 1).Trim();

    private static string NormalizeOptionText(string text)
    {
        var duplicate = DuplicateOptionPrefixRegex.Match(text);
        return duplicate.Success
            ? duplicate.Groups["actual"].Value.ToUpperInvariant() + ") " + text.Substring(duplicate.Length).TrimStart()
            : text.Trim();
    }

    private static char? GetOptionLabel(string text)
    {
        var match = OptionLabelRegex.Match(text);
        return match.Success ? char.ToUpperInvariant(match.Groups["label"].Value[0]) : null;
    }

    private static readonly Regex QuestionStartRegex = new(
        @"^\s*\d{1,3}\s*[\.\)\-:]\s*\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OptionRegex = new(
        @"^\s*(?:[oO0]\s*)?[A-Ea-e]\s*[\.\)\-:]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DuplicateOptionPrefixRegex = new(
        @"^\s*[A-Ea-e]\s*[\.\)]\s*(?<actual>[A-Ea-e])\s*[\.\)]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OptionLabelRegex = new(
        @"^\s*(?:[oO0]\s*)?(?<label>[A-Ea-e])\s*[\.\)\-:]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InputCueRegex = new(
        @"^\s*(?:respuesta\s+corta|" +
        @"escrib(?:e|a)\s+(?:(?:tu|su|una)\s+)?respuesta(?:\s+(?:aquí|breve))?|" +
        @"explica\s+brevemente|" +
        @"escribe\s+tus\s+observaciones(?:\s+detalladas)?|" +
        @"observaciones(?:\s+detalladas)?|" +
        @"short\s+answer|enter\s+your\s+answer|type\s+your\s+answer)" +
        @"(?:\s*\.*)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RemainingQuestionRegex = new(
        @"\b(?:restan|resta|quedan|queda|sobran|sobra)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitPerPersonRegex = new(
        @"\b(?:a|para)\s+cada\s+(?:uno|una|persona|amigo|amiga)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TotalThenFractionRegex = new(
        @"(?<total>\d+(?:[\.,]\d+)?)\D{0,100}(?<numerator>\d+)\s*/\s*(?<denominator>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitExpressionRegex = new(
        @"(?<left>-?\d+(?:[\.,]\d+)?)\s*(?<operator>[+\-−*×xX÷/])\s*(?<right>-?\d+(?:[\.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumberRegex = new(
        @"-?\d+(?:[\.,]\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnowledgeTokenRegex = new(
        @"[\p{L}\p{N}_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LocalContextLeakRegex = new(
        @"\b(?:aivsp|ollama|endpoint|localhost)\b|127\.0\.0\.1|11434",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OpenAnswerMetaRegex = new(
        @"\b(?:modelo de lenguaje|como (?:una? )?ia|contexto de la aplicaci[oó]n|respuesta a preguntas|la pregunta es)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RequiresTwoItemsRegex = new(
        @"\b(?:al menos dos|menciona(?:r)? dos|dos (?:retos|razones|causas|ejemplos|ventajas|desventajas|elementos|puntos))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TwoItemSeparatorRegex = new(
        @"[,;]|\b(?:y|e|adem[aá]s|también)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnowledgeStopWords = new(StringComparer.Ordinal)
    {
        "a", "al", "de", "del", "el", "en", "hacia", "la", "las", "los",
        "para", "por", "puede", "solo", "un", "una", "y"
    };

    private static int ComputeLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return string.IsNullOrEmpty(target) ? 0 : target.Length;
        if (string.IsNullOrEmpty(target)) return source.Length;

        int sourceLength = source.Length;
        int targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        for (int i = 0; i <= sourceLength; distance[i, 0] = i++) { }
        for (int j = 0; j <= targetLength; distance[0, j] = j++) { }

        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }
        return distance[sourceLength, targetLength];
    }
}
