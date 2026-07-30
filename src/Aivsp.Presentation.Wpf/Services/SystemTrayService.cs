using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Notifications;
using Aivsp.Presentation.Wpf.UI;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aivsp.Presentation.Wpf.Services;

public class SystemTrayService : IHostedService, INotificationService, IDisposable
{
    private readonly ILogger<SystemTrayService> _logger;
    private readonly AssistantOptions _options;
    private readonly IOverlayService _overlayService;
    private readonly IAssistantSettingsStore _settingsStore;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _pauseMenuItem;
    private MenuItem? _ocrModeMenuItem;
    private MenuItem? _geminiModeMenuItem;
    private MenuItem? _vlmModeMenuItem;
    private MenuItem? _autoPasteMenuItem;
    private MenuItem? _visibleOverlayMenuItem;
    private MenuItem? _subtleOverlayMenuItem;
    private MenuItem? _desktopOcrMenuItem;
    private MenuItem? _browserIntegrationMenuItem;
    private readonly ConcurrentQueue<UserNotification> _pendingNotifications = new();
    private int _isDrainingNotifications;
    private bool _disposed;

    public SystemTrayService(
        ILogger<SystemTrayService> logger,
        AssistantOptions options,
        IOverlayService overlayService,
        IAssistantSettingsStore settingsStore)
    {
        _logger = logger;
        _options = options;
        _overlayService = overlayService;
        _settingsStore = settingsStore;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Information, // Placeholder icon
                ToolTipText = ResolveReadyTooltip(_options)
            };

            var contextMenu = new ContextMenu();

            _pauseMenuItem = new MenuItem { Header = "Pausar Asistente", IsCheckable = true };
            _pauseMenuItem.Click += (s, e) =>
            {
                _options.IsPaused = _pauseMenuItem.IsChecked;
                _pauseMenuItem.Header = _options.IsPaused ? "Reanudar Asistente" : "Pausar Asistente";
                _taskbarIcon.ToolTipText = _options.IsPaused
                    ? "AIVSP (Pausado)"
                    : ResolveReadyTooltip(_options);
                _logger.LogInformation("Asistente " + (_options.IsPaused ? "pausado" : "reanudado"));
            };
            contextMenu.Items.Add(_pauseMenuItem);

            contextMenu.Items.Add(new Separator());

            _ocrModeMenuItem = new MenuItem
            {
                Header = "Inferencia: Ollama local (Qwen3)",
                IsCheckable = true,
                IsChecked = !_options.UseVisionModel &&
                    _options.InferenceProvider == InferenceProvider.Ollama
            };
            _geminiModeMenuItem = new MenuItem
            {
                Header = "Inferencia: Gemini CLI (3.1 Flash Lite)",
                IsCheckable = true,
                IsChecked = !_options.UseVisionModel &&
                    _options.InferenceProvider == InferenceProvider.GeminiCli,
                ToolTip = "Requiere Gemini CLI instalado y autenticado con una cuenta o proyecto compatible."
            };
            _vlmModeMenuItem = new MenuItem
            {
                Header = "Modo: VLM (Moondream) - pendiente",
                IsCheckable = true,
                IsChecked = false,
                IsEnabled = false,
                ToolTip = "El flujo VLM todavía no genera coordenadas y límites de overlay confiables."
            };

            _ocrModeMenuItem.Click += (s, e) =>
            {
                _options.UseVisionModel = false;
                _options.InferenceProvider = InferenceProvider.Ollama;
                _options.ModelName = "qwen3:4b";
                _ocrModeMenuItem.IsChecked = true;
                _geminiModeMenuItem.IsChecked = false;
                _vlmModeMenuItem.IsChecked = false;
                _taskbarIcon.ToolTipText = ResolveReadyTooltip(_options);
                _taskbarIcon.ShowBalloonTip(
                    "AIVSP",
                    "Inferencia cambiada a Ollama local (Qwen3).",
                    BalloonIcon.Info);
                SaveSettings();
            };

