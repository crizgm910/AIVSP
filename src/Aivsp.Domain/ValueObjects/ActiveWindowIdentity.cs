namespace Aivsp.Domain.ValueObjects;

public readonly record struct ActiveWindowIdentity(
    long WindowHandle,
    int ProcessId,
    long FocusedControlHandle);
