using System.Threading;
using System.Threading.Tasks;
using Aivsp.Domain.Entities;

namespace Aivsp.Application.Contracts;

public interface IImageCaptureService
{
    ValueTask<CapturedImage> CaptureActiveWindowAsync(CancellationToken cancellationToken);
}
