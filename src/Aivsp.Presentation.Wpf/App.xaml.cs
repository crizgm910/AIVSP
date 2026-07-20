using System;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.UseCases;
using Aivsp.Application.Detection;
using Aivsp.Infrastructure.Windows.Ocr;
using Aivsp.Infrastructure.Windows.Services;
using Aivsp.Infrastructure.Inference.Services;
using Aivsp.Presentation.Wpf.Services;
using Aivsp.Application.Matching;

namespace Aivsp.Presentation.Wpf;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices((context, services) =>
            {
                var options = new AssistantOptions 
                { 
                    ModelName = "llama3.2:3b",
                    OllamaEndpoint = "http://127.0.0.1:11434"
                };
                services.AddSingleton(options);

                services.AddTransient<MainAssistantUseCase>();
                services.AddSingleton<ITextMatcher, CompositeTextMatcher>();
                services.AddSingleton<IQuestionTypeDetector, QuestionTypeDetector>();
                services.AddSingleton<IInputSimulationService, Win32InputSimulator>();

                services.AddSingleton<IImageCaptureService, GdiImageCaptureService>();
                services.AddSingleton<IOcrEngine, WindowsMediaOcrEngine>();

                services.AddHttpClient<IInferenceService, OllamaInferenceService>(client => 
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                });

                var overlayService = new WpfOverlayService();
                overlayService.Initialize(); 
                services.AddSingleton<IOverlayService>(overlayService);
                
                services.AddHostedService<GlobalHotkeyService>();
                services.AddHostedService<SystemTrayService>();
            })
            .Build();

        await _host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
