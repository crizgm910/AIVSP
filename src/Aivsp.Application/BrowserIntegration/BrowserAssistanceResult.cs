using System;
using System.Collections.Generic;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Enums;

namespace Aivsp.Application.BrowserIntegration;

public sealed record BrowserAssistanceResult(
    Guid CorrelationId,
    string ContextId,
    AssistanceMode Mode,
    string Answer,
    string? OptionKey,
    double Confidence,
    long DurationMilliseconds,
    InferenceProvider Provider,
    OverlayProfile OverlayProfile,
    IReadOnlyList<string>? OptionKeys = null);
