namespace Aivsp.Application.AutoPaste;

public enum AutoPasteCancelledReason
{
    None,
    WindowChanged,
    ProcessChanged,
    FocusChanged,
    ContextExpired,
    EmptyResponse,
    LowConfidence
}
