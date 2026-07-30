using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aivsp.Presentation.Wpf.Services;

public sealed class ActiveRequestCoordinator : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _activeCancellation;
    private Task _activeTask = Task.CompletedTask;
    private bool _stopping;
    private bool _disposed;

    public bool TryStart(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        CancellationTokenSource? previousCancellation;
        CancellationTokenSource currentCancellation;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_stopping)
            {
                return false;
            }

            previousCancellation = _activeCancellation;
            currentCancellation = new CancellationTokenSource();
            _activeCancellation = currentCancellation;
            _activeTask = Task.Run(
                () => RunAsync(operation, currentCancellation));
        }

        previousCancellation?.Cancel();
        return true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? activeCancellation;
        Task activeTask;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _stopping = true;
            activeCancellation = _activeCancellation;
            activeTask = _activeTask;
        }

        activeCancellation?.Cancel();
        await activeTask.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        CancellationTokenSource? activeCancellation;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            activeCancellation = _activeCancellation;
            _activeCancellation = null;
        }

        activeCancellation?.Cancel();
    }

    private async Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await operation(cancellation.Token);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }
}
