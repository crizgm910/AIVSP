using System;
using System.Threading;
using System.Windows.Threading;
using Aivsp.Application.Configuration;
using Aivsp.Application.Geometry;
using Aivsp.Presentation.Wpf.UI;

namespace Aivsp.Infrastructure.Windows.Tests;

public sealed class OverlayWindowLifecycleTests
{
    [Fact]
    [Trait("Category", "InteractiveWindows")]
    public void HideAll_HidesTheNativeOverlayWindow()
    {
        RunInSta(() =>
        {
            var window = CreateOffscreenWindow();
            try
            {
                window.Show();

                Assert.True(window.IsVisible);

                window.HideAll();

                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    [Trait("Category", "InteractiveWindows")]
    public void IndicatorCompletion_HidesTheNativeOverlayWindow()
    {
        RunInSta(() =>
        {
            var window = CreateOffscreenWindow();
            try
            {
                window.Show();
                window.HideAll();

                window.ShowIndicator(-9_990, -9_990, 100, 40, 10);
                Assert.True(window.IsVisible);

                PumpDispatcher(TimeSpan.FromMilliseconds(150));

                Assert.False(window.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static OverlayWindow CreateOffscreenWindow() =>
        new(
            new PhysicalScreenBounds(-10_000, -10_000, 320, 240),
            new AssistantOptions());

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The STA overlay test timed out.");
        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
    }
}
