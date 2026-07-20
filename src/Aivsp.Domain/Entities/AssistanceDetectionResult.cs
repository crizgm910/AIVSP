using System.Collections.Generic;
using Aivsp.Domain.Enums;

namespace Aivsp.Domain.Entities;

public record AssistanceDetectionResult(
    AssistanceMode Mode,
    IReadOnlyList<OcrResult> OcrResults,
    CapturedImage Image,
    int? CursorX = null,
    int? CursorY = null
);
