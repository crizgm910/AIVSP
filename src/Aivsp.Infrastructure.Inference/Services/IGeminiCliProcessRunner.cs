using System.Threading;
using System.Threading.Tasks;

namespace Aivsp.Infrastructure.Inference.Services;

public interface IGeminiCliProcessRunner
{
    Task<GeminiCliProcessResult> RunAsync(
        string executablePath,
        string prompt,
        string? modelName,
        CancellationToken cancellationToken);
}

public sealed record GeminiCliProcessResult(int ExitCode, string StandardOutput, string StandardError);
