using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Aivsp.Infrastructure.Windows.Logging;

public sealed class DailyFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _sync = new();
    private readonly string _logDirectory;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    public DailyFileLoggerProvider(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIVSP",
            "logs");
    }

    public string LogDirectory => _logDirectory;

    public ILogger CreateLogger(string categoryName) =>
        new DailyFileLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }

    private void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(
                _logDirectory,
                $"aivsp-{DateTime.UtcNow:yyyyMMdd}.log");
            var scopes = new StringWriter(CultureInfo.InvariantCulture);
            _scopeProvider.ForEachScope((scope, writer) =>
            {
                if (writer.GetStringBuilder().Length > 0)
                {
                    writer.Write(" => ");
                }
                writer.Write(scope);
            }, scopes);

            using var writer = new StreamWriter(path, append: true);
            writer.Write(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(" [");
            writer.Write(level);
            writer.Write("] ");
            writer.Write(category);
            if (eventId.Id != 0)
            {
                writer.Write(" EventId=");
                writer.Write(eventId.Id);
            }
            if (scopes.GetStringBuilder().Length > 0)
            {
                writer.Write(" Scope=");
                writer.Write(scopes.ToString());
            }
            writer.Write(" - ");
            writer.WriteLine(message.ReplaceLineEndings(" "));
            if (exception != null)
            {
                writer.WriteLine(exception.ToString());
            }
        }
    }

    private sealed class DailyFileLogger(
        DailyFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
