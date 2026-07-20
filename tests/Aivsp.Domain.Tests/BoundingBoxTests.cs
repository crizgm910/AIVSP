using System;
using Xunit;
using Aivsp.Domain.ValueObjects;

namespace Aivsp.Domain.Tests;

public class BoundingBoxTests
{
    [Fact]
    public void Center_ReturnsCorrectCoordinate()
    {
        var box = new BoundingBox(10, 20, 100, 50);
        var center = box.Center;

        Assert.Equal(60, center.X); // 10 + 100 / 2
        Assert.Equal(45, center.Y); // 20 + 50 / 2
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-10, 50)]
    [InlineData(50, 0)]
    [InlineData(50, -10)]
    public void Constructor_InvalidDimensions_ThrowsArgumentOutOfRangeException(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundingBox(0, 0, width, height));
    }
}
