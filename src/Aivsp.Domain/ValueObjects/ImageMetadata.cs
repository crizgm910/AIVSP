using Aivsp.Domain.Enums;

namespace Aivsp.Domain.ValueObjects;

public readonly struct ImageMetadata(int width, int height, int stride, PixelFormat pixelFormat, float dpiX, float dpiY)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = stride;
    public PixelFormat PixelFormat { get; } = pixelFormat;
    public float DpiX { get; } = dpiX;
    public float DpiY { get; } = dpiY;
}
