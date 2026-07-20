using System.Text.Json.Serialization;

namespace Aivsp.Infrastructure.Inference.Models;

public class StructuredInferenceResponse
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("optionId")]
    public int? OptionId { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("suggestedText")]
    public string? SuggestedText { get; set; }
    
    [JsonPropertyName("suggestedX")]
    public double? SuggestedX { get; set; }
    
    [JsonPropertyName("suggestedY")]
    public double? SuggestedY { get; set; }

    [JsonPropertyName("debugReason")]
    public string DebugReason { get; set; } = string.Empty;
}
