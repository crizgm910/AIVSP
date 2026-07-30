using System.Text.Json.Serialization;

namespace Aivsp.Infrastructure.Inference.Models;

public class OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; set; }

    [JsonPropertyName("think")]
    public bool? Think { get; set; }

    [JsonPropertyName("images")]
    public string[]? Images { get; set; }

    [JsonPropertyName("options")]
    public OllamaRequestOptions? Options { get; set; }
}

public class OllamaRequestOptions
{
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }
}
