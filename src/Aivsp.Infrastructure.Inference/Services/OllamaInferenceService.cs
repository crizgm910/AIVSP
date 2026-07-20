using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
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
        var requestDto = new OllamaRequest
        {
            Model = options.ModelName,
            Stream = false,
            Format = "json", // Force JSON output for structural parsing
            KeepAlive = "10m"
        };

        if (options.UseVisionModel)
        {
            // VLM branch: send image and ask for coordinates directly
            requestDto.Prompt = $"You are a visual assistant. User prompt: {userPrompt}. Return a JSON object with 'mode': 'MultipleChoice' or 'OpenText', 'suggestedX' (number), 'suggestedY' (number).";
            
            // Convert to base64
            string base64Image = Convert.ToBase64String(detection.Image.Data.ToArray());
            requestDto.Images = new[] { base64Image };
        }
        else
        {
            requestDto.Prompt = BuildCompactPrompt(detection, options);
            requestDto.Options = new OllamaRequestOptions
            {
                NumPredict = detection.Mode == AssistanceMode.MultipleChoice ? 16 : 80,
                Temperature = 0.1
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var requestContent = JsonContent.Create(requestDto, OllamaJsonContext.Default.OllamaRequest);

        try
        {
            using var response = await _httpClient.PostAsync($"{options.OllamaEndpoint.TrimEnd('/')}/api/generate", requestContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseDto = await response.Content.ReadFromJsonAsync(OllamaJsonContext.Default.OllamaResponse, cancellationToken);
            stopwatch.Stop();

            if (responseDto == null || string.IsNullOrWhiteSpace(responseDto.Response))
            {
                throw new Exception("Ollama returned an empty response.");
            }

            var jsonText = responseDto.Response.Trim();
            if (jsonText.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) jsonText = jsonText.Substring(7);
            if (jsonText.StartsWith("```", StringComparison.OrdinalIgnoreCase)) jsonText = jsonText.Substring(3);
            if (jsonText.EndsWith("```", StringComparison.OrdinalIgnoreCase)) jsonText = jsonText.Substring(0, jsonText.Length - 3);
            jsonText = jsonText.Trim();

            StructuredInferenceResponse? structured = null;
            try 
            {
                structured = JsonSerializer.Deserialize<StructuredInferenceResponse>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new Exception($"Could not parse JSON response from Ollama. Cleaned Text: {jsonText}. Error: {ex.Message}");
            }
            
            if (structured == null)
            {
                 throw new Exception("JSON parsed to null.");
            }

            if (string.IsNullOrWhiteSpace(structured.Answer) &&
                string.IsNullOrWhiteSpace(structured.SuggestedText))
            {
                structured.Answer = ExtractFirstAnswerValue(jsonText);
            }
            
            // Map structured response to AssistanceOutput
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

                    // The text answer is more reliable than a small model's numeric
                    // index. Ollama occasionally returns the right text with an
                    // off-by-one optionId.
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
                
                if (candidate == null) throw new Exception("Could not determine the correct option candidate.");
                
                return new VisualIndicatorOutput(
                    responseDto.Response,
                    structured.Confidence,
                    stopwatch.Elapsed,
                    candidate.Text,
                    new MatchResult(candidate, 1.0, 1.0, MatchMethod.Levenshtein)
                );
            }
            else
            {
                // OpenText or VLM coordinate mode
                var inputTarget = FindInputTarget(detection.OcrResults);
                double x = options.UseVisionModel && structured.SuggestedX.HasValue
                    ? structured.SuggestedX.Value
                    : inputTarget.X;
                double y = options.UseVisionModel && structured.SuggestedY.HasValue
                    ? structured.SuggestedY.Value
                    : inputTarget.Y;
                
                string textToShow = !string.IsNullOrWhiteSpace(structured.SuggestedText) 
                    ? structured.SuggestedText 
                    : structured.Answer ?? throw new Exception("Ollama did not return an open-text answer.");

                return new TextSuggestionOutput(
                    responseDto.Response,
                    structured.Confidence,
                    stopwatch.Elapsed,
                    textToShow,
                    x, y, 200, 50
                );
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            throw new Exception($"Failed to infer action. Model: {options.ModelName}. Error: {ex.Message}", ex);
        }
    }

    private static string BuildCompactPrompt(AssistanceDetectionResult detection, AssistantOptions options)
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

            return "Choose the correct option. Return only its single letter as JSON: " +
                "{\"answer\":\"A\"}.\n" +
                string.Join("\n", lines);
        }

        var languageInstruction = options.OcrLanguage.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? "Responde exclusivamente en español. No uses portugués. "
            : $"Answer exclusively in the language identified by locale {options.OcrLanguage}. ";

        return languageInstruction + "Usa una oración directa de máximo 25 palabras. " +
            "Return only JSON: {\"answer\":\"answer\"}.\n" +
            string.Join("\n", lines.Where(line => !InputCueRegex.IsMatch(line)));
    }

    private static OptionCandidate SelectOptionByAnswer(IReadOnlyList<OptionCandidate> options, string answer)
    {
        var trimmedAnswer = answer.Trim();
        if (trimmedAnswer.Length == 1 && char.IsLetter(trimmedAnswer[0]))
        {
            var prefix = char.ToUpperInvariant(trimmedAnswer[0]);
            var byLetter = options.FirstOrDefault(option =>
                char.ToUpperInvariant(option.Anchor.Text.TrimStart().TrimStart('o', 'O', '0', ' ').FirstOrDefault()) == prefix);
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
                    candidates.Add(new OptionCandidate(anchor, string.Join(" ", parts)));
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
            candidates.Add(new OptionCandidate(anchor, string.Join(" ", parts)));
        }

        return candidates;
    }

    private static string? ExtractFirstAnswerValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FindString(document.RootElement);
    }

    private static string? FindString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("mode", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("debugReason", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = FindString(property.Value);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var value = FindString(item);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private sealed record OptionCandidate(OcrResult Anchor, string Text);

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
        var cue = results.FirstOrDefault(result => IsInputCue(result.Text));
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

    private static string StripOptionPrefix(string text) => OptionRegex.Replace(text, string.Empty, 1).Trim();

    private static readonly Regex QuestionStartRegex = new(
        @"^\s*\d{1,3}\s*[\.\)\-:]\s*\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OptionRegex = new(
        @"^\s*(?:[oO0]\s*)?[A-Ea-e]\s*[\.\)\-:]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InputCueRegex = new(
        @"respuesta\s+corta|escribe\s+(?:tu\s+)?respuesta|explica\s+brevemente|observaciones",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
