using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.AutoPaste;
using Aivsp.Application.Configuration;
using Aivsp.Application.Contracts;
using Aivsp.Application.Exceptions;
using Aivsp.Application.Notifications;
using Aivsp.Application.UseCases;
using Aivsp.Domain.Entities;
using Aivsp.Domain.Enums;
using Aivsp.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aivsp.Application.Tests;

public class MainAssistantUseCaseTests
{
    private readonly Mock<IImageCaptureService> _captureMock;
    private readonly Mock<IOcrEngine> _ocrMock;
    private readonly Mock<IQuestionTypeDetector> _detectorMock;
    private readonly Mock<IInferenceService> _inferenceMock;
    private readonly Mock<IOverlayService> _overlayMock;
    private readonly Mock<IInputSimulationService> _inputMock;
    private readonly Mock<IAutoPasteSecurityService> _autoPasteSecurityMock;
    private readonly Mock<INotificationService> _notificationMock;
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
        _autoPasteSecurityMock = new Mock<IAutoPasteSecurityService>();
        _autoPasteSecurityMock
            .Setup(x => x.Validate(It.IsAny<AutoPasteRequest>()))
            .Returns(AutoPasteValidationResult.Allowed());
        _notificationMock = new Mock<INotificationService>();
        _loggerMock = new Mock<ILogger<MainAssistantUseCase>>();
        _options = new AssistantOptions();

