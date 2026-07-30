using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;

namespace Aivsp.Application.Contracts;

public interface IOcrEngine
{
    Task<IReadOnlyList<OcrResult>> ExtractTextAsync(CapturedImage image, AssistantOptions options, CancellationToken cancellationToken);
}
