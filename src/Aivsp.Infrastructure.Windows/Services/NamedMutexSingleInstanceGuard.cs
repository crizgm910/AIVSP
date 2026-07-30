using System;
using System.Threading;

namespace Aivsp.Infrastructure.Windows.Services;

/// <summary>
/// Owns a named Windows mutex for the lifetime of the current application instance.
/// </summary>
public sealed class NamedMutexSingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\AIVSP.8B6F45E3-151D-4F76-9EC5-4A7B20B84A9D";

    private readonly object _sync = new();
    private readonly string _mutexName;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private bool _disposed;

    public NamedMutexSingleInstanceGuard(string mutexName = DefaultMutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("Mutex name cannot be empty.", nameof(mutexName));
        }

        _mutexName = mutexName;
    }

    public string MutexName => _mutexName;

    /// <summary>
    /// Returns true only for the process that created and owns the named mutex.
    /// Repeated calls on the same guard return the original result.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_mutex != null)
            {
                return _ownsMutex;
            }

            _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
            _ownsMutex = createdNew;
            return _ownsMutex;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_ownsMutex)
                {
                    _mutex?.ReleaseMutex();
                }
            }
            finally
            {
                _ownsMutex = false;
                _mutex?.Dispose();
                _mutex = null;
                _disposed = true;
            }
        }
    }
}
