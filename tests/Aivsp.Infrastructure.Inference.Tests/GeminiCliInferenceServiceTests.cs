using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Application.Exceptions;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Aivsp.Infrastructure.Inference.Services;

namespace Aivsp.Infrastructure.Inference.Tests;

public class GeminiCliInferenceServiceTests
{
    [Fact]
    public async Task InferActionAsync_MultipleChoiceEnvelope_MapsSelectedOption()
    {
        var runner = new StubGeminiCliProcessRunner(
            SuccessEnvelope("""{"answer":"B","confidence":0.94}"""));
        var service = new GeminiCliInferenceService(runner);

        var result = await service.InferActionAsync(
            "Responde",
            CreateMultipleChoiceDetection(),
            CreateOptions(),
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(result);
        Assert.Equal("B) París", visual.SelectedText);
        Assert.Equal(0.94, visual.Confidence);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task InferActionAsync_OpenTextEnvelope_MapsSuggestionAndInputTarget()
    {
        var runner = new StubGeminiCliProcessRunner(
            SuccessEnvelope("""{"answer":"París","confidence":0.91}"""));
        var service = new GeminiCliInferenceService(runner);

        var result = await service.InferActionAsync(
            "Responde",
            CreateOpenTextDetection(),
            CreateOptions(),
            CancellationToken.None);

        var suggestion = Assert.IsType<TextSuggestionOutput>(result);
        Assert.Equal("París", suggestion.SuggestedText);
        Assert.Equal(200, suggestion.SuggestedX);
        Assert.Equal(220, suggestion.SuggestedY);
    }

    [Fact]
    public async Task InferActionAsync_InvalidFirstResponse_RetriesOnce()
    {
        var runner = new StubGeminiCliProcessRunner(
            SuccessEnvelope("not-json"),
            SuccessEnvelope("""{"answer":"B","confidence":0.90}"""));
        var service = new GeminiCliInferenceService(runner);

        var result = await service.InferActionAsync(
            "Responde",
            CreateMultipleChoiceDetection(),
            CreateOptions(),
            CancellationToken.None);

        Assert.IsType<VisualIndicatorOutput>(result);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task InferActionAsync_UnrelatedLocalContextLeak_RetriesOnce()
    {
        var runner = new StubGeminiCliProcessRunner(
            SuccessEnvelope(
                """{"answer":"El endpoint local de Ollama es http://127.0.0.1:11434","confidence":0.90}"""),
            SuccessEnvelope(
                """{"answer":"La copia masiva y la distribución sin autorización dificultan el control y la remuneración.","confidence":0.94}"""));
        var service = new GeminiCliInferenceService(runner);
        var options = CreateOptions();
        options.LocalKnowledgeContext =
            "Datos comprobados de AIVSP: el endpoint local de Ollama es http://127.0.0.1:11434.";
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new List<OcrResult>
            {
                Ocr(
                    "Explica dos retos que enfrentan los autores en el entorno digital.",
                    20,
                    20),
                Ocr("Escribe una respuesta breve", 100, 200, 200, 40)
            },
            null!);

        var result = await service.InferActionAsync(
            "Responde",
            detection,
            options,
            CancellationToken.None);

        var suggestion = Assert.IsType<TextSuggestionOutput>(result);
        Assert.DoesNotContain("Ollama", suggestion.SuggestedText);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task InferActionAsync_MetaOpenAnswer_RetriesOnce()
    {
        var runner = new StubGeminiCliProcessRunner(
            SuccessEnvelope(
                """{"answer":"El modelo de lenguaje responde preguntas en el contexto de la aplicación.","confidence":0.95}"""),
            SuccessEnvelope(
                """{"answer":"La copia masiva dificulta el control y la distribución global reduce la remuneración.","confidence":0.94}"""));
        var service = new GeminiCliInferenceService(runner);
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new List<OcrResult>
            {
                Ocr(
                    "Explica por qué Internet desafía el derecho de autor y menciona dos retos.",
                    20,
                    20),
                Ocr("Escribe una respuesta breve", 100, 200, 200, 40)
            },
            null!);

        var result = await service.InferActionAsync(
            "Responde",
            detection,
            CreateOptions(),
            CancellationToken.None);

        Assert.Contains(
            "copia masiva",
            Assert.IsType<TextSuggestionOutput>(result).SuggestedText);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task InferActionAsync_NonZeroExit_ThrowsUnavailable()
    {
        var runner = new StubGeminiCliProcessRunner(
            new GeminiCliProcessResult(
                1,
                string.Empty,
                "Warning: terminal colors unavailable\nError authenticating: unsupported account"));
        var service = new GeminiCliInferenceService(runner);

        var exception = await Assert.ThrowsAsync<InferenceUnavailableException>(() =>
            service.InferActionAsync(
                "Responde",
                CreateOpenTextDetection(),
                CreateOptions(),
                CancellationToken.None));

        Assert.Contains("exited with code 1", exception.Message);
        Assert.Contains("Error authenticating", exception.Message);
        Assert.DoesNotContain("Responde", exception.Message);
    }

    [Fact]
    public async Task InferActionAsync_CancelledRunner_PropagatesCancellation()
    {
        var runner = new CancellingGeminiCliProcessRunner();
        var service = new GeminiCliInferenceService(runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.InferActionAsync(
                "Responde",
                CreateOpenTextDetection(),
                CreateOptions(),
                new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Router_GeminiSelected_UsesGeminiProvider()
    {
        var runner = new StubGeminiCliProcessRunner(
            SuccessEnvelope("""{"answer":"B","confidence":0.93}"""));
        var gemini = new GeminiCliInferenceService(runner);
        var ollama = new OllamaInferenceService(new HttpClient(new ThrowingHttpHandler()));
        var router = new InferenceServiceRouter(ollama, gemini);
        var options = CreateOptions();
        options.InferenceProvider = InferenceProvider.GeminiCli;

        var result = await router.InferActionAsync(
            "Responde",
            CreateMultipleChoiceDetection(),
            options,
            CancellationToken.None);

        Assert.IsType<VisualIndicatorOutput>(result);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task ProcessRunner_UnsafeModelName_IsRejectedBeforeStartingProcess()
    {
        var runner = new GeminiCliProcessRunner();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync(
                "gemini.cmd",
                "prompt",
                "model & unsafe",
                CancellationToken.None));
    }

    [Fact]
    public async Task ProcessRunner_WindowsCommandScript_StartsAndReceivesStandardInput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "Aivsp.GeminiRunner.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "fake-gemini.cmd");
        var outputScriptPath = Path.Combine(directory, "fake-output.js");

        try
        {
            File.WriteAllText(
                outputScriptPath,
                """
                process.stdout.write(JSON.stringify({
                  session_id: "test",
                  response: "{\"answer\":\"distribución y piratería\",\"confidence\":0.95}",
                  stats: {}
                }));
                """);
            File.WriteAllText(
                scriptPath,
                """
                @echo off
                echo %* | findstr /c:"--extensions" >nul || exit /b 8
                echo %* | findstr /c:"none" >nul || exit /b 9
                echo %* | findstr /c:"--skip-trust" >nul || exit /b 10
                echo %* | findstr /c:"--admin-policy" >nul || exit /b 11
                echo %* | findstr /c:"aivsp-fast" >nul || exit /b 12
                if "%GEMINI_SYSTEM_MD%"=="" exit /b 13
                if not exist "%GEMINI_SYSTEM_MD%" exit /b 14
                if "%GEMINI_CLI_SYSTEM_SETTINGS_PATH%"=="" exit /b 15
                if not exist "%GEMINI_CLI_SYSTEM_SETTINGS_PATH%" exit /b 16
                set /p received=
                if "%received%"=="" exit /b 7
                node "%~dp0fake-output.js"
                """);
            var runner = new GeminiCliProcessRunner();

            var result = await runner.RunAsync(
                scriptPath,
                "pregunta por entrada estandar",
                AssistantOptions.DefaultGeminiModelName,
                CancellationToken.None);

            Assert.True(
                result.ExitCode == 0,
                $"ExitCode={result.ExitCode}; stdout={result.StandardOutput}; stderr={result.StandardError}");
            Assert.Contains("distribución y piratería", result.StandardOutput);
            Assert.DoesNotContain("distribuciÃ³n", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessRunner_WindowsCommandScript_RejectsShellMetacharacters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new GeminiCliProcessRunner();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync(
                "gemini&unexpected-command.cmd",
                "prompt",
                null,
                CancellationToken.None));
    }

    private static AssistantOptions CreateOptions() => new()
    {
        InferenceProvider = InferenceProvider.GeminiCli,
        GeminiCliExecutablePath = "gemini.cmd",
        OcrLanguage = "es-MX"
    };

    private static AssistanceDetectionResult CreateMultipleChoiceDetection() => new(
        AssistanceMode.MultipleChoice,
        new List<OcrResult>
        {
            Ocr("1. ¿Capital de Francia?", 20, 20),
            Ocr("A) Madrid", 20, 80),
            Ocr("B) París", 20, 120),
            Ocr("C) Roma", 20, 160)
        },
        null!);

    private static AssistanceDetectionResult CreateOpenTextDetection() => new(
        AssistanceMode.OpenText,
        new List<OcrResult>
        {
            Ocr("1. Escribe la capital de Francia.", 20, 20),
            Ocr("Escribe tu respuesta", 100, 200, 200, 40)
        },
        null!);

    private static OcrResult Ocr(
        string text,
        int x,
        int y,
        int width = 180,
        int height = 30) =>
        new(text, new BoundingBox(x, y, width, height), 1.0, OcrResultLevel.Line);

    private static GeminiCliProcessResult SuccessEnvelope(string modelResponse) =>
        new(
            0,
            JsonSerializer.Serialize(new
            {
                session_id = "test-session",
                response = modelResponse,
                stats = new { }
            }),
            string.Empty);

    private sealed class StubGeminiCliProcessRunner : IGeminiCliProcessRunner
    {
        private readonly Queue<GeminiCliProcessResult> _results;

        public StubGeminiCliProcessRunner(params GeminiCliProcessResult[] results)
        {
            _results = new Queue<GeminiCliProcessResult>(results);
        }

        public int CallCount { get; private set; }

        public Task<GeminiCliProcessResult> RunAsync(
            string executablePath,
            string prompt,
            string? modelName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class CancellingGeminiCliProcessRunner : IGeminiCliProcessRunner
    {
        public Task<GeminiCliProcessResult> RunAsync(
            string executablePath,
            string prompt,
            string? modelName,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<GeminiCliProcessResult>(
                cancellationToken.IsCancellationRequested
                    ? cancellationToken
                    : new CancellationToken(canceled: true));
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException("Ollama should not be called."));
    }
}
