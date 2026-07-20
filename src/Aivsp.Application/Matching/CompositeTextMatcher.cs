using System;
using System.Collections.Generic;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.Matching;

public class CompositeTextMatcher : ITextMatcher
{
    public MatchResult? FindBestMatch(InferenceResult inference, IReadOnlyList<OcrResult> candidates, AssistantOptions options)
    {
        if (string.IsNullOrWhiteSpace(inference.SelectedText) || candidates.Count == 0)
            return null;

        var normalizedTarget = TextNormalizer.Normalize(inference.SelectedText);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
            return null;

        MatchResult? bestMatch = null;
        MatchResult? secondBestMatch = null;

        foreach (var candidate in candidates)
        {
            var normalizedCandidate = TextNormalizer.Normalize(candidate.Text);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
                continue;

            double score = CalculateSimilarity(normalizedTarget, normalizedCandidate);
            
            if (bestMatch == null || score > bestMatch.Score)
            {
                secondBestMatch = bestMatch;
                bestMatch = new MatchResult(candidate, score, 0, MatchMethod.Levenshtein);
            }
            else if (secondBestMatch == null || score > secondBestMatch.Score)
            {
                secondBestMatch = new MatchResult(candidate, score, 0, MatchMethod.Levenshtein);
            }
        }

        if (bestMatch != null && bestMatch.Score >= options.MinimumMatchScore)
        {
            double margin = bestMatch.Score - (secondBestMatch?.Score ?? 0);
            if (margin >= options.MinimumMatchMargin)
            {
                return new MatchResult(bestMatch.Candidate, bestMatch.Score, margin, MatchMethod.Levenshtein);
            }
        }

        return null;
    }

    private static double CalculateSimilarity(string source, string target)
    {
        if ((source == null) || (target == null)) return 0.0;
        if ((source.Length == 0) || (target.Length == 0)) return 0.0;
        if (source == target) return 1.0;

        int stepsToSame = ComputeLevenshteinDistance(source, target);
        return 1.0 - ((double)stepsToSame / Math.Max(source.Length, target.Length));
    }

    private static int ComputeLevenshteinDistance(string source, string target)
    {
        int n = source.Length;
        int m = target.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
