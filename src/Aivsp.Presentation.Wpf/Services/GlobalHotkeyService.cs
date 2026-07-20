using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Aivsp.Application.UseCases;

namespace Aivsp.Presentation.Wpf.Services;

public class GlobalHotkeyService : IHostedService, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;
    
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
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
    private readonly object _requestLock = new();
    private CancellationTokenSource? _activeRequestCts;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        uint modifiers = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
        uint vk = 0x78; // F9

        if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID, modifiers, vk))
        {
            _logger.LogInformation("Global hotkey (Ctrl+Shift+F9) registered successfully.");
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;
            });
        }
        else
        {
            _logger.LogError("Failed to register global hotkey.");
        }

        return Task.CompletedTask;
    }

    private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            
            GetCursorPos(out POINT cursorPosition);
            _logger.LogInformation("Hotkey pressed! Triggering AIVSP at cursor ({X}, {Y})...", cursorPosition.X, cursorPosition.Y);
            
            CancellationTokenSource requestCts;

            lock (_requestLock)
            {
                _activeRequestCts?.Cancel();
                requestCts = new CancellationTokenSource();
                _activeRequestCts = requestCts;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var useCase = scope.ServiceProvider.GetRequiredService<MainAssistantUseCase>();
                    await useCase.ExecuteAsync("Responde a la pregunta señalada", cursorPosition.X, cursorPosition.Y, requestCts.Token);
                }
                catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
                {
                    _logger.LogDebug("La solicitud visual anterior fue cancelada por una activación más reciente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing use case from hotkey.");
                }
                finally
                {
                    lock (_requestLock)
                    {
                        if (ReferenceEquals(_activeRequestCts, requestCts))
                        {
                            _activeRequestCts = null;
                        }
                    }

                    requestCts.Dispose();
                }
            });
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_requestLock)
        {
            _activeRequestCts?.Cancel();
            _activeRequestCts = null;
        }

        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ComponentDispatcher.ThreadPreprocessMessage -= ComponentDispatcher_ThreadPreprocessMessage;
            });
        }
        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_requestLock)
        {
            _activeRequestCts?.Cancel();
            _activeRequestCts = null;
        }
        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
    }
}