        _useCase = new MainAssistantUseCase(
            _captureMock.Object,
            _ocrMock.Object,
            _detectorMock.Object,
            _inferenceMock.Object,
            _overlayMock.Object,
            _inputMock.Object,
            _autoPasteSecurityMock.Object,
            _notificationMock.Object,
            _options,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_DesktopOcrDisabled_DoesNotCaptureAndNotifiesWarning()
    {
        _options.DesktopOcrEnabled = false;

        await _useCase.ExecuteAsync("do not capture");

        _captureMock.Verify(
            x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _ocrMock.Verify(
            x => x.ExtractTextAsync(
                It.IsAny<CapturedImage>(),
                It.IsAny<AssistantOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _overlayMock.Verify(x => x.Clear(), Times.Never);
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Warning &&
            notification.Code == NotificationCode.DesktopCaptureDisabled)), Times.Once);
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
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Processing &&
            notification.Code == NotificationCode.ExecutionStarted)), Times.Once);
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Completed &&
            notification.Code == NotificationCode.ExecutionCompleted)), Times.Once);
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
        _options.OverlayDurationMilliseconds = 4_321;

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
        _overlayMock.Verify(x => x.DrawTextSuggestionAsync(textOutput, 4_321), Times.Once);
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
    public async Task ExecuteAsync_ProcessingNotificationIsShownAfterOcrAndBeforeInference()
    {
        var events = new List<string>();
        var ocrResults = new List<OcrResult>
        {
            new("A) Option 1", new BoundingBox(10, 10, 50, 20), 0.9, OcrResultLevel.Line),
            new("B) Option 2", new BoundingBox(10, 40, 50, 20), 0.9, OcrResultLevel.Line)
        };
        var detectionResult = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, null!);
        var match = new MatchResult(ocrResults[0], 1, 1, MatchMethod.Exact);
        var output = new VisualIndicatorOutput("response", 1, TimeSpan.Zero, ocrResults[0].Text, match);

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("capture"))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("ocr"))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);
        _notificationMock.Setup(x => x.Notify(It.Is<UserNotification>(notification =>
                notification.Status == NotificationStatus.Processing)))
            .Callback(() => events.Add("processing"));
        _inferenceMock.Setup(x => x.InferActionAsync(
                It.IsAny<string>(), detectionResult, _options, It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("inference"))
            .ReturnsAsync(output);

        await _useCase.ExecuteAsync("select option A");

        Assert.Equal(new[] { "capture", "ocr", "processing", "inference" }, events);
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
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Cancelled &&
            notification.Code == NotificationCode.ExecutionCancelled)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GeminiTimeout_NotifiesWarningInsteadOfManualCancellation()
    {
        var ocrResults = new List<OcrResult>
        {
            new("A) Option 1", new BoundingBox(10, 10, 50, 20), 0.9, OcrResultLevel.Line)
        };
        var detectionResult = new AssistanceDetectionResult(
            AssistanceMode.MultipleChoice,
            ocrResults,
            null!);
        _options.InferenceProvider = InferenceProvider.GeminiCli;
        _options.GeminiInferenceTimeoutSeconds = 1;
        _options.InferenceTimeoutSeconds = 30;

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(
                It.IsAny<CapturedImage>(),
                _options,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);
        _inferenceMock.Setup(x => x.InferActionAsync(
                It.IsAny<string>(),
                detectionResult,
                _options,
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string _,
                AssistanceDetectionResult _,
                AssistantOptions _,
                CancellationToken cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });

        await _useCase.ExecuteAsync("select option A");

        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Warning &&
            notification.Code == NotificationCode.InferenceTimedOut)), Times.Once);
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Code == NotificationCode.ExecutionCancelled)), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SlowOcr_DoesNotConsumeGeminiInferenceTimeout()
    {
        var ocrResults = new List<OcrResult>
        {
            new("A) Option 1", new BoundingBox(10, 10, 50, 20), 0.9, OcrResultLevel.Line)
        };
        var detectionResult = new AssistanceDetectionResult(
            AssistanceMode.MultipleChoice,
            ocrResults,
            null!);
        var match = new MatchResult(ocrResults[0], 1, 1, MatchMethod.Exact);
        var output = new VisualIndicatorOutput(
            "response",
            1,
            TimeSpan.Zero,
            ocrResults[0].Text,
            match);
        _options.InferenceProvider = InferenceProvider.GeminiCli;
        _options.GeminiInferenceTimeoutSeconds = 1;

        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(
                It.IsAny<CapturedImage>(),
                _options,
                It.IsAny<CancellationToken>()))
            .Returns(async (
                CapturedImage _,
                AssistantOptions _,
                CancellationToken cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1_100), cancellationToken);
                return ocrResults;
            });
        _detectorMock.Setup(x => x.Detect(ocrResults, It.IsAny<CapturedImage>()))
            .Returns(detectionResult);
        _inferenceMock.Setup(x => x.InferActionAsync(
                It.IsAny<string>(),
                detectionResult,
                _options,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);

        await _useCase.ExecuteAsync("select option A");

        _overlayMock.Verify(
            x => x.DrawIndicatorAsync(match, _options.OverlayDurationMilliseconds),
            Times.Once);
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Code == NotificationCode.InferenceTimedOut)), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GeminiAutoPaste_UsesProviderSpecificMaximumContextAge()
    {
        var ocrResults = new[]
        {
            new OcrResult(
                "Escribe tu respuesta",
                new BoundingBox(10, 10, 120, 20),
                1,
                OcrResultLevel.Line)
        };
        var detection = new AssistanceDetectionResult(AssistanceMode.OpenText, ocrResults, null!);
        var output = new TextSuggestionOutput(
            "response",
            1,
            TimeSpan.Zero,
            "answer",
            100,
            100,
            200,
            50);
        _options.InferenceProvider = InferenceProvider.GeminiCli;
        _options.GeminiAutoPasteMaximumContextAgeSeconds = 91;
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(
                It.IsAny<CapturedImage>(),
                _options,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(
                It.IsAny<IReadOnlyList<OcrResult>>(),
                It.IsAny<CapturedImage>()))
            .Returns(detection);
        _inferenceMock.Setup(x => x.InferActionAsync(
                It.IsAny<string>(),
                It.IsAny<AssistanceDetectionResult>(),
                _options,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);

        await _useCase.ExecuteAsync("answer");

        _autoPasteSecurityMock.Verify(x => x.Validate(It.Is<AutoPasteRequest>(request =>
            request.MaximumContextAge == TimeSpan.FromSeconds(91))), Times.Once);
        _inputMock.Verify(x => x.SimulatePasteAsync(
            "answer",
            100,
            100,
            It.IsAny<CancellationToken>()), Times.Once);
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
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Warning &&
            notification.Code == NotificationCode.NoTextDetected)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OcrFailure_NotifiesErrorWithoutExceptionDetails()
    {
        var correlationId = Guid.NewGuid();
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive OCR diagnostic"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _useCase.ExecuteAsync("private prompt", correlationId: correlationId));

        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.CorrelationId == correlationId &&
            notification.Status == NotificationStatus.Error &&
            notification.Code == NotificationCode.OcrUnavailable)), Times.Once);
        _loggerMock.Verify(
            logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("private prompt", StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_NotifiesTypedInferenceError()
    {
        var ocrResults = new[]
        {
            new OcrResult("A) Uno", new BoundingBox(10, 10, 80, 20), 1, OcrResultLevel.Line)
        };
        var detection = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, null!);
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(It.IsAny<IReadOnlyList<OcrResult>>(), It.IsAny<CapturedImage>()))
            .Returns(detection);
        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), _options, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InferenceResponseException("raw model response"));

        await Assert.ThrowsAsync<InferenceResponseException>(() => _useCase.ExecuteAsync("answer"));

        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Error &&
            notification.Code == NotificationCode.InvalidInferenceResponse)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OllamaUnavailable_NotifiesTypedError()
    {
        var ocrResults = new[]
        {
            new OcrResult("A) Uno", new BoundingBox(10, 10, 80, 20), 1, OcrResultLevel.Line)
        };
        var detection = new AssistanceDetectionResult(AssistanceMode.MultipleChoice, ocrResults, null!);
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(It.IsAny<IReadOnlyList<OcrResult>>(), It.IsAny<CapturedImage>()))
            .Returns(detection);
        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), _options, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InferenceUnavailableException("technical detail", new Exception()));

        await Assert.ThrowsAsync<InferenceUnavailableException>(() => _useCase.ExecuteAsync("answer"));

        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Error &&
            notification.Code == NotificationCode.OllamaUnavailable)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ModelUnavailable_NotifiesActionableError()
    {
        var ocrResults = new[]
        {
            new OcrResult("A) Uno", new BoundingBox(10, 10, 80, 20), 1, OcrResultLevel.Line)
        };
        var detection = new AssistanceDetectionResult(
            AssistanceMode.MultipleChoice,
            ocrResults,
            null!);
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(
                It.IsAny<CapturedImage>(),
                _options,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(
                It.IsAny<IReadOnlyList<OcrResult>>(),
                It.IsAny<CapturedImage>()))
            .Returns(detection);
        _inferenceMock.Setup(x => x.InferActionAsync(
                It.IsAny<string>(),
                It.IsAny<AssistanceDetectionResult>(),
                _options,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InferenceModelUnavailableException("technical detail"));

        await Assert.ThrowsAsync<InferenceModelUnavailableException>(() =>
            _useCase.ExecuteAsync("answer"));

        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Error &&
            notification.Code == NotificationCode.InferenceModelUnavailable)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AutoPasteFailure_NotifiesTypedError()
    {
        var ocrResults = new[]
        {
            new OcrResult("Escribe tu respuesta", new BoundingBox(10, 10, 120, 20), 1, OcrResultLevel.Line)
        };
        var detection = new AssistanceDetectionResult(AssistanceMode.OpenText, ocrResults, null!);
        var output = new TextSuggestionOutput("response", 1, TimeSpan.Zero, "answer", 100, 100, 200, 50);
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(It.IsAny<IReadOnlyList<OcrResult>>(), It.IsAny<CapturedImage>()))
            .Returns(detection);
        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);
        _inputMock.Setup(x => x.SimulatePasteAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("clipboard contents"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync("answer"));

        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Error &&
            notification.Code == NotificationCode.AutoPasteFailed)), Times.Once);
    }

    [Theory]
    [InlineData(AutoPasteCancelledReason.WindowChanged)]
    [InlineData(AutoPasteCancelledReason.ContextExpired)]
    [InlineData(AutoPasteCancelledReason.EmptyResponse)]
    [InlineData(AutoPasteCancelledReason.LowConfidence)]
    public async Task ExecuteAsync_UnsafeAutoPaste_DoesNotPasteAndNotifiesCancellation(
        AutoPasteCancelledReason reason)
    {
        var ocrResults = new[]
        {
            new OcrResult("Escribe tu respuesta", new BoundingBox(10, 10, 120, 20), 1, OcrResultLevel.Line)
        };
        var detection = new AssistanceDetectionResult(AssistanceMode.OpenText, ocrResults, null!);
        var output = new TextSuggestionOutput("response", 0.9, TimeSpan.Zero, "answer", 100, 100, 200, 50);
        _captureMock.Setup(x => x.CaptureActiveWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapturedImage)null!);
        _ocrMock.Setup(x => x.ExtractTextAsync(It.IsAny<CapturedImage>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ocrResults);
        _detectorMock.Setup(x => x.Detect(It.IsAny<IReadOnlyList<OcrResult>>(), It.IsAny<CapturedImage>()))
            .Returns(detection);
        _inferenceMock.Setup(x => x.InferActionAsync(It.IsAny<string>(), It.IsAny<AssistanceDetectionResult>(), _options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);
        _autoPasteSecurityMock.Setup(x => x.Validate(It.IsAny<AutoPasteRequest>()))
            .Returns(AutoPasteValidationResult.Cancelled(reason));

        await _useCase.ExecuteAsync("answer");

        _inputMock.Verify(x => x.SimulatePasteAsync(
            It.IsAny<string>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _notificationMock.Verify(x => x.Notify(It.Is<UserNotification>(notification =>
            notification.Status == NotificationStatus.Cancelled &&
            notification.Code == NotificationCode.AutoPasteCancelled)), Times.Once);
    }
}