            _geminiModeMenuItem.Click += (s, e) =>
            {
                _options.UseVisionModel = false;
                _options.InferenceProvider = InferenceProvider.GeminiCli;
                _ocrModeMenuItem.IsChecked = false;
                _geminiModeMenuItem.IsChecked = true;
                _vlmModeMenuItem.IsChecked = false;
                _taskbarIcon.ToolTipText = ResolveReadyTooltip(_options);
                _taskbarIcon.ShowBalloonTip(
                    "AIVSP",
                    "Inferencia cambiada a Gemini CLI aislado (3.1 Flash Lite).",
                    BalloonIcon.Info);
                _logger.LogInformation(
                    "Proveedor de inferencia cambiado a {InferenceProvider}.",
                    _options.InferenceProvider);
                SaveSettings();
            };

            contextMenu.Items.Add(_ocrModeMenuItem);
            contextMenu.Items.Add(_geminiModeMenuItem);
            contextMenu.Items.Add(_vlmModeMenuItem);

            contextMenu.Items.Add(new Separator());

            _autoPasteMenuItem = new MenuItem { Header = "Auto-Pegado Habilitado", IsCheckable = true, IsChecked = _options.AutoPasteEnabled };
            _autoPasteMenuItem.Click += (s, e) =>
            {
                _options.AutoPasteEnabled = _autoPasteMenuItem.IsChecked;
                SaveSettings();
            };
            contextMenu.Items.Add(_autoPasteMenuItem);

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(new MenuItem
            {
                Header = "Fuentes de entrada",
                IsEnabled = false
            });

            _desktopOcrMenuItem = new MenuItem
            {
                Header = "Apps de escritorio (OCR · Ctrl+Shift+F9)",
                IsCheckable = true,
                IsChecked = _options.DesktopOcrEnabled,
                ToolTip = "Captura la ventana activa y reconoce el texto mediante OCR."
            };
            _desktopOcrMenuItem.Click += (s, e) =>
            {
                _options.DesktopOcrEnabled = _desktopOcrMenuItem.IsChecked;
                _logger.LogInformation(
                    "Fuente Apps de escritorio (OCR) cambiada a {Enabled}.",
                    _options.DesktopOcrEnabled);
                _taskbarIcon.ShowBalloonTip(
                    "AIVSP",
                    _options.DesktopOcrEnabled
                        ? "Apps de escritorio (OCR) habilitadas."
                        : "Apps de escritorio (OCR) deshabilitadas.",
                    BalloonIcon.Info);
                SaveSettings();
            };
            contextMenu.Items.Add(_desktopOcrMenuItem);

            _browserIntegrationMenuItem = new MenuItem
            {
                Header = "Chrome (DOM · Ctrl+Shift+Y)",
                IsCheckable = true,
                IsChecked = _options.BrowserIntegrationEnabled,
                ToolTip = "Acepta solicitudes autenticadas sólo desde el puente local."
            };
            _browserIntegrationMenuItem.Click += (s, e) =>
            {
                _options.BrowserIntegrationEnabled = _browserIntegrationMenuItem.IsChecked;
                _logger.LogInformation(
                    "Integración Chrome cambiada a {Enabled}.",
                    _options.BrowserIntegrationEnabled);
                _taskbarIcon.ShowBalloonTip(
                    "AIVSP",
                    _options.BrowserIntegrationEnabled
                        ? "Integración Chrome habilitada en este equipo."
                        : "Integración Chrome deshabilitada.",
                    BalloonIcon.Info);
                SaveSettings();
            };
            contextMenu.Items.Add(_browserIntegrationMenuItem);

