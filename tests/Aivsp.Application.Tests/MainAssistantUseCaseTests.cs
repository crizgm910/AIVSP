using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.UseCases;
using Aivsp.Domain.Entities;
using Aivsp.Domain.ValueObjects;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.Tests;

public class MainAssistantUseCaseTests
{
    private readonly Mock<IImageCaptureService> _captureMock;
    private readonly Mock<IOcrEngine> _ocrMock;
    private readonly Mock<IQuestionTypeDetector> _detectorMock;
    private readonly Mock<IInferenceService> _inferenceMock;
    private readonly Mock<IOverlayService> _overlayMock;
    private readonly Mock<IInputSimulationService> _inputMock;
    private readonly Mock<ILogger<MainAssistantUseCase>> _loggerMock;
    private readonly AssistantOptions _options;
    private readonly MainAssistantUseCase _useCase;

    public MainAssistantUseCaseTests()
    {
        _captureMock = new Mock<IImageCaptureService>();
        _ocrMock = new Mock<IOcrEngine>();
        _detectorMock = new Mock<IQuestionTypeDetector>();
        _inferenceMock = new Mock<IInferenceService>();
        _overlayMock = new Mock<IOverlayService>();
        _inputMock = new Mock<IInputSimulationService>();
        _loggerMock = new Mock<ILogger<MainAssistantUseCase>>();
        _options = new AssistantOptions();

        _useCase = new MainAssistantUseCase(
            _captureMock.Object,
            _ocrMock.Object,
            _detectorMock.Object,
            _inferenceMock.Object,
            _overlayMock.Object,
            _inputMock.Object,
            _options,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleChoice_ShowsIndicator()
    {
        // Arrange
        var ocrResults = new List<OcrResult> { new OcrResult("A) Option 1", new BoundingBox(10, 10, 50, 20), 0.9, OcrResultLevel.Word) };
        var matchResult = new MatchResult(ocrResults[0], 1.0, 1.0, MatchMethod.Exact);
        var visualOutput = new VisualIndicatorOutput("response", 1.0, TimeSpan.FromMilliseconds(100), "A) Option 1", matchResult);
        var detectionResult = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, null!);

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);

        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);

        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), detectionResult, _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(visualOutput);

        // Act
        await _useCase.ExecuteAsync("select option A");

        // Assert
        _overlayMock.Verify(x => x.DrawIndicatorAsync(matchResult, _options.OverlayDurationMilliseconds), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OpenText_ShowsTextSuggestion()
    {
        // Arrange
        var ocrResults = new List<OcrResult>
        {
            new OcrResult("Escribe tu respuesta", new BoundingBox(10, 10, 100, 20), 0.9, OcrResultLevel.Line)
        };
        var textOutput = new TextSuggestionOutput("response", 1.0, TimeSpan.FromMilliseconds(100), "Hello", 100, 100, 200, 50);
        var detectionResult = new AssistanceDetectionResult(AssistanceMode.OpenText, ocrResults, null!);

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);

        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);

        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(textOutput);

        // Act
        await _useCase.ExecuteAsync("write hello");

        // Assert
        _inputMock.Verify(x => x.SimulatePasteAsync("Hello", 100, 100, It.IsAny<CancellationToken>()), Times.Once);
        _overlayMock.Verify(x => x.DrawTextSuggestionAsync(textOutput, 2000), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ClearsPreviousOverlayBeforeCapture()
    {
        var ocrResults = new List<OcrResult>
        {
            new OcrResult("Pregunta", new BoundingBox(10, 10, 50, 20), 0.9, OcrResultLevel.Line)
        };
        var detectionResult = new AssistanceDetectionResult(AssistanceMode.Unknown, ocrResults, null!);

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);

        await _useCase.ExecuteAsync("do something");

        _overlayMock.Verify(x => x.Clear(), Times.Once);
        _captureMock.Verify(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CanceledRequest_DoesNotDrawStaleIndicator()
    {
        var cancellation = new CancellationTokenSource();
        var ocrResults = new List<OcrResult>
        {
            new OcrResult("A) Option 1", new BoundingBox(10, 10, 50, 20), 0.9, OcrResultLevel.Line)
        };
        var matchResult = new MatchResult(ocrResults[0], 1.0, 1.0, MatchMethod.Exact);
        var visualOutput = new VisualIndicatorOutput("response", 1.0, TimeSpan.Zero, "A) Option 1", matchResult);
        var detectionResult = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, null!);

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);
        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cancellation.Cancel();
                return visualOutput;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _useCase.ExecuteAsync("select option A", cancellationToken: cancellation.Token));

        _overlayMock.Verify(x => x.DrawIndicatorAsync(It.IsAny<MatchResult>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownMode_Aborts()
    {
        // Arrange
        var ocrResults = new List<OcrResult>();
        var detectionResult = new AssistanceDetectionResult(AssistanceMode.Unknown, ocrResults, null!);

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);

        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);

        // Act
        await _useCase.ExecuteAsync("do something");

        // Assert
        _inferenceMock.Verify(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), It.IsAny<AssistantOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        _overlayMock.Verify(x => x.DrawIndicatorAsync(It.IsAny<MatchResult>(), It.IsAny<int>()), Times.Never);
        _overlayMock.Verify(x => x.DrawTextSuggestionAsync(It.IsAny<TextSuggestionOutput>(), It.IsAny<int>()), Times.Never);
    }
}
