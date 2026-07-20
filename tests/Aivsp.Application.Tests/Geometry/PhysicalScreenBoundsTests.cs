using System;
using Aivsp.Application.Geometry;
using Xunit;

namespace Aivsp.Application.Tests.Geometry;

public class PhysicalScreenBoundsTests
{
    [Fact]
    public void Contains_NegativeOrigin_UsesHalfOpenScreenBounds()
    {
        var screen = new PhysicalScreenBounds(-1920, -100, 1920, 1080);

        Assert.True(screen.Contains(-1920, -100));
        Assert.True(screen.Contains(-1, 979));
        Assert.False(screen.Contains(0, 0));
        Assert.False(screen.Contains(-1920, 980));
    }

    [Fact]
    public void DistanceSquaredTo_PointOutside_ReturnsDistanceToNearestPixel()
    {
        var screen = new PhysicalScreenBounds(100, 100, 100, 100);

        Assert.Equal(0, screen.DistanceSquaredTo(150, 150));
        Assert.Equal(100, screen.DistanceSquaredTo(90, 150));
        Assert.Equal(242, screen.DistanceSquaredTo(210, 210));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void Constructor_InvalidDimensions_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PhysicalScreenBounds(0, 0, width, height));
    }
}
