using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Aivsp.Application.Configuration;
using Aivsp.Application.Geometry;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace Aivsp.Presentation.Wpf.UI;

public partial class OverlayWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private readonly AssistantOptions _options;
    private readonly Dictionary<FrameworkElement, long> _animationGenerations = new();
    private long _nextAnimationGeneration;

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

    public OverlayWindow(PhysicalScreenBounds physicalBounds, AssistantOptions options)
    {
        PhysicalBounds = physicalBounds;
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
        EnsureShown();
        var style = ApplyVisualProfile(durationMs);
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

        StartFadeOut(IndicatorBorder, style.DurationMilliseconds);
    }

    public void ShowTextSuggestion(string text, int x, int y, int durationMs)
    {
        EnsureShown();
        var style = ApplyVisualProfile(durationMs);
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

        StartFadeOut(SuggestionBorder, style.DurationMilliseconds);
    }

    public void HideAll()
    {
        _animationGenerations.Clear();
        HideElement(IndicatorBorder);
        HideElement(SuggestionBorder);
        Hide();
    }

    private void StartFadeOut(FrameworkElement element, int durationMs)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1.0;
        element.Visibility = Visibility.Visible;
        long generation = ++_nextAnimationGeneration;
        _animationGenerations[element] = generation;

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(Math.Max(0, durationMs))),
            FillBehavior = FillBehavior.HoldEnd
        };

        fadeOut.Completed += (_, _) =>
        {
            if (!_animationGenerations.TryGetValue(element, out long currentGeneration) ||
                currentGeneration != generation)
            {
                return;
            }

            _animationGenerations.Remove(element);
            HideElement(element);
            HideWindowIfIdle();
        };
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

    private void EnsureShown()
    {
        if (!IsVisible)
        {
            Show();
        }
    }

    private void HideWindowIfIdle()
    {
        if (IndicatorBorder.Visibility != Visibility.Visible &&
            SuggestionBorder.Visibility != Visibility.Visible)
        {
            Hide();
        }
    }

    private OverlayStyleSettings ApplyVisualProfile(int requestedDurationMilliseconds)
    {
        var style = OverlayStyleResolver.Resolve(_options, requestedDurationMilliseconds);

        IndicatorBorder.BorderThickness = new Thickness(style.IndicatorBorderThickness);
        IndicatorBorder.Background = new SolidColorBrush(
            WpfColor.FromArgb(style.IndicatorFillAlpha, 0, 255, 0));
        IndicatorBorder.BorderBrush = _options.OverlayProfile == OverlayProfile.Subtle
            ? new SolidColorBrush(WpfColor.FromArgb(0xB3, 50, 205, 50))
            : WpfBrushes.LimeGreen;
        IndicatorBorder.Effect = style.IndicatorShadowEnabled
            ? new DropShadowEffect
            {
                Color = Colors.LimeGreen,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 1
            }
            : null;

        SuggestionBorder.BorderThickness = new Thickness(style.SuggestionBorderThickness);
        SuggestionBorder.Background = new SolidColorBrush(
            WpfColor.FromArgb(style.SuggestionBackgroundAlpha, 0, 0, 0));
        SuggestionBorder.BorderBrush = _options.OverlayProfile == OverlayProfile.Subtle
            ? new SolidColorBrush(WpfColor.FromArgb(0x80, 0, 191, 255))
            : WpfBrushes.DeepSkyBlue;
        SuggestionBorder.CornerRadius = _options.OverlayProfile == OverlayProfile.Subtle
            ? new CornerRadius(4)
            : new CornerRadius(8);
        SuggestionBorder.Padding = new Thickness(style.SuggestionPadding);
        SuggestionBorder.MaxWidth = style.SuggestionMaximumWidth;
        SuggestionBorder.Effect = style.SuggestionShadowEnabled
            ? new DropShadowEffect
            {
                Color = Colors.DeepSkyBlue,
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 0.8
            }
            : null;
        SuggestionText.FontSize = style.SuggestionFontSize;

        return style;
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
