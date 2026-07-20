using System;

namespace Aivsp.Domain.Entities;

public class TextSuggestionOutput : AssistanceOutput
{
    public string SuggestedText { get; }
    public double SuggestedX { get; }
    public double SuggestedY { get; }
    public double SuggestedWidth { get; }
    public double SuggestedHeight { get; }

    public TextSuggestionOutput(string rawResponse, double confidence, TimeSpan duration, string suggestedText, double x, double y, double width, double height) 
        : base(rawResponse, confidence, duration)
    {
        SuggestedText = suggestedText;
        SuggestedX = x;
        SuggestedY = y;
        SuggestedWidth = width;
        SuggestedHeight = height;
    }
}
