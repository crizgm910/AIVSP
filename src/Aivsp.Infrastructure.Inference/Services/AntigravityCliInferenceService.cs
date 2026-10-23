using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;

namespace Aivsp.Infrastructure.Inference.Services;

public sealed class AntigravityCliInferenceService(IAntigravityCliProcessRunner runner) : IInferenceService
{
    public Task<AssistanceOutput> InferActionAsync(string userPrompt, AssistanceDetectionResult detection,
        AssistantOptions options, CancellationToken cancellationToken)
    {
        // Reuse the validated textual prompt, matching and rejection rules, not the
        // Gemini executable or authentication. An adapter pins the selected agy model.
        var service = new GeminiCliInferenceService(new ConfiguredRunner(runner, options));
        return service.InferActionAsync(userPrompt, detection, options, cancellationToken);
    }

    private sealed class ConfiguredRunner(IAntigravityCliProcessRunner runner, AssistantOptions options) : IGeminiCliProcessRunner
    {
        public Task<GeminiCliProcessResult> RunAsync(string executablePath, string prompt,
            string? modelName, CancellationToken cancellationToken) =>
            runner.RunAsync(options.AntigravityCliExecutablePath, prompt, options.AntigravityModelName, cancellationToken);
    }
}
