namespace Aivsp.Application.AutoPaste;

public readonly record struct AutoPasteValidationResult(
    bool CanPaste,
    AutoPasteCancelledReason CancelledReason)
{
    public static AutoPasteValidationResult Allowed() =>
        new(true, AutoPasteCancelledReason.None);

    public static AutoPasteValidationResult Cancelled(AutoPasteCancelledReason reason) =>
        new(false, reason);
}
