using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Aivsp.Application.Geometry;

namespace Aivsp.Presentation.Wpf.UI;

public partial class OverlayWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    public PhysicalScreenBounds PhysicalBounds { get; }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

    public OverlayWindow(PhysicalScreenBounds physicalBounds)
    {
        PhysicalBounds = physicalBounds;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);

        ConfigureMonitorBounds(hwnd);
    }

    public void ShowIndicator(int x, int y, int width, int height, int durationMs)
    {
        var context = GetCoordinateContext();
        var point = OverlayCoordinateMapper.ScreenToOverlay(
            x,
            y,
            context.WindowBounds.Left,
            context.WindowBounds.Top,
            context.DpiScaleX,
            context.DpiScaleY);
        var size = OverlayCoordinateMapper.PixelsToDips(
            width,
            height,
            context.DpiScaleX,
            context.DpiScaleY);

        double actualX = Math.Clamp(point.X, 0, Math.Max(0, ActualWidth - size.Width));
        double actualY = Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight - size.Height));

        Canvas.SetLeft(IndicatorBorder, actualX);
        Canvas.SetTop(IndicatorBorder, actualY);
        IndicatorBorder.Width = Math.Min(size.Width, ActualWidth);
        IndicatorBorder.Height = Math.Min(size.Height, ActualHeight);

        StartFadeOut(IndicatorBorder, durationMs);
    }

    public void ShowTextSuggestion(string text, int x, int y, int durationMs)
    {
        var context = GetCoordinateContext();
        var point = OverlayCoordinateMapper.ScreenToOverlay(
            x,
            y,
            context.WindowBounds.Left,
            context.WindowBounds.Top,
            context.DpiScaleX,
            context.DpiScaleY);

        SuggestionText.Text = text;
        SuggestionBorder.Visibility = Visibility.Visible;
        SuggestionBorder.UpdateLayout(); // Force measurement

        double maxX = Math.Max(0, ActualWidth - SuggestionBorder.ActualWidth - 20);
        double maxY = Math.Max(0, ActualHeight - SuggestionBorder.ActualHeight - 20);
        double actualX = Math.Clamp(point.X, 0, maxX);
        double actualY = Math.Clamp(point.Y, 0, maxY);

        Canvas.SetLeft(SuggestionBorder, actualX);
        Canvas.SetTop(SuggestionBorder, actualY);

        StartFadeOut(SuggestionBorder, durationMs);
    }

    public void HideAll()
    {
        HideElement(IndicatorBorder);
        HideElement(SuggestionBorder);
    }

    private static void StartFadeOut(FrameworkElement element, int durationMs)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1.0;
        element.Visibility = Visibility.Visible;

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(Math.Max(0, durationMs))),
            FillBehavior = FillBehavior.HoldEnd
        };

        fadeOut.Completed += (_, _) => HideElement(element);
        element.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private static void HideElement(FrameworkElement element)
    {
        // Collapse while the held animation is still at opacity 0. Removing it only
        // after that avoids the one-frame flash caused by FillBehavior.Stop.
        element.Visibility = Visibility.Collapsed;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1.0;
    }

    private void ConfigureMonitorBounds(IntPtr hwnd)
    {
        if (!SetWindowPos(
                hwnd,
                HWND_TOPMOST,
                PhysicalBounds.Left,
                PhysicalBounds.Top,
                PhysicalBounds.Width,
                PhysicalBounds.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not size the overlay to its monitor.");
        }
    }

    private CoordinateContext GetCoordinateContext()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not determine the overlay bounds.");

        var source = PresentationSource.FromVisual(this);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        return new CoordinateContext(bounds, dpiScaleX, dpiScaleY);
    }

    private readonly record struct CoordinateContext(WindowRect WindowBounds, double DpiScaleX, double DpiScaleY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
