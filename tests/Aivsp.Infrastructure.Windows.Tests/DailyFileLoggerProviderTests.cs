using System;
using System.IO;
using Aivsp.Infrastructure.Windows.Logging;
using Microsoft.Extensions.Logging;

namespace Aivsp.Infrastructure.Windows.Tests;

public class DailyFileLoggerProviderTests
{
    [Fact]
    public void Logger_PersistsCorrelationScopeMessageAndException()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIVSP.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new DailyFileLoggerProvider(directory);
            var logger = provider.CreateLogger("AIVSP.Test");
            using (logger.BeginScope("CorrelationId: {CorrelationId}", "25892cca"))
            {
                logger.LogError(new InvalidOperationException("technical failure"), "AutoPaste failed");
            }

            var logFile = Assert.Single(Directory.GetFiles(directory, "aivsp-*.log"));
            var contents = File.ReadAllText(logFile);
            Assert.Contains("25892cca", contents, StringComparison.Ordinal);
            Assert.Contains("AutoPaste failed", contents, StringComparison.Ordinal);
            Assert.Contains("technical failure", contents, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
