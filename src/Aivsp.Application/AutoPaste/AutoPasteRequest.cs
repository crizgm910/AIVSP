using System;
using Aivsp.Domain.ValueObjects;

namespace Aivsp.Application.AutoPaste;

public sealed record AutoPasteRequest(
    string? Response,
    double Confidence,
    CaptureContext CaptureContext,
    TimeSpan MaximumContextAge,
    double MinimumConfidence);