            var copyBrowserTokenItem = new MenuItem
            {
                Header = "Copiar token para extensión Chrome",
                ToolTip = "El token sólo autoriza conexiones al puente local de AIVSP."
            };
            copyBrowserTokenItem.Click += (s, e) =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(_options.BrowserBridgeToken);
                    _taskbarIcon.ShowBalloonTip(
                        "AIVSP",
                        "Token local copiado. Pégalo en las opciones de la extensión.",
                        BalloonIcon.Info);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "No se pudo copiar el token local de Chrome.");
                    _taskbarIcon.ShowBalloonTip(
                        "AIVSP",
                        "No se pudo copiar el token de la extensión.",
                        BalloonIcon.Error);
                }
            };
            contextMenu.Items.Add(copyBrowserTokenItem);

            contextMenu.Items.Add(new Separator());

            _visibleOverlayMenuItem = new MenuItem
            {
                Header = "Visual: Visible (diagnóstico)",
                IsCheckable = true,
                IsChecked = _options.OverlayProfile == OverlayProfile.Visible
            };
            _subtleOverlayMenuItem = new MenuItem
            {
                Header = "Visual: Discreto (baja distracción)",
                IsCheckable = true,
                IsChecked = _options.OverlayProfile == OverlayProfile.Subtle,
                ToolTip = "Borde fino, sin sombra y duración reducida."
            };

            _visibleOverlayMenuItem.Click += (s, e) =>
            {
                SetOverlayProfile(OverlayProfile.Visible);
            };
            _subtleOverlayMenuItem.Click += (s, e) =>
            {
                SetOverlayProfile(OverlayProfile.Subtle);
            };

            contextMenu.Items.Add(_visibleOverlayMenuItem);
            contextMenu.Items.Add(_subtleOverlayMenuItem);

            contextMenu.Items.Add(new Separator());

            var testOverlayItem = new MenuItem { Header = "Probar Overlay (Recuadro Azul)" };
            testOverlayItem.Click += async (s, e) =>
            {
                // Draw in the center of the primary screen
                int centerX = (int)(SystemParameters.PrimaryScreenWidth / 2 - 150);
                int centerY = (int)(SystemParameters.PrimaryScreenHeight / 2 - 50);

                await _overlayService.DrawTextSuggestionDirectAsync("Prueba de overlay visual desde System Tray", centerX, centerY, 3000);
            };
            contextMenu.Items.Add(testOverlayItem);

            contextMenu.Items.Add(new Separator());

            var exitMenuItem = new MenuItem { Header = "Salir" };
            exitMenuItem.Click += (s, e) =>
            {
                System.Windows.Application.Current.Shutdown();
            };
            contextMenu.Items.Add(exitMenuItem);

            _taskbarIcon.ContextMenu = contextMenu;
            ScheduleNotificationDrain();
        });

        _logger.LogInformation("System Tray Icon initialized successfully.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposed = true;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarIcon?.Dispose();
            _taskbarIcon = null;
        });
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }

    public void Notify(UserNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (_disposed)
        {
            _logger.LogWarning(
                "Notification {NotificationCode} ignored because the tray service is disposed. CorrelationId: {CorrelationId}",
                notification.Code,
                notification.CorrelationId);
            return;
        }

        _pendingNotifications.Enqueue(notification);
        _logger.LogInformation(
            "User notification queued. Status: {NotificationStatus}; Code: {NotificationCode}; CorrelationId: {CorrelationId}",
            notification.Status,
            notification.Code,
            notification.CorrelationId);

        var application = System.Windows.Application.Current;
        if (application != null && !application.Dispatcher.HasShutdownStarted)
        {
            application.Dispatcher.BeginInvoke(ScheduleNotificationDrain);
        }
    }

    private void ScheduleNotificationDrain()
    {
        if (_taskbarIcon == null || _disposed ||
            Interlocked.CompareExchange(ref _isDrainingNotifications, 1, 0) != 0)
        {
            return;
        }

        _ = DrainNotificationsAsync();
    }

    private async Task DrainNotificationsAsync()
    {
        try
        {
            while (!_disposed && _taskbarIcon != null &&
                _pendingNotifications.TryDequeue(out var notification))
            {
                var (title, message) = ResolveText(notification);
                var shortId = notification.CorrelationId.ToString("N")[..8];
                var tooltip = $"{title} [{shortId}]: {ResolveMessage(notification.Code)}";
                _taskbarIcon.ToolTipText = tooltip.Length <= 63 ? tooltip : tooltip[..63];

                // Routine progress and success are already represented by the
                // extension/overlay. Keep them in logs and the tray tooltip without
                // opening a large Windows toast. Only actionable states consume the
                // balloon channel.
                if (!NotificationPresentationPolicy.ShouldShowBalloon(
                        notification.Status))
                {
                    if (notification.Status == NotificationStatus.Completed)
                    {
                        _taskbarIcon.ToolTipText = ResolveReadyTooltip(_options);
                    }
                    continue;
                }

                _taskbarIcon.ShowBalloonTip(title, message, ResolveIcon(notification.Status));
                await Task.Delay(1500);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isDrainingNotifications, 0);
            if (!_pendingNotifications.IsEmpty && !_disposed)
            {
                ScheduleNotificationDrain();
            }
        }
    }

    private static BalloonIcon ResolveIcon(NotificationStatus status) => status switch
    {
        NotificationStatus.Error => BalloonIcon.Error,
        NotificationStatus.Warning or NotificationStatus.Cancelled => BalloonIcon.Warning,
        _ => BalloonIcon.Info
    };

    private static (string Title, string Message) ResolveText(UserNotification notification)
    {
        var title = notification.Status switch
        {
            NotificationStatus.Processing => "AIVSP · Procesando",
            NotificationStatus.Completed => "AIVSP · Completado",
            NotificationStatus.Cancelled => "AIVSP · Cancelado",
            NotificationStatus.Error => "AIVSP · Error",
            NotificationStatus.Warning => "AIVSP · Advertencia",
            _ => "AIVSP"
        };

        var message = ResolveMessage(notification.Code);

        var shortId = notification.CorrelationId.ToString("N")[..8];
        return (title, $"{message} ID: {shortId}");
    }

    private static string ResolveMessage(NotificationCode code) => code switch
    {
        NotificationCode.ExecutionStarted => "Analizando la pregunta…",
        NotificationCode.ExecutionCompleted => "La respuesta está lista.",
        NotificationCode.ExecutionCancelled => "La operación fue cancelada.",
        NotificationCode.InferenceTimedOut => "La IA agotó el tiempo de espera configurado.",
        NotificationCode.AssistantPaused => "El asistente está pausado.",
        NotificationCode.NoTextDetected => "No se detectó texto legible.",
        NotificationCode.QuestionNotRecognized => "No se reconoció el tipo de pregunta.",
        NotificationCode.OcrUnavailable => "No se pudo leer el contenido de la pantalla.",
        NotificationCode.OllamaUnavailable => "El servicio local de IA no está disponible.",
        NotificationCode.InferenceModelUnavailable =>
            "El modelo configurado no está instalado. Descárgalo en Ollama y vuelve a intentar.",
        NotificationCode.InferenceProviderUnavailable => "El proveedor de IA seleccionado no está disponible.",
        NotificationCode.InvalidInferenceResponse => "La IA devolvió una respuesta no válida.",
        NotificationCode.HotkeyUnavailable => "El atajo Ctrl+Shift+F9 está ocupado.",
        NotificationCode.DesktopCaptureDisabled =>
            "Apps de escritorio (OCR) están deshabilitadas en la bandeja.",
        NotificationCode.AutoPasteCancelled => "Autopegado cancelado: el contexto cambió o no es seguro.",
        NotificationCode.AutoPasteFailed => "No se pudo pegar la respuesta automáticamente.",
        NotificationCode.BrowserBridgeUnavailable => "No se pudo iniciar la integración local de Chrome.",
        NotificationCode.BrowserRequestFailed => "La solicitud enviada desde Chrome no pudo completarse.",
        _ => "La operación no pudo completarse."
    };

    private static string ResolveReadyTooltip(AssistantOptions options) =>
        options.InferenceProvider == InferenceProvider.GeminiCli
            ? "AIVSP - Listo (Gemini CLI)"
            : "AIVSP - Listo (Ollama local)";

    private void SetOverlayProfile(OverlayProfile profile)
    {
        _options.OverlayProfile = profile;
        _overlayService.Clear();

        if (_visibleOverlayMenuItem != null)
        {
            _visibleOverlayMenuItem.IsChecked = profile == OverlayProfile.Visible;
        }
        if (_subtleOverlayMenuItem != null)
        {
            _subtleOverlayMenuItem.IsChecked = profile == OverlayProfile.Subtle;
        }

        _logger.LogInformation("Perfil visual cambiado a {OverlayProfile}.", profile);
        _taskbarIcon?.ShowBalloonTip(
            "AIVSP",
            profile == OverlayProfile.Subtle
                ? "Perfil discreto activado: borde fino y duración corta."
                : "Perfil visible de diagnóstico activado.",
            BalloonIcon.Info);
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar la configuración de AIVSP.");
        }
    }
}
