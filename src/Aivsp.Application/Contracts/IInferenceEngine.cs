using System.Threading;
using System.Threading.Tasks;
using Aivsp.Domain.Entities;

namespace Aivsp.Application.Contracts;

public class InferenceRequest
{
    public string Prompt { get; }
    
    public InferenceRequest(string prompt)
    {
        Prompt = prompt;
    }
}

public interface IInferenceEngine
{
    Task<InferenceResult> AnalyzeAsync(CapturedImage image, InferenceRequest request, CancellationToken cancellationToken);
}
