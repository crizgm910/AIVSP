using System;

namespace Aivsp.Domain.Entities;

public class InferenceResult
{
    public string RawResponse { get; }
    public string SelectedText { get; }
    public string? OptionId { get; }
    public double Confidence { get; }
    public TimeSpan Duration { get; }

    public InferenceResult(string rawResponse, string selectedText, double confidence, TimeSpan duration, string? optionId = null)
    {
        RawResponse = rawResponse;
        SelectedText = selectedText;
        Confidence = confidence;
        Duration = duration;
        OptionId = optionId;
    }
}
