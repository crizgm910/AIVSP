using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.Detection;

public class QuestionTypeDetector : IQuestionTypeDetector
{
    public AssistanceDetectionResult Detect(IReadOnlyList<OcrResult> ocrResults, CapturedImage image)
    {
        if (ocrResults == null || ocrResults.Count == 0)
        {
            return new AssistanceDetectionResult(AssistanceMode.Unknown, ocrResults ?? new List<OcrResult>(), image);
        }

        var fullText = string.Join("\n", ocrResults.Select(r => r.Text));
        var lowerText = fullText.ToLowerInvariant();

        // A visible option structure is stronger evidence than words such as
        // "escribe" or "explica" inside the question itself.
        var strongOptionCount = ocrResults.Count(result => StrongOptionPattern.IsMatch(result.Text));
        if (strongOptionCount >= 2)
        {
            return new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, image);
        }

        // Basic heuristics for OpenText
        string[] openTextKeywords = { "escribe", "desarrolla", "explica", "describe", "justifica", "ingresa tu respuesta" };
        if (openTextKeywords.Any(k => lowerText.Contains(k)))
        {
            return new AssistanceDetectionResult(AssistanceMode.OpenText, ocrResults, image);
        }

        // Basic heuristics for MultipleChoice
        var mcqPattern = new Regex(@"([a-eA-E]\s*[\.\-\)])|(o\s+[a-eA-E]\s*[\.\-\)])|([O0]\s+[a-eA-E]\s*[\.\-\)])|(opci[oó]n\s+[a-eA-E])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var bulletMatches = mcqPattern.Matches(fullText);
        
        if (bulletMatches.Count >= 2 || lowerText.Contains("selecciona") || lowerText.Contains("elige la") || lowerText.Contains("opción correcta"))
        {
            return new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, image);
        }

        // Default fallback to OpenText if not explicitly multiple choice
        return new AssistanceDetectionResult(AssistanceMode.OpenText, ocrResults, image);
    }

    private static readonly Regex StrongOptionPattern = new(
        @"^\s*(?:[oO0]\s*)?[A-Ea-e]\s*[\.\-\):]\s*\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
