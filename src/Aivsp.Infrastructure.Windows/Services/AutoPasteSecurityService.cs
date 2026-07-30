using System;
using Aivsp.Application.AutoPaste;
using Aivsp.Application.Contracts;

namespace Aivsp.Infrastructure.Windows.Services;

public sealed class AutoPasteSecurityService : IAutoPasteSecurityService
{
    private readonly IActiveWindowContextService _activeWindowContextService;
    private readonly TimeProvider _timeProvider;

    public AutoPasteSecurityService(
        IActiveWindowContextService activeWindowContextService,
        TimeProvider? timeProvider = null)
    {
        _activeWindowContextService = activeWindowContextService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AutoPasteValidationResult Validate(AutoPasteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Response))
        {
            return AutoPasteValidationResult.Cancelled(AutoPasteCancelledReason.EmptyResponse);
        }

        if (!double.IsFinite(request.Confidence) || request.Confidence < request.MinimumConfidence)
        {
            return AutoPasteValidationResult.Cancelled(AutoPasteCancelledReason.LowConfidence);
        }

        var capturedAtUtc = request.CaptureContext.Timestamp.Kind == DateTimeKind.Utc
            ? request.CaptureContext.Timestamp
            : request.CaptureContext.Timestamp.ToUniversalTime();
        var age = _timeProvider.GetUtcNow().UtcDateTime - capturedAtUtc;
        if (request.MaximumContextAge <= TimeSpan.Zero || age < TimeSpan.Zero || age > request.MaximumContextAge)
        {
            return AutoPasteValidationResult.Cancelled(AutoPasteCancelledReason.ContextExpired);
        }

        var active = _activeWindowContextService.GetActiveWindowIdentity();
        if (request.CaptureContext.WindowHandle == 0 ||
            active.WindowHandle == 0 ||
            active.WindowHandle != request.CaptureContext.WindowHandle)
        {
            return AutoPasteValidationResult.Cancelled(AutoPasteCancelledReason.WindowChanged);
        }

        if (request.CaptureContext.ProcessId <= 0 ||
            active.ProcessId <= 0 ||
            active.ProcessId != request.CaptureContext.ProcessId)
        {
            return AutoPasteValidationResult.Cancelled(AutoPasteCancelledReason.ProcessChanged);
        }

        if (request.CaptureContext.FocusedControlHandle == 0 ||
            active.FocusedControlHandle == 0 ||
            active.FocusedControlHandle != request.CaptureContext.FocusedControlHandle)
        {
            return AutoPasteValidationResult.Cancelled(AutoPasteCancelledReason.FocusChanged);
        }

        return AutoPasteValidationResult.Allowed();
    }
}
