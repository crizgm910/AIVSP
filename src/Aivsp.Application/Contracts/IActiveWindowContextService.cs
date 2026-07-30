using Aivsp.Domain.ValueObjects;

namespace Aivsp.Application.Contracts;

public interface IActiveWindowContextService
{
    ActiveWindowIdentity GetActiveWindowIdentity();
}
