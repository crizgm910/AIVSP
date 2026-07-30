using System.Collections.Generic;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;

namespace Aivsp.Application.Contracts;

public interface ITextMatcher
{
    MatchResult? FindBestMatch(InferenceResult inference, IReadOnlyList<OcrResult> candidates, AssistantOptions options);
}
