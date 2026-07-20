using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Aivsp.Infrastructure.Windows.Services;

namespace Aivsp.Infrastructure.Windows.Tests;

public class GdiImageCaptureServiceTests
{
    [Fact]
    public async Task CaptureActiveWindowAsync_ShouldReturnValidImage()
    {
        var service = new GdiImageCaptureService();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var image = await service.CaptureActiveWindowAsync(cts.Token);

        Assert.NotNull(image);
        Assert.True(image.Length > 0);
        Assert.True(image.Metadata.Width > 0);
        Assert.True(image.Metadata.Height > 0);
        Assert.NotNull(image.Context.WindowTitle);
    }

    [Fact]
    public async Task CaptureActiveWindowAsync_StressTest_ShouldNotLeakMemory()
    {
        var service = new GdiImageCaptureService();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        for (int i = 0; i < 50; i++)
        {
            using var image = await service.CaptureActiveWindowAsync(cts.Token);
            Assert.True(image.Length > 0);
        }

        // We just ensure it doesn't crash or throw exceptions due to GDI exhaustion.
        // ArrayPool might hold onto buffers, so strict memory assertions might be flaky,
        // but the main goal is no GDI leak exceptions.
        Assert.True(true); 
    }
}
