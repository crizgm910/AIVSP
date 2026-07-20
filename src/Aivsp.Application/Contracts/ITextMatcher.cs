using System.Collections.Generic;
using Aivsp.Domain.Entities;
using Aivsp.Application.Configuration;

namespace Aivsp.Application.Contracts;

public interface ITextMatcher
{
    MatchResult? FindBestMatch(InferenceResult inference, IReadOnlyList<OcrResult> candidates, AssistantOptions options);
}
