using Aivsp.Domain.Enums;

namespace Aivsp.Domain.Entities;

public class MatchResult
{
    public OcrResult Candidate { get; }
    public double Score { get; }
    public double Margin { get; }
    public MatchMethod Method { get; }

    public MatchResult(OcrResult candidate, double score, double margin, MatchMethod method)
    {
        Candidate = candidate;
        Score = score;
        Margin = margin;
        Method = method;
    }
}
