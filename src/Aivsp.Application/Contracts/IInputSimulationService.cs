using System.Threading;
using System.Threading.Tasks;

namespace Aivsp.Application.Contracts;

public interface IInputSimulationService
{
    /// <summary>
    /// Focuses the target point, copies the provided text and presses Ctrl+V.
    /// </summary>
    /// <param name="text">The text to paste.</param>
    Task SimulatePasteAsync(string text, double targetX, double targetY, CancellationToken cancellationToken = default);
}
