namespace Aivsp.Application.Notifications;

public enum NotificationCode
{
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionCancelled,
    InferenceTimedOut,
    AssistantPaused,
    NoTextDetected,
    QuestionNotRecognized,
    OcrUnavailable,
    OllamaUnavailable,
    InferenceModelUnavailable,
    InferenceProviderUnavailable,
    InvalidInferenceResponse,
    HotkeyUnavailable,
    DesktopCaptureDisabled,
    AutoPasteCancelled,
    AutoPasteFailed,
    BrowserBridgeUnavailable,
    BrowserRequestFailed,
    UnexpectedError
}
