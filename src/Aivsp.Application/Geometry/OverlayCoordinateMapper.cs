using System;

namespace Aivsp.Application.Geometry;

public readonly record struct OverlayPoint(double X, double Y);

public readonly record struct OverlaySize(double Width, double Height);

public static class OverlayCoordinateMapper
{
    public static OverlayPoint ScreenToOverlay(
        double screenX,
        double screenY,
        double overlayOriginXInPixels,
        double overlayOriginYInPixels,
        double dpiScaleX,
        double dpiScaleY)
    {
        ValidateScale(dpiScaleX, nameof(dpiScaleX));
        ValidateScale(dpiScaleY, nameof(dpiScaleY));

        return new OverlayPoint(
            (screenX - overlayOriginXInPixels) / dpiScaleX,
            (screenY - overlayOriginYInPixels) / dpiScaleY);
    }

    public static OverlaySize PixelsToDips(
        double widthInPixels,
        double heightInPixels,
        double dpiScaleX,
        double dpiScaleY)
    {
        if (widthInPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(widthInPixels));
        if (heightInPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(heightInPixels));

        ValidateScale(dpiScaleX, nameof(dpiScaleX));
        ValidateScale(dpiScaleY, nameof(dpiScaleY));

        return new OverlaySize(widthInPixels / dpiScaleX, heightInPixels / dpiScaleY);
    }

    private static void ValidateScale(double scale, string parameterName)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "DPI scale must be a finite value greater than zero.");
    }
}
