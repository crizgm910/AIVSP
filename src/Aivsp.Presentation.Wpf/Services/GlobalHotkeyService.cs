using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using Aivsp.Application.Contracts;
using Aivsp.Application.Notifications;
using Aivsp.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aivsp.Presentation.Wpf.Services;

public class GlobalHotkeyService : IHostedService, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationService _notificationService;
    private readonly ActiveRequestCoordinator _requestCoordinator = new();
    private int _isHotkeyRegistered;

    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        IServiceProvider serviceProvider,
        INotificationService notificationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        uint modifiers = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
        uint vk = 0x78; // F9

        if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID, modifiers, vk))
        {
            Volatile.Write(ref _isHotkeyRegistered, 1);
            _logger.LogInformation("Global hotkey (Ctrl+Shift+F9) registered successfully.");

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;
            });
        }
        else
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            _logger.LogError(error, "Failed to register global hotkey Ctrl+Shift+F9.");

            _notificationService.Notify(new UserNotification(
                Guid.NewGuid(),
                NotificationStatus.Error,
                NotificationCode.HotkeyUnavailable));
        }

        return Task.CompletedTask;
    }

    private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;

            GetCursorPos(out POINT cursorPosition);
            var correlationId = Guid.NewGuid();
            _logger.LogInformation("Hotkey pressed! Triggering AIVSP at cursor ({X}, {Y})...", cursorPosition.X, cursorPosition.Y);

            _requestCoordinator.TryStart(async cancellationToken =>
            {
                using var logScope = _logger.BeginScope("CorrelationId: {CorrelationId}", correlationId);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var useCase = scope.ServiceProvider.GetRequiredService<MainAssistantUseCase>();
                    await useCase.ExecuteAsync(
                        "Responde a la pregunta señalada",
                        cursorPosition.X,
                        cursorPosition.Y,
                        cancellationToken,
                        correlationId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("La solicitud visual anterior fue cancelada por una activación más reciente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing use case from hotkey.");
                }
            });
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ComponentDispatcher.ThreadPreprocessMessage -= ComponentDispatcher_ThreadPreprocessMessage;
            });
        }
        UnregisterHotkeyIfNeeded();
        await _requestCoordinator.StopAsync(cancellationToken);
    }

    public void Dispose()
    {
        _requestCoordinator.Dispose();
        UnregisterHotkeyIfNeeded();
    }

    private void UnregisterHotkeyIfNeeded()
    {
        if (Interlocked.Exchange(ref _isHotkeyRegistered, 0) == 0)
        {
            return;
        }

        if (UnregisterHotKey(IntPtr.Zero, HOTKEY_ID))
        {
            _logger.LogInformation("Global hotkey Ctrl+Shift+F9 unregistered successfully.");
        }
        else
        {
            _logger.LogWarning(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "Failed to unregister global hotkey Ctrl+Shift+F9.");
        }
    }
}
