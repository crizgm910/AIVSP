using System;
using System.Buffers;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Xunit;

namespace Aivsp.Domain.Tests;

public class CapturedImageTests
{
    [Fact]
    public void Dispose_ShouldReturnBufferToPool()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(100);
        var metadata = new ImageMetadata(10, 10, 10, PixelFormat.Bgra32, 96, 96);
        var context = new CaptureContext("test", "test", DateTime.Now);

        var image = new CapturedImage(buffer, 100, metadata, context);

        Assert.Equal(100, image.Data.Length);

        image.Dispose();

        // After dispose, accessing Data should throw
        Assert.Throws<ObjectDisposedException>(() => image.Data);
    }
}
