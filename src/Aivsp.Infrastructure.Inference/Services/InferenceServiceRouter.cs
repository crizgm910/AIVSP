using System;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;

namespace Aivsp.Infrastructure.Inference.Services;

public sealed class InferenceServiceRouter : IInferenceService
{
    private readonly OllamaInferenceService _ollama;
    private readonly GeminiCliInferenceService _geminiCli;

    public InferenceServiceRouter(
        OllamaInferenceService ollama,
        GeminiCliInferenceService geminiCli)
    {
        _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
        _geminiCli = geminiCli ?? throw new ArgumentNullException(nameof(geminiCli));
    }

    public Task<AssistanceOutput> InferActionAsync(
        string userPrompt,
        AssistanceDetectionResult detection,
        AssistantOptions options,
        CancellationToken cancellationToken) =>
        options.InferenceProvider switch
        {
            InferenceProvider.Ollama =>
                _ollama.InferActionAsync(userPrompt, detection, options, cancellationToken),
            InferenceProvider.GeminiCli =>
                _geminiCli.InferActionAsync(userPrompt, detection, options, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported inference provider: {options.InferenceProvider}.")
        };
}
