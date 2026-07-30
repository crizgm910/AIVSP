using System;
using System.Collections.Generic;
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
using Aivsp.Infrastructure.Inference.Models;
using Aivsp.Infrastructure.Inference.Serialization;
using Aivsp.Infrastructure.Inference.Services;
using Moq;
using Moq.Protected;
using Xunit;

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

        var ex = await Assert.ThrowsAsync<InferenceUnavailableException>(() =>
            service.InferActionAsync("click", detection, options, CancellationToken.None));
        Assert.Contains("Failed to infer action", ex.Message);
    }

    [Fact]
    public async Task InferActionAsync_ModelNotFound_ThrowsTypedModelException()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent("{\"error\":\"model not found\"}")
            });

        var service = new OllamaInferenceService(new HttpClient(handlerMock.Object));
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[] { Line("Explica una prueba.", 100) },
            null!);

        await Assert.ThrowsAsync<InferenceModelUnavailableException>(() =>
            service.InferActionAsync(
                "answer",
                detection,
                new AssistantOptions
                {
                    ModelName = "missing-model",
                    OllamaEndpoint = "http://localhost:11434"
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task InferActionAsync_InvalidModelJson_ThrowsTypedResponseException()
    {
        var service = CreateService("not valid json");
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[] { Line("Explica brevemente", 100) },
            null!);

        var ex = await Assert.ThrowsAsync<InferenceResponseException>(() =>
            service.InferActionAsync(
                "answer",
                detection,
                new AssistantOptions { OllamaEndpoint = "http://localhost:11434" },
                CancellationToken.None));

        Assert.DoesNotContain("not valid json", ex.Message, StringComparison.Ordinal);
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
    public async Task InferActionAsync_OpenTextPrefersFieldCueWhenQuestionRepeatsPlaceholderText()
    {
        var service = CreateService(new StructuredInferenceResponse
        {
            Mode = "OpenText",
            SuggestedText = "DeleteObject evita fugas de recursos GDI.",
            Confidence = 0.95
        });
        var results = new List<OcrResult>
        {
            Line("9. Explica brevemente por qué se debe usar DeleteObject.", 100),
            Line("Explica brevemente...", 180)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, results, null!, 300, 110),
            new AssistantOptions { ModelName = "llama3.2:3b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var text = Assert.IsType<TextSuggestionOutput>(output);
        Assert.Equal(250, text.SuggestedX);
        Assert.Equal(192, text.SuggestedY);
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
    public async Task InferActionAsync_DuplicatedOcrRadioPrefixUsesActualOptionLetter()
    {
        var service = CreateService(new StructuredInferenceResponse
        {
            Mode = "MultipleChoice",
            Answer = "C",
            Confidence = 0.95
        });
        var results = new List<OcrResult>
        {
            Line("2. Selecciona la definición correcta de un Sistema operativo.", 100),
            Line("C) A) Es el hardware físico donde se ejecuta todo", 135),
            Line("C) B) Es una aplicación para editar documentos", 170),
            Line("C) C) Es el software que administra el hardware", 205)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!, 300, 110),
            new AssistantOptions { ModelName = "qwen3:4b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.Equal(205, visual.Match.Candidate.Bounds.Y);
    }

    [Fact]
    public async Task InferActionAsync_MissingAnswerRetriesOnceAndSucceeds()
    {
        var responses = new Queue<string>(new[]
        {
            "{\"confidence\":0.95}",
            "{\"answer\":\"11434\",\"confidence\":0.95}"
        });
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(
                    new OllamaResponse { Response = responses.Dequeue() },
                    OllamaJsonContext.Default.OllamaResponse))
            });
        var service = new OllamaInferenceService(new HttpClient(handler.Object));

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, new[] { Line("7. ¿Cuál es el puerto de Ollama?", 100) }, null!),
            new AssistantOptions { ModelName = "qwen3:4b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        Assert.Equal("11434", Assert.IsType<TextSuggestionOutput>(output).SuggestedText);
        handler.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
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
                sentRequest = document.RootElement.GetProperty("system").GetString();
            })
            .ReturnsAsync(() => new HttpResponseMessage
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
        Assert.Contains("confidence", sentRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cada requisito explícito", sentRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dos elementos", sentRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InferActionAsync_DisablesModelThinkingForFastStructuredResponse()
    {
        bool? think = null;
        double? temperature = null;
        var response = new OllamaResponse
        {
            Response = "{\"answer\":\"Respuesta breve\",\"confidence\":0.95}"
        };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                think = document.RootElement.GetProperty("think").GetBoolean();
                temperature = document.RootElement
                    .GetProperty("options")
                    .GetProperty("temperature")
                    .GetDouble();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response, OllamaJsonContext.Default.OllamaResponse))
            });

        await new OllamaInferenceService(new HttpClient(handler.Object)).InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, new[] { Line("Pregunta", 100) }, null!),
            new AssistantOptions { ModelName = "qwen3:4b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        Assert.False(think);
        Assert.Equal(0, temperature);
    }

    [Fact]
    public async Task InferActionAsync_MultipleChoiceReservesEnoughTokensForCompleteJson()
    {
        int? numPredict = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                numPredict = document.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32();
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(
                    new OllamaResponse { Response = "{\"answer\":\"B\",\"confidence\":0.95}" },
                    OllamaJsonContext.Default.OllamaResponse))
            });

        await new OllamaInferenceService(new HttpClient(handler.Object)).InferActionAsync(
            "responde",
            new AssistanceDetectionResult(
                AssistanceMode.MultipleChoice,
                new[] { Line("1. Pregunta", 100), Line("A) Uno", 140), Line("B) Dos", 180) },
                null!),
            new AssistantOptions { ModelName = "qwen3:4b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        Assert.True(numPredict >= 32);
    }

    [Fact]
    public async Task InferActionAsync_OpenTextReservesEnoughTokensForCompleteJson()
    {
        int? numPredict = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                numPredict = document.RootElement
                    .GetProperty("options")
                    .GetProperty("num_predict")
                    .GetInt32();
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(
                    new OllamaResponse
                    {
                        Response =
                            "{\"answer\":\"Respuesta completa y breve.\",\"confidence\":0.95}"
                    },
                    OllamaJsonContext.Default.OllamaResponse))
            });

        await new OllamaInferenceService(new HttpClient(handler.Object)).InferActionAsync(
            "responde",
            new AssistanceDetectionResult(
                AssistanceMode.OpenText,
                new[] { Line("21. Explica dos retos específicos.", 100) },
                null!),
            new AssistantOptions
            {
                ModelName = "qwen3:4b",
                OllamaEndpoint = "http://localhost:11434"
            },
            CancellationToken.None);

        Assert.True(numPredict >= 96);
    }

    [Fact]
    public async Task InferActionAsync_IncludesConfiguredLocalKnowledgeContext()
    {
        string? sentPrompt = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                sentPrompt = document.RootElement.GetProperty("prompt").GetString();
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(
                    new OllamaResponse { Response = "{\"answer\":\"11434\",\"confidence\":0.95}" },
                    OllamaJsonContext.Default.OllamaResponse))
            });

        await new OllamaInferenceService(new HttpClient(handler.Object)).InferActionAsync(
            "responde",
            new AssistanceDetectionResult(
                AssistanceMode.OpenText,
                new[] { Line("7. ¿Cuál es el endpoint comprobado y el puerto de Ollama?", 100) },
                null!),
            new AssistantOptions
            {
                ModelName = "qwen3:4b",
                OllamaEndpoint = "http://localhost:11434",
                LocalKnowledgeContext = "El endpoint comprobado usa el puerto 11434."
            },
            CancellationToken.None);

        Assert.Contains("El endpoint comprobado usa el puerto 11434.", sentPrompt);
    }

    [Fact]
    public async Task InferActionAsync_UnrelatedOpenQuestionExcludesLocalKnowledgeFromPrompt()
    {
        string? sentPrompt = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                sentPrompt = document.RootElement.GetProperty("prompt").GetString();
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(
                    new OllamaResponse
                    {
                        Response =
                            "{\"answer\":\"La copia y la distribución global dificultan el control.\",\"confidence\":0.95}"
                    },
                    OllamaJsonContext.Default.OllamaResponse))
            });
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[]
            {
                Line(
                    "21. Explica dos retos que enfrentan los creadores en la era digital.",
                    100),
                Line("Escriba su respuesta", 140)
            },
            null!);
        var options = new AssistantOptions
        {
            LocalKnowledgeContext =
                "Datos comprobados de AIVSP: el endpoint local de Ollama es http://127.0.0.1:11434."
        };

        await new OllamaInferenceService(new HttpClient(handler.Object))
            .InferActionAsync(
                "responde",
                detection,
                options,
                CancellationToken.None);

        Assert.DoesNotContain("127.0.0.1", sentPrompt);
        Assert.DoesNotContain("CONTEXTO LOCAL VERIFICADO", sentPrompt);
        Assert.Contains("dos retos que enfrentan los creadores", sentPrompt);
    }

    [Fact]
    public async Task InferActionAsync_QuestionStartingWithInputCueRemainsInPrompt()
    {
        string? sentPrompt = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                sentPrompt = document.RootElement.GetProperty("prompt").GetString();
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(
                    new OllamaResponse
                    {
                        Response =
                            "{\"answer\":\"La copia global dificulta el control y la piratería reduce ingresos.\",\"confidence\":0.95}"
                    },
                    OllamaJsonContext.Default.OllamaResponse))
            });
        const string question =
            "Explica brevemente por qué Internet desafía el derecho de autor y menciona dos retos.";
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[]
            {
                Line(question, 100),
                Line("Explica brevemente...", 140)
            },
            null!);

        await new OllamaInferenceService(new HttpClient(handler.Object))
            .InferActionAsync(
                "responde",
                detection,
                new AssistantOptions(),
                CancellationToken.None);

        Assert.Contains(question, sentPrompt);
        Assert.DoesNotContain("Explica brevemente...", sentPrompt);
    }

    [Fact]
    public async Task InferActionAsync_UnrelatedOpenQuestionRetriesLocalContextLeak()
    {
        var requestCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                requestCount++;
                var answer = requestCount == 1
                    ? "El endpoint local de Ollama es http://127.0.0.1:11434"
                    : "La copia no autorizada y la distribución global dificultan el control y la remuneración.";
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(
                        new OllamaResponse
                        {
                            Response =
                                $"{{\"answer\":\"{answer}\",\"confidence\":0.95}}"
                        },
                        OllamaJsonContext.Default.OllamaResponse))
                };
            });
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[]
            {
                Line(
                    "21. Explica dos retos que enfrentan los creadores en la era digital.",
                    100),
                Line("Escriba su respuesta", 140)
            },
            null!);

        var output = await service.InferActionAsync(
            "responde",
            detection,
            new AssistantOptions
            {
                LocalKnowledgeContext =
                    "Datos comprobados de AIVSP: el endpoint local de Ollama es http://127.0.0.1:11434."
            },
            CancellationToken.None);

        Assert.Equal(2, requestCount);
        Assert.Contains(
            "copia no autorizada",
            Assert.IsType<TextSuggestionOutput>(output).SuggestedText);
    }

    [Fact]
    public async Task InferActionAsync_MetaOpenAnswer_RetriesOnce()
    {
        var requestCount = 0;
        var sentPrompts = new List<string>();
        var sentSystems = new List<string>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(json);
                sentPrompts.Add(
                    document.RootElement.GetProperty("prompt").GetString()!);
                sentSystems.Add(
                    document.RootElement.GetProperty("system").GetString()!);
            })
            .ReturnsAsync(() =>
            {
                requestCount++;
                var answer = requestCount == 1
                    ? "El modelo de lenguaje de la pregunta genera respuestas en el contexto de la aplicación."
                    : "La copia instantánea dificulta el control y la distribución global reduce la remuneración.";
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(
                        new OllamaResponse
                        {
                            Response =
                                $"{{\"answer\":\"{answer}\",\"confidence\":0.95}}"
                        },
                        OllamaJsonContext.Default.OllamaResponse))
                };
            });
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[]
            {
                Line(
                    "Explica por qué Internet desafía el derecho de autor y menciona dos retos para los creadores.",
                    100),
                Line("Escribe tu respuesta", 140)
            },
            null!);

        var output = await new OllamaInferenceService(new HttpClient(handler.Object))
            .InferActionAsync(
                "responde",
                detection,
                new AssistantOptions(),
                CancellationToken.None);

        Assert.Equal(2, requestCount);
        Assert.Equal(2, sentPrompts.Count);
        Assert.Equal(2, sentSystems.Count);
        var retryPrompt = sentPrompts[1];
        var retrySystem = sentSystems[1];
        Assert.StartsWith(
            "CORRECCIÓN",
            retrySystem,
            StringComparison.Ordinal);
        var questionIndex = retryPrompt.IndexOf(
            "PREGUNTA:",
            StringComparison.Ordinal);
        Assert.True(questionIndex >= 0);
        Assert.DoesNotContain(
            "CORRECCIÓN",
            retryPrompt,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "Explica por qué Internet desafía el derecho de autor y menciona dos retos para los creadores.",
            retryPrompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "copia instantánea",
            Assert.IsType<TextSuggestionOutput>(output).SuggestedText);
    }

    [Fact]
    public async Task InferActionAsync_TwoRequestedItemsButOneReturned_RetriesOnce()
    {
        var requestCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                requestCount++;
                var answer = requestCount == 1
                    ? "Piratería"
                    : "Piratería y pérdida de ingresos.";
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(
                        new OllamaResponse
                        {
                            Response =
                                $"{{\"answer\":\"{answer}\",\"confidence\":0.95}}"
                        },
                        OllamaJsonContext.Default.OllamaResponse))
                };
            });
        var detection = new AssistanceDetectionResult(
            AssistanceMode.OpenText,
            new[] { Line("Menciona dos retos para los creadores digitales.", 100) },
            null!);

        var output = await new OllamaInferenceService(new HttpClient(handler.Object))
            .InferActionAsync(
                "responde",
                detection,
                new AssistantOptions(),
                CancellationToken.None);

        Assert.Equal(2, requestCount);
        Assert.Equal(
            "Piratería y pérdida de ingresos.",
            Assert.IsType<TextSuggestionOutput>(output).SuggestedText);
    }

    [Fact]
    public async Task InferActionAsync_ExactVerifiedLegalFactSelectsCorrectOptionLocally()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var detection = new AssistanceDetectionResult(
            AssistanceMode.MultipleChoice,
            new[]
            {
                Line(
                    "2. ¿Cuál es el plazo de protección del derecho patrimonial en México, según la LFDA?",
                    100),
                Line("A) La vida del autor más 50 años después de su muerte.", 140),
                Line("B) La vida del autor más 75 años después de su muerte.", 180),
                Line("C) Cien años a partir de la publicación de la obra.", 220),
                Line("D) La vida del autor más 100 años después de su muerte.", 260)
            },
            null!);

        var output = await service.InferActionAsync(
            "responde",
            detection,
            new AssistantOptions
            {
                LocalKnowledgeContext =
                    "LFDA artículo 29: los derechos patrimoniales están vigentes durante la vida del autor y, a partir de su muerte, cien años más; equivale a la vida del autor más 100 años después de su muerte."
            },
            CancellationToken.None);

        Assert.Equal(
            "D) La vida del autor más 100 años después de su muerte.",
            Assert.IsType<VisualIndicatorOutput>(output).SelectedText);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData("{\"prompt\":\"Responde la pregunta\",\"confidence\":0.95}")]
    [InlineData("{\"answer\":\"Return only JSON with confidence from 0 to 1: {\\\"answer\\\":\\\"answer\\\",\\\"confidence\\\":0.95}.\",\"confidence\":0.95}")]
    [InlineData("{\"answer\":\"La respuesta anterior fue vacía, irrelevante o incompleta. Devuelve solamente el objeto JSON.\",\"confidence\":0.95}")]
    public async Task InferActionAsync_PromptEchoIsRejected(string modelResponse)
    {
        var service = CreateService(modelResponse);

        await Assert.ThrowsAsync<InferenceResponseException>(() => service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.OpenText, new[] { Line("10. Pregunta técnica", 100) }, null!),
            new AssistantOptions { ModelName = "qwen3:4b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None));
    }

    [Fact]
    public async Task InferActionAsync_FractionOfTotalRemainingUsesDeterministicMath()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("11. Prueba matemática de reconocimiento (Números y símbolos):", 100),
            Line("Si tenemos 15 manzanas y repartimos 3/5 del total entre nuestros 2 amigos, ¿Cuántas manzanas restan?", 130),
            Line("A) 3 manzanas", 170),
            Line("B) 6 manzanas", 205),
            Line("C) 9 manzanas", 240),
            Line("D) 12 manzanas", 275)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions { ModelName = "qwen3:4b", OllamaEndpoint = "http://localhost:11434" },
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.Equal("B) 6 manzanas", visual.SelectedText);
        Assert.Equal(205, visual.Match.Candidate.Bounds.Y);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task InferActionAsync_ExplicitArithmeticExpressionUsesDeterministicMath()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("12. ¿Cuánto es 12 + 8?", 100),
            Line("A) 18", 140),
            Line("B) 20", 175),
            Line("C) 22", 210)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions(),
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.Equal("B) 20", visual.SelectedText);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task InferActionAsync_SplitOcrArithmeticQuestionUsesDeterministicMath()
    {
        var handler = new Mock<HttpMessageHandler>();
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("1. ¿Cuál es el", 157),
            Line("resultado", 157),
            Line("12 + 8?", 157),
            Line("A) 18", 201),
            Line("B) 19", 223),
            Line("C) 20", 245),
            Line("D) 21", 267),
            Line("afi", 402)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions(),
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.Equal("C) 20", visual.SelectedText);
        Assert.Equal(TimeSpan.Zero, visual.Duration);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task InferActionAsync_ArithmeticContinuationReportedAfterOptionsUsesDeterministicMath()
    {
        var handler = new Mock<HttpMessageHandler>();
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("1. ¿Cuál es el resultado?", 100),
            Line("A) 18", 140),
            Line("B) 19", 175),
            Line("C) 20", 210),
            Line("D) 21", 245),
            Line("12 + 8?", 300)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions(),
            CancellationToken.None);

        Assert.Equal("C) 20", Assert.IsType<VisualIndicatorOutput>(output).SelectedText);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task InferActionAsync_MultipleArithmeticExpressionsFallBackToOllama()
    {
        var response = new OllamaResponse { Response = "{\"answer\":\"B\",\"confidence\":0.9}" };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response, OllamaJsonContext.Default.OllamaResponse))
            });
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("1. ¿Cuál igualdad está escrita correctamente?", 100),
            Line("A) 12 + 8 = 19", 140),
            Line("B) 12 + 8 = 20", 175),
            Line("C) 12 + 8 = 21", 210)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions(),
            CancellationToken.None);

        Assert.Equal("B) 12 + 8 = 20", Assert.IsType<VisualIndicatorOutput>(output).SelectedText);
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }


    [Fact]
    public async Task InferActionAsync_VerifiedKnowledgeSelectsStrongUniqueOptionWithoutOllama()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("4. El patrón Clean Architecture se caracteriza principalmente por:", 100),
            Line("A) Depender fuertemente de la base de datos en todas las capas", 140),
            Line("B) Usar frameworks UI para definir la lógica de negocio central", 175),
            Line("C) La regla de dependencia: el código fuente solo puede apuntar hacia adentro", 210),
            Line("D) La regla de independencia: el código fuente solo puede apuntar hacia afuera", 245)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions
            {
                LocalKnowledgeContext =
                    "Clean Architecture aplica la regla de dependencia: el código fuente solo puede apuntar hacia adentro."
            },
            CancellationToken.None);

        var visual = Assert.IsType<VisualIndicatorOutput>(output);
        Assert.Equal("C) La regla de dependencia: el código fuente solo puede apuntar hacia adentro", visual.SelectedText);
        Assert.Equal(210, visual.Match.Candidate.Bounds.Y);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task InferActionAsync_WeakKnowledgeMatchFallsBackToOllama()
    {
        var response = new OllamaResponse { Response = "{\"answer\":\"B\",\"confidence\":0.9}" };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response, OllamaJsonContext.Default.OllamaResponse))
            });
        var service = new OllamaInferenceService(new HttpClient(handler.Object));
        var results = new List<OcrResult>
        {
            Line("4. El patrón Clean Architecture se caracteriza por:", 100),
            Line("A) Depender de la base de datos", 140),
            Line("B) Usar frameworks UI", 175),
            Line("C) El código fuente apunta hacia adentro", 210),
            Line("D) El código fuente apunta hacia afuera", 245)
        };

        var output = await service.InferActionAsync(
            "responde",
            new AssistanceDetectionResult(AssistanceMode.MultipleChoice, results, null!),
            new AssistantOptions { LocalKnowledgeContext = "El código fuente puede usar base de datos y frameworks." },
            CancellationToken.None);

        Assert.Equal("B) Usar frameworks UI", Assert.IsType<VisualIndicatorOutput>(output).SelectedText);
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
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
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response, OllamaJsonContext.Default.OllamaResponse))
            });
        return new OllamaInferenceService(new HttpClient(handler.Object));
    }

    private static OcrResult Line(string text, int y) =>
        new(text, new BoundingBox(100, y, 300, 24), 1, OcrResultLevel.Line);
}
