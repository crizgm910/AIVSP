using System;

namespace Aivsp.Application.Geometry;

public readonly record struct PhysicalScreenBounds
{
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }

    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public PhysicalScreenBounds(int left, int top, int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;

    public long DistanceSquaredTo(int x, int y)
    {
        long closestX = Math.Clamp(x, Left, Right - 1);
        long closestY = Math.Clamp(y, Top, Bottom - 1);
        long deltaX = x - closestX;
        long deltaY = y - closestY;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
