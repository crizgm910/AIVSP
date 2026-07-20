using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;

namespace Aivsp.Domain.Entities;

public class OcrResult
{
    public string Text { get; }
    public BoundingBox Bounds { get; }
    public double Confidence { get; }
    public OcrResultLevel Level { get; }

    public OcrResult(string text, BoundingBox bounds, double confidence, OcrResultLevel level)
    {
        Text = text;
        Bounds = bounds;
        Confidence = confidence;
        Level = level;
    }
}
