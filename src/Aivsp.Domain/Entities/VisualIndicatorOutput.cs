using System;

namespace Aivsp.Domain.Entities;

public class VisualIndicatorOutput : AssistanceOutput
{
    public string SelectedText { get; }
    public MatchResult Match { get; }

    public VisualIndicatorOutput(string rawResponse, double confidence, TimeSpan duration, string selectedText, MatchResult match)
        : base(rawResponse, confidence, duration)
    {
        SelectedText = selectedText;
        Match = match;
    }
}
