namespace Aivsp.Application.Results;

public enum ErrorCode
{
    None,
    CaptureFailed,
    ProcessExcluded,
    OcrUnavailable,
    NoTextDetected,
    InferenceUnavailable,
    InferenceTimeout,
    LowConfidence,
    OperationCancelled,
    OverlayFailed
}
