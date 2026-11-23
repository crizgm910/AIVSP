using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;

namespace Aivsp.Application.BrowserIntegration;

public interface IBrowserAdvancedInferenceService
{
    Task<BrowserInferenceAnswer> InferAsync(BrowserQuestionRequest request, AssistantOptions options, CancellationToken cancellationToken);
}
