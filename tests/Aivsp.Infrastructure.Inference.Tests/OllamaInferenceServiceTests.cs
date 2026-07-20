using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Xunit;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;
using Aivsp.Domain.ValueObjects;
using Aivsp.Domain.Enums;
using Aivsp.Infrastructure.Inference.Services;
using Aivsp.Infrastructure.Inference.Models;
using Aivsp.Infrastructure.Inference.Serialization;

namespace Aivsp.Infrastructure.Inference.Tests;

public class OllamaInferenceServiceTests
{
    [Fact]
    public async Task InferActionAsync_ValidResponse_ReturnsVisualIndicator()
    {
        var structuredResponse = new StructuredInferenceResponse
        {
            Mode = "MultipleChoice",
            Answer = "Submit",
            OptionId = 0,
            Confidence = 0.9,
            DebugReason = "Match found"
        };
        
        var expectedResponse = new OllamaResponse
        {
            Response = JsonSerializer.Serialize(structuredResponse)
        };
        
        var jsonResponse = JsonSerializer.Serialize(expectedResponse, OllamaJsonContext.Default.OllamaResponse);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new OllamaInferenceService(httpClient);
        var options = new AssistantOptions { ModelName = "phi3", OllamaEndpoint = "http://localhost:11434" };
        var ocrResults = new List<OcrResult> { new OcrResult("Submit", new BoundingBox(0, 0, 10, 10), 1.0, OcrResultLevel.Word) };
        var detection = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, null!);

        var result = await service.InferActionAsync("click submit", detection, options, CancellationToken.None);

        Assert.NotNull(result);
        var visualResult = Assert.IsType<VisualIndicatorOutput>(result);
        Assert.Equal("Submit", visualResult.SelectedText);
    }

    [Fact]
    public async Task InferActionAsync_NetworkError_ThrowsException()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new OllamaInferenceService(httpClient);
        var options = new AssistantOptions { ModelName = "phi3", OllamaEndpoint = "http://localhost:11434" };
        var detection = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, new List<OcrResult>(), null!);

        var ex = await Assert.ThrowsAsync<Exception>(() => service.InferActionAsync("click", detection, options, CancellationToken.None));
        Assert.Contains("Failed to infer action", ex.Message);
    }

    [Fact]
    public async Task InferActionAsync_CloseOptionIsNotRejectedAsQuestion()
    {
        var service = CreateService(new StructuredInferenceResponse
        {
            Mode = "MultipleChoice",
            Answer = "Fotosíntesis",
            OptionId = 3,
            Confidence = 0.98
        });
        var results = new List<OcrResult>
        {
            Line("1. ¿Qué proceso utilizan las plantas?", 100),
            Line("A) Quimiosíntesis", 135),
            Line("B) Fotosíntesis", 170),
            Line("C) Respiración celular", 205)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!, 300, 110),
            new AssistantOptions { ModelName = "llama3.2:3b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.Equal("B) Fotosíntesis", visual.SelectedText);
    }

    [Fact]
    public async Task InferActionAsync_OpenTextTargetsDetectedInputCue()
    {
        var service = CreateService(new StructuredInferenceResponse
        {
            Mode = "OpenText",
            SuggestedText = "11434",
            SuggestedX = 999,
            SuggestedY = 999,
            Confidence = 0.95
        });
        var results = new List<OcrResult>
        {
            Line("7. ¿Cuál es el puerto de Ollama?", 100),
            new OcrResult("Respuesta corta", new BoundingBox(120, 150, 240, 30), 1, OcrResultLevel.Line)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, results, null!, 300, 110),
            new AssistantOptions { ModelName = "llama3.2:3b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var text = Assert.IsType<TextSuggestionOutput>(output);
        Assert.Equal(240, text.SuggestedX);
        Assert.Equal(165, text.SuggestedY);
        Assert.Equal("11434", text.SuggestedText);
    }

    [Fact]
    public async Task InferActionAsync_OpenTextAcceptsAlternateJsonProperty()
    {
        var service = CreateService("{\"respuesta\":\"Liberar los handles evita fugas de recursos GDI.\"}");
        var results = new List<OcrResult>
        {
            Line("9. Explica por qué deben liberarse los handles GDI.", 100),
            Line("Explica brevemente...", 150)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, results, null!, 300, 110),
            new AssistantOptions { ModelName = "llama3.2:3b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var text = Assert.IsType<TextSuggestionOutput>(output);
        Assert.Equal("Liberar los handles evita fugas de recursos GDI.", text.SuggestedText);
    }

    [Fact]
    public async Task InferActionAsync_MultilineOptionMatchesFullGroupedText()
    {
        var service = CreateService(new StructuredInferenceResponse
        {
            Mode = "MultipleChoice",
            Answer = "B) Una arquitectura monolítica con fuerte acoplamiento a procedimientos almacenados y lógica de negocio codificada en disparadores.",
            OptionId = 1
        });
        var results = new List<OcrResult>
        {
            Line("13. Selecciona la arquitectura descrita.", 100),
            Line("A) Microservicios distribuidos mediante Kubernetes,", 140),
            Line("con despliegues blue-green constantes.", 165),
            Line("B) Una arquitectura monolítica con fuerte acoplamiento", 200),
            Line("a procedimientos almacenados y lógica de negocio codificada en disparadores.", 225)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!, 300, 110),
            new AssistantOptions { ModelName = "llama3.2:3b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.StartsWith("B) Una arquitectura", visual.SelectedText);
        Assert.Equal(200, visual.Match.Candidate.Bounds.Y);
    }

    [Fact]
    public async Task InferActionAsync_SpanishOcrForcesSpanishOpenAnswerPrompt()
    {
        string? sentRequest = null;
        var response = new OllamaResponse { Response = "{\"answer\":\"Evita fugas de recursos.\"}" };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                sentRequest = document.RootElement.GetProperty("prompt").GetString();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response, OllamaJsonContext.Default.OllamaResponse))
            });
        var service = new OllamaInferenceService(new HttpClient(handler.Object));

        await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, new[] { Line("9. Explica el riesgo.", 100) }, null!),
            new AssistantOptions
            {
                ModelName = "llama3.2:3b",
                OllamaEndpoint = "http://localhost:11434",
                OcrLanguage = "es-MX"
            },
            CancellationToken.None);

        Assert.NotNull(sentRequest);
        Assert.Contains("exclusivamente en español", sentRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No uses portugués", sentRequest, StringComparison.OrdinalIgnoreCase);
    }

    private static OllamaInferenceService CreateService(StructuredInferenceResponse structuredResponse)
    {
        return CreateService(JsonSerializer.Serialize(structuredResponse));
    }

    private static OllamaInferenceService CreateService(string rawModelResponse)
    {
        var response = new OllamaResponse { Response = rawModelResponse };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response, OllamaJsonContext.Default.OllamaResponse))
            });
        return new OllamaInferenceService(new HttpClient(handler.Object));
    }

    private static OcrResult Line(string text, int y) =>
        new(text, new BoundingBox(100, y, 300, 24), 1, OcrResultLevel.Line);
}
