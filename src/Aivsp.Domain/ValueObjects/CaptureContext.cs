using System;

namespace Aivsp.Domain.ValueObjects;

public readonly struct CaptureContext(
    string processName,
    string windowTitle,
    DateTime timestamp,
    int originX = 0,
    int originY = 0)
{
    public string ProcessName { get; } = processName;
    public string WindowTitle { get; } = windowTitle;
    public DateTime Timestamp { get; } = timestamp;
    public int OriginX { get; } = originX;
    public int OriginY { get; } = originY;
}
