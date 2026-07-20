using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aivsp.Domain.Entities;

namespace Aivsp.Application.Detection;

/// <summary>
/// Isolates the question card under the cursor so inference does not mix options
/// from neighbouring questions.
/// </summary>
public static partial class QuestionContextSelector
{
    public static IReadOnlyList<OcrResult> Select(
        IReadOnlyList<OcrResult> results,
        int? cursorY)
    {
        if (!cursorY.HasValue || results.Count == 0)
        {
            return results;
        }

        var ordered = results.OrderBy(r => r.Bounds.Y).ToList();
        var nearestIndex = ordered
            .Select((result, index) => new
            {
                Index = index,
                Distance = Math.Abs(CenterY(result) - cursorY.Value)
            })
            .OrderBy(item => item.Distance)
            .First().Index;

        var startIndex = -1;
        for (var index = nearestIndex; index >= 0; index--)
        {
            if (QuestionStartRegex().IsMatch(ordered[index].Text))
            {
                startIndex = index;
                break;
            }
        }

        if (startIndex >= 0)
        {
            var endIndex = ordered.Count;
            for (var index = startIndex + 1; index < ordered.Count; index++)
            {
                if (QuestionStartRegex().IsMatch(ordered[index].Text))
                {
                    endIndex = index;
                    break;
                }
            }

            return ordered.Skip(startIndex).Take(endIndex - startIndex).ToList();
        }

        // Forms without numbered questions: keep only the local vertical region.
        return ordered
            .Where(result => Math.Abs(CenterY(result) - cursorY.Value) <= 260)
            .Take(12)
            .ToList();
    }

    private static double CenterY(OcrResult result) =>
        result.Bounds.Y + result.Bounds.Height / 2.0;

    [GeneratedRegex(@"^\s*\d{1,3}\s*[\.\)\-:]\s*\S", RegexOptions.CultureInvariant)]
    private static partial Regex QuestionStartRegex();
}
