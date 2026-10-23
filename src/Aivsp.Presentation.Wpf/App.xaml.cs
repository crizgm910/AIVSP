using System;
using System.Net.Http;
using System.Windows;
using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Detection;
using Aivsp.Application.Matching;
using Aivsp.Application.UseCases;
using Aivsp.Infrastructure.Inference.Services;
using Aivsp.Infrastructure.Windows.Logging;
using Aivsp.Infrastructure.Windows.Ocr;
using Aivsp.Infrastructure.Windows.Services;
using Aivsp.Presentation.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfMessageBox = System.Windows.MessageBox;

namespace Aivsp.Presentation.Wpf;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private NamedMutexSingleInstanceGuard? _singleInstanceGuard;
    private ILoggerFactory? _bootstrapLoggerFactory;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _bootstrapLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
        });
        var bootstrapLogger = _bootstrapLoggerFactory.CreateLogger<App>();
        _singleInstanceGuard = new NamedMutexSingleInstanceGuard();

        try
        {
            if (!_singleInstanceGuard.TryAcquire())
            {
                bootstrapLogger.LogWarning(
                    "AIVSP startup rejected because another instance owns mutex {MutexName}.",
                    _singleInstanceGuard.MutexName);

                WpfMessageBox.Show(
                    "AIVSP ya está ejecutándose. Revisa el icono de la aplicación en la bandeja del sistema.",
                    "AIVSP ya está abierto",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            bootstrapLogger.LogCritical(ex, "AIVSP could not acquire its single-instance mutex.");
            WpfMessageBox.Show(
                $"No se pudo verificar si AIVSP ya está ejecutándose.\n\n{ex.Message}",
                "Error al iniciar AIVSP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureLogging(logging =>
                {
                    logging.AddProvider(new DailyFileLoggerProvider());
                })
                .ConfigureServices((context, services) =>
                {
                    var options = new AssistantOptions
                    {
                        ModelName = "qwen3:4b",
                        OllamaEndpoint = "http://127.0.0.1:11434",
                        LocalKnowledgeContext =
                            "Datos comprobados de AIVSP: el endpoint local de Ollama es http://127.0.0.1:11434. " +
                            "OverlayWindow usa WS_EX_TRANSPARENT y WS_EX_NOACTIVATE para click-through sin activar la ventana. " +
                            "Los handles GDI se liberan con DeleteObject para evitar fugas y agotamiento de recursos. " +
                            "Para evitar Reflection en System.Text.Json se implementó OllamaJsonContext con Source Generators y metadatos JSON precompilados. " +
                            "Clean Architecture aplica la regla de dependencia: el código fuente solo puede apuntar hacia adentro. " +
                            "LFDA artículo 29: los derechos patrimoniales están vigentes durante la vida del autor y, a partir de su muerte, cien años más; equivale a la vida del autor más 100 años después de su muerte."
                    };
                    services.AddSingleton(options);
                    services.AddSingleton<IAssistantSettingsStore, JsonAssistantSettingsStore>();

                    services.AddTransient<MainAssistantUseCase>();
                    services.AddSingleton<ITextMatcher, CompositeTextMatcher>();
                    services.AddSingleton<IQuestionTypeDetector, QuestionTypeDetector>();
                    services.AddSingleton<IInputSimulationService, Win32InputSimulator>();
                    services.AddSingleton<IActiveWindowContextService, Win32ActiveWindowContextService>();
                    services.AddSingleton<IAutoPasteSecurityService, AutoPasteSecurityService>();

                    services.AddSingleton<IImageCaptureService, GdiImageCaptureService>();
                    services.AddSingleton<IOcrEngine, WindowsMediaOcrEngine>();

                    services.AddHttpClient<OllamaInferenceService>(client =>
                    {
                        client.Timeout = TimeSpan.FromMinutes(5);
                    });
                    services.AddSingleton<IGeminiCliProcessRunner, GeminiCliProcessRunner>();
                    services.AddSingleton<GeminiCliInferenceService>();
                    services.AddSingleton<IAntigravityCliProcessRunner, AntigravityCliProcessRunner>();
                    services.AddSingleton<AntigravityCliInferenceService>();
                    services.AddSingleton<IInferenceService, InferenceServiceRouter>();
                    services.AddSingleton<BrowserDomAssistantService>();

                    var overlayService = new WpfOverlayService(options);
                    overlayService.Initialize();
                    services.AddSingleton<IOverlayService>(overlayService);

                    services.AddSingleton<SystemTrayService>();
                    services.AddSingleton<INotificationService>(provider =>
                        provider.GetRequiredService<SystemTrayService>());
                    services.AddHostedService(provider =>
                        provider.GetRequiredService<SystemTrayService>());
                    services.AddHostedService<GlobalHotkeyService>();
                    services.AddHostedService<ChromeBridgeHostedService>();
                })
                .Build();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            try
            {
                var settingsStore = _host.Services.GetRequiredService<IAssistantSettingsStore>();
                var options = _host.Services.GetRequiredService<AssistantOptions>();
                settingsStore.LoadInto(options);

                // VLM remains unavailable until its coordinate contract and physical
                // validation are complete. It cannot be enabled through persisted settings.
                options.UseVisionModel = false;
                _logger.LogInformation(
                    "AIVSP settings loaded. Provider: {InferenceProvider}; Overlay: {OverlayProfile}; " +
                    "AutoPaste: {AutoPasteEnabled}; DesktopOCR: {DesktopOcrEnabled}; " +
                    "BrowserDOM: {BrowserIntegrationEnabled}; InferenceTimeoutSeconds: {InferenceTimeoutSeconds}; " +
                    "GeminiInferenceTimeoutSeconds: {GeminiInferenceTimeoutSeconds}; " +
                    "BrowserGeminiInferenceTimeoutSeconds: {BrowserGeminiInferenceTimeoutSeconds}; " +
                    "GeminiModel: {GeminiModelName}; " +
                    "AntigravityModel: {AntigravityModelName}; " +
                    "VLM enabled: {UseVisionModel}.",
                    options.InferenceProvider,
                    options.OverlayProfile,
                    options.AutoPasteEnabled,
                    options.DesktopOcrEnabled,
                    options.BrowserIntegrationEnabled,
                    options.InferenceTimeoutSeconds,
                    options.GeminiInferenceTimeoutSeconds,
                    options.BrowserGeminiInferenceTimeoutSeconds,
                    options.GeminiModelName,
                    options.AntigravityModelName,
                    options.UseVisionModel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AIVSP settings could not be loaded; defaults remain active.");
            }

            _logger.LogInformation(
                "AIVSP acquired single-instance mutex {MutexName}.",
                _singleInstanceGuard.MutexName);

            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            (_logger ?? bootstrapLogger).LogCritical(ex, "AIVSP failed during startup.");
            WpfMessageBox.Show(
                $"AIVSP no pudo iniciarse.\n\n{ex.Message}",
                "Error al iniciar AIVSP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _logger?.LogInformation(
                    "AIVSP is releasing single-instance mutex {MutexName}.",
                    _singleInstanceGuard?.MutexName);
            }
        }
        finally
        {
            _host?.Dispose();
            _host = null;
            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;
            _bootstrapLoggerFactory?.Dispose();
            _bootstrapLoggerFactory = null;
            base.OnExit(e);
        }
    }
}
