using Aivsp.Domain.Entities;

namespace Aivsp.Application.Contracts;

public interface IQuestionTypeDetector
{
    AssistanceDetectionResult Detect(System.Collections.Generic.IReadOnlyList<OcrResult> ocrResults, CapturedImage image);
}
