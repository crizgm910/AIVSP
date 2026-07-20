using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Hardcodet.Wpf.TaskbarNotification;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Presentation.Wpf.UI;

namespace Aivsp.Presentation.Wpf.Services;

public class SystemTrayService : IHostedService, IDisposable
{
    private readonly ILogger<SystemTrayService> _logger;
    private readonly AssistantOptions _options;
    private readonly IOverlayService _overlayService;
    private TaskbarIcon? _taskbarIcon;
    private MenuItem? _pauseMenuItem;
    private MenuItem? _ocrModeMenuItem;
    private MenuItem? _vlmModeMenuItem;
    private MenuItem? _autoPasteMenuItem;

    public SystemTrayService(
        ILogger<SystemTrayService> logger,
        AssistantOptions options,
        IOverlayService overlayService)
    {
        _logger = logger;
        _options = options;
        _overlayService = overlayService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Information, // Placeholder icon
                ToolTipText = "AIVSP - Listo (OCR + Llama3.2)"
            };

            var contextMenu = new ContextMenu();

            _pauseMenuItem = new MenuItem { Header = "Pausar Asistente", IsCheckable = true };
            _pauseMenuItem.Click += (s, e) => 
            {
                _options.IsPaused = _pauseMenuItem.IsChecked;
                _pauseMenuItem.Header = _options.IsPaused ? "Reanudar Asistente" : "Pausar Asistente";
                _taskbarIcon.ToolTipText = _options.IsPaused ? "AIVSP (Pausado)" : "AIVSP - Listo";
                _logger.LogInformation("Asistente " + (_options.IsPaused ? "pausado" : "reanudado"));
            };
            contextMenu.Items.Add(_pauseMenuItem);

            contextMenu.Items.Add(new Separator());

            _ocrModeMenuItem = new MenuItem { Header = "Modo: OCR + Llama3.2", IsCheckable = true, IsChecked = !_options.UseVisionModel };
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
                _options.ModelName = "llama3.2:3b";
                _ocrModeMenuItem.IsChecked = true;
                _vlmModeMenuItem.IsChecked = false;
                _taskbarIcon.ShowBalloonTip("AIVSP", "Modo cambiado a OCR + Llama3.2", BalloonIcon.Info);
            };

            contextMenu.Items.Add(_ocrModeMenuItem);
            contextMenu.Items.Add(_vlmModeMenuItem);

            contextMenu.Items.Add(new Separator());

            _autoPasteMenuItem = new MenuItem { Header = "Auto-Pegado Habilitado", IsCheckable = true, IsChecked = _options.AutoPasteEnabled };
            _autoPasteMenuItem.Click += (s, e) =>
            {
                _options.AutoPasteEnabled = _autoPasteMenuItem.IsChecked;
            };
            contextMenu.Items.Add(_autoPasteMenuItem);

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
        });

        _logger.LogInformation("System Tray Icon initialized successfully.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarIcon?.Dispose();
        });
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _taskbarIcon?.Dispose();
    }
}
