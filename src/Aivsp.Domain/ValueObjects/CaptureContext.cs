using System;

namespace Aivsp.Domain.ValueObjects;

public readonly struct CaptureContext(
    string processName,
    string windowTitle,
    DateTime timestamp,
    int originX = 0,
    int originY = 0,
    long windowHandle = 0,
    int processId = 0,
    long focusedControlHandle = 0)
{
    public string ProcessName { get; } = processName;
    public string WindowTitle { get; } = windowTitle;
    public DateTime Timestamp { get; } = timestamp;
    public int OriginX { get; } = originX;
    public int OriginY { get; } = originY;
    public long WindowHandle { get; } = windowHandle;
    public int ProcessId { get; } = processId;
    public long FocusedControlHandle { get; } = focusedControlHandle;
}
