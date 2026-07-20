using System.Threading;
using System.Threading.Tasks;
using Aivsp.Domain.Entities;
using Aivsp.Application.Configuration;

namespace Aivsp.Application.Contracts;

public interface IOverlayService
{
    Task DrawIndicatorAsync(MatchResult match, int durationMs);
    Task DrawTextSuggestionAsync(TextSuggestionOutput suggestion, int durationMs);
    Task DrawTextSuggestionDirectAsync(string text, int x, int y, int durationMs);
    void Clear();
}
