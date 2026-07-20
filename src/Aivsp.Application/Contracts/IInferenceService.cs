using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.Contracts;

public interface IInferenceService
{
    Task<AssistanceOutput> InferActionAsync(string userPrompt, AssistanceDetectionResult detection, AssistantOptions options, CancellationToken cancellationToken);
}
