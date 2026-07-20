using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Domain.Entities;
using Aivsp.Application.Configuration;

namespace Aivsp.Application.Contracts;

public interface IOcrEngine
{
    Task<IReadOnlyList<OcrResult>> ExtractTextAsync(CapturedImage image, AssistantOptions options, CancellationToken cancellationToken);
}
