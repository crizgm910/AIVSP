using Aivsp.Application.AutoPaste;

namespace Aivsp.Application.Contracts;

public interface IAutoPasteSecurityService
{
    AutoPasteValidationResult Validate(AutoPasteRequest request);
}
