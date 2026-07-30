using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.BrowserIntegration;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;

namespace Aivsp.Application.Tests.BrowserIntegration;

public class BrowserDomAssistantServiceTests
{
    private readonly Mock<IInferenceService> _inference = new();
    private readonly AssistantOptions _options = new()
    {
        InferenceProvider = InferenceProvider.GeminiCli
    };
    private readonly Mock<ILogger<BrowserDomAssistantService>> _logger = new();

    [Fact]
    public async Task AssistAsync_MultipleChoice_MapsSelectedDomOption()
    {
        _inference.Setup(service => service.InferActionAsync(
                It.IsAny<string>(),
                It.IsAny<AssistanceDetectionResult>(),
                _options,
                It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                AssistanceDetectionResult detection,
                AssistantOptions _,
                CancellationToken _) =>
            {
                var candidate = detection.OcrResults[2];
                var match = new MatchResult(candidate, 1, 1, MatchMethod.Exact);
                return Task.FromResult<AssistanceOutput>(
                    new VisualIndicatorOutput(
                        """{"answer":"B","confidence":0.98}""",
                        0.98,
                        TimeSpan.Zero,
                        candidate.Text,
                        match));
            });
        var assistant = CreateAssistant();
        var request = new BrowserQuestionRequest(
            "context-1",
            AssistanceMode.MultipleChoice,
            "¿Capital de Francia?",
            new[]
            {
                new BrowserOptionInput("A", "Madrid"),
                new BrowserOptionInput("B", "París"),
                new BrowserOptionInput("C", "Roma")
            },
            null);

        var result = await assistant.AssistAsync(
            request,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("B", result.OptionKey);
        Assert.Equal("París", result.Answer);
        Assert.Equal(0.98, result.Confidence);
        Assert.Equal(InferenceProvider.GeminiCli, result.Provider);
        Assert.Equal(OverlayProfile.Visible, result.OverlayProfile);
    }

    [Fact]
    public async Task AssistAsync_OpenText_ReturnsSuggestionWithoutOptions()
    {
        _inference.Setup(service => service.InferActionAsync(
                It.IsAny<string>(),
                It.Is<AssistanceDetectionResult>(detection =>
                    detection.Mode == AssistanceMode.OpenText),
                _options,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextSuggestionOutput(
                """{"answer":"París","confidence":0.95}""",
                0.95,
                TimeSpan.Zero,
                "París",
                0,
                0,
                100,
                30));
        var assistant = CreateAssistant();
        var request = new BrowserQuestionRequest(
            "context-2",
            AssistanceMode.OpenText,
            "Escribe la capital de Francia.",
            Array.Empty<BrowserOptionInput>(),
            "Respuesta");

        var result = await assistant.AssistAsync(
            request,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(result.OptionKey);
        Assert.Equal("París", result.Answer);
        Assert.Equal(AssistanceMode.OpenText, result.Mode);
    }

    [Fact]
    public async Task AssistAsync_DuplicateOptionKeys_RejectsBeforeInference()
    {
        var assistant = CreateAssistant();
        var request = new BrowserQuestionRequest(
            "context-3",
            AssistanceMode.MultipleChoice,
            "Pregunta",
            new[]
            {
                new BrowserOptionInput("A", "Uno"),
                new BrowserOptionInput("a", "Dos")
            },
            null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            assistant.AssistAsync(request, Guid.NewGuid(), CancellationToken.None));
        _inference.Verify(service => service.InferActionAsync(
            It.IsAny<string>(),
            It.IsAny<AssistanceDetectionResult>(),
            It.IsAny<AssistantOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssistAsync_OverlongQuestion_RejectsBeforeInference()
    {
        var assistant = CreateAssistant();
        var request = new BrowserQuestionRequest(
            "context-4",
            AssistanceMode.OpenText,
            new string('x', 8_001),
            Array.Empty<BrowserOptionInput>(),
            null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            assistant.AssistAsync(request, Guid.NewGuid(), CancellationToken.None));
        _inference.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AssistAsync_GeminiTimeout_UsesBrowserSpecificLimit()
    {
        _options.GeminiInferenceTimeoutSeconds = 75;
        _options.BrowserGeminiInferenceTimeoutSeconds = 1;
        _inference.Setup(service => service.InferActionAsync(
                It.IsAny<string>(),
                It.IsAny<AssistanceDetectionResult>(),
                _options,
                It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                AssistanceDetectionResult _,
                AssistantOptions _,
                CancellationToken cancellationToken) =>
                WaitForCancellationAsync(cancellationToken));
        var assistant = CreateAssistant();
        var request = new BrowserQuestionRequest(
            "context-timeout",
            AssistanceMode.OpenText,
            "Pregunta que no termina.",
            Array.Empty<BrowserOptionInput>(),
            null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            assistant.AssistAsync(request, Guid.NewGuid(), CancellationToken.None));
    }

    private BrowserDomAssistantService CreateAssistant() =>
        new(_inference.Object, _options, _logger.Object);

    private static async Task<AssistanceOutput> WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The cancellation token was not observed.");
    }
}
