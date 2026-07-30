using System.Threading;
using System.Threading.Tasks;
using Aivsp.Application.Configuration;
using Aivsp.Domain.Entities;

namespace Aivsp.Application.Contracts;

public interface IOverlayService
{
    Task DrawIndicatorAsync(MatchResult match, int durationMs);
    Task DrawTextSuggestionAsync(TextSuggestionOutput suggestion, int durationMs);
    Task DrawTextSuggestionDirectAsync(string text, int x, int y, int durationMs);
    void Clear();
}
