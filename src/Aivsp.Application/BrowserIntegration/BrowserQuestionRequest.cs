using System.Collections.Generic;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.BrowserIntegration;

public sealed record BrowserQuestionRequest(
    string ContextId,
    AssistanceMode Mode,
    string Question,
    IReadOnlyList<BrowserOptionInput> Options,
    string? InputPlaceholder,
    IReadOnlyList<BrowserImageInput>? Images = null,
    bool MultipleAnswers = false);

public sealed record BrowserImageInput(string MimeType, string Data);

public sealed record BrowserInferenceAnswer(string Answer, IReadOnlyList<string> OptionKeys, double Confidence);
