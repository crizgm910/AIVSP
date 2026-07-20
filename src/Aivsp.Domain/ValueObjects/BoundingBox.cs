using System;

namespace Aivsp.Domain.ValueObjects;

public readonly struct BoundingBox
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public BoundingBox(int x, int y, int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Coordinate Center => new Coordinate(X + Width / 2, Y + Height / 2);

    public override string ToString() => $"[{X}, {Y}, {Width}x{Height}]";
}
