using System.Collections.Generic;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.BrowserIntegration;

public sealed record BrowserQuestionRequest(
    string ContextId,
    AssistanceMode Mode,
    string Question,
    IReadOnlyList<BrowserOptionInput> Options,
    string? InputPlaceholder);
