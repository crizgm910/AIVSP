using System.Text.Json.Serialization;
using Aivsp.Infrastructure.Inference.Models;

namespace Aivsp.Infrastructure.Inference.Serialization;

[JsonSerializable(typeof(OllamaRequest))]
[JsonSerializable(typeof(OllamaResponse))]
[JsonSerializable(typeof(OllamaRequestOptions))]
public partial class OllamaJsonContext : JsonSerializerContext
{
}
