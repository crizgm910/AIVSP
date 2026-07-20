using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Aivsp.Application.Contracts;
using Aivsp.Application.Geometry;
using Aivsp.Domain.Entities;
using Aivsp.Presentation.Wpf.UI;
using Microsoft.Win32;

namespace Aivsp.Presentation.Wpf.Services;

public class WpfOverlayService : IOverlayService, IDisposable
{
    private readonly List<OverlayWindow> _overlayWindows = new();
    private bool _initialized;

    public void Initialize()
    {
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        System.Windows.Application.Current!.Dispatcher.Invoke(() =>
        {
            if (_initialized)
                return;

            RebuildOverlayWindows();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _initialized = true;
        });
    }

    public async Task DrawIndicatorAsync(MatchResult match, int durationMs)
    {
        if (!_initialized) Initialize();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var bounds = match.Candidate.Bounds;
            var overlay = FindOverlay(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            overlay.ShowIndicator(bounds.X, bounds.Y, bounds.Width, bounds.Height, durationMs);
        });
    }

    public async Task DrawTextSuggestionAsync(TextSuggestionOutput suggestion, int durationMs)
    {
        if (!_initialized) Initialize();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var overlay = FindOverlay((int)suggestion.SuggestedX, (int)suggestion.SuggestedY);
            overlay.ShowTextSuggestion(suggestion.SuggestedText, (int)suggestion.SuggestedX, (int)suggestion.SuggestedY, durationMs);
        });
    }

    public async Task DrawTextSuggestionDirectAsync(string text, int x, int y, int durationMs)
    {
        if (!_initialized) Initialize();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            FindOverlay(x, y).ShowTextSuggestion(text, x, y, durationMs);
        });
    }

    public void Clear()
    {
        if (_initialized)
        {
            System.Windows.Application.Current!.Dispatcher.Invoke(() =>
            {
                foreach (var overlay in _overlayWindows)
                {
                    overlay.HideAll();
                }
            });
        }
    }

    public void Dispose()
    {
        if (!_initialized)
            return;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        var application = System.Windows.Application.Current;
        if (application != null && !application.Dispatcher.HasShutdownStarted)
        {
            application.Dispatcher.Invoke(CloseOverlayWindows);
        }

        _initialized = false;
    }

    private void RebuildOverlayWindows()
    {
        CloseOverlayWindows();

        var screens = System.Windows.Forms.Screen.AllScreens
            .OrderByDescending(screen => screen.Primary);

        foreach (var screen in screens)
        {
            var bounds = screen.Bounds;
            var overlay = new OverlayWindow(new PhysicalScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            overlay.Show();
            _overlayWindows.Add(overlay);
        }

        if (_overlayWindows.Count == 0)
            throw new InvalidOperationException("Windows did not report any active monitor.");
    }

    private void CloseOverlayWindows()
    {
        foreach (var overlay in _overlayWindows)
        {
            overlay.Close();
        }
        _overlayWindows.Clear();
    }

    private OverlayWindow FindOverlay(int x, int y)
    {
        var containingOverlay = _overlayWindows.FirstOrDefault(overlay => overlay.PhysicalBounds.Contains(x, y));
        if (containingOverlay != null)
            return containingOverlay;

        return _overlayWindows
            .OrderBy(overlay => overlay.PhysicalBounds.DistanceSquaredTo(x, y))
            .First();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var application = System.Windows.Application.Current;
        if (application == null || application.Dispatcher.HasShutdownStarted)
            return;

        application.Dispatcher.BeginInvoke(RebuildOverlayWindows);
    }
}
