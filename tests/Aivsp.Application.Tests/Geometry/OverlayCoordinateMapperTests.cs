using System;
using Aivsp.Application.Geometry;
using Xunit;

namespace Aivsp.Application.Tests.Geometry;

public class OverlayCoordinateMapperTests
{
    [Fact]
    public void ScreenToOverlay_PrimaryMonitorAtOneHundredPercent_PreservesCoordinates()
    {
        var result = OverlayCoordinateMapper.ScreenToOverlay(640, 480, 0, 0, 1.0, 1.0);

        Assert.Equal(640, result.X);
        Assert.Equal(480, result.Y);
    }

    [Fact]
    public void ScreenToOverlay_NegativeVirtualOrigin_ReturnsLocalCoordinates()
    {
        var result = OverlayCoordinateMapper.ScreenToOverlay(-1600, 200, -1920, -100, 1.0, 1.0);

        Assert.Equal(320, result.X);
        Assert.Equal(300, result.Y);
    }

    [Theory]
    [InlineData(1.25, 1000, 800, 800, 640)]
    [InlineData(1.50, 1200, 900, 800, 600)]
    public void ScreenToOverlay_ScaledDesktop_ConvertsPixelsToDips(
        double scale,
        double screenX,
        double screenY,
        double expectedX,
        double expectedY)
    {
        var result = OverlayCoordinateMapper.ScreenToOverlay(screenX, screenY, 0, 0, scale, scale);

        Assert.Equal(expectedX, result.X, precision: 6);
        Assert.Equal(expectedY, result.Y, precision: 6);
    }

    [Fact]
    public void PixelsToDips_MixedDpiScales_ConvertsEachAxis()
    {
        var result = OverlayCoordinateMapper.PixelsToDips(250, 300, 1.25, 1.5);

        Assert.Equal(200, result.Width);
        Assert.Equal(200, result.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ScreenToOverlay_InvalidScale_Throws(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayCoordinateMapper.ScreenToOverlay(0, 0, 0, 0, scale, 1));
    }
}
