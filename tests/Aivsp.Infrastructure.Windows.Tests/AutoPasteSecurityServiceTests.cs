using System;
using Aivsp.Application.AutoPaste;
using Aivsp.Application.Contracts;
using Aivsp.Domain.ValueObjects;
using Aivsp.Infrastructure.Windows.Services;

namespace Aivsp.Infrastructure.Windows.Tests;

public class AutoPasteSecurityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly ActiveWindowIdentity ExpectedWindow = new(1001, 42, 2002);

    [Fact]
    public void Validate_CancelsWhenWindowChanges()
    {
        var service = CreateService(new ActiveWindowIdentity(9999, 77, 8888));

        var result = service.Validate(CreateRequest());

        Assert.False(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.WindowChanged, result.CancelledReason);
    }

    [Fact]
    public void Validate_CancelsWhenContextTimesOut()
    {
        var service = CreateService(ExpectedWindow);
        var request = CreateRequest(capturedAtUtc: Now.UtcDateTime.AddSeconds(-16));

        var result = service.Validate(request);

        Assert.False(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.ContextExpired, result.CancelledReason);
    }

    [Fact]
    public void Validate_CancelsWhenFocusChangesInsideTheSameWindow()
    {
        var service = CreateService(ExpectedWindow with { FocusedControlHandle = 3003 });

        var result = service.Validate(CreateRequest());

        Assert.False(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.FocusChanged, result.CancelledReason);
    }

    [Fact]
    public void Validate_CancelsWhenResponseIsEmpty()
    {
        var service = CreateService(ExpectedWindow);

        var result = service.Validate(CreateRequest(response: "   "));

        Assert.False(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.EmptyResponse, result.CancelledReason);
    }

    [Fact]
    public void Validate_CancelsWhenConfidenceIsLow()
    {
        var service = CreateService(ExpectedWindow);

        var result = service.Validate(CreateRequest(confidence: 0.49));

        Assert.False(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.LowConfidence, result.CancelledReason);
    }

    [Fact]
    public void Validate_AllowsPasteWhenContextIsValid()
    {
        var service = CreateService(ExpectedWindow);

        var result = service.Validate(CreateRequest());

        Assert.True(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.None, result.CancelledReason);
    }

    [Fact]
    public void Validate_AllowsPasteAfterLongInferenceWhenWithinConfiguredAge()
    {
        var service = CreateService(ExpectedWindow);
        var request = CreateRequest(
            capturedAtUtc: Now.UtcDateTime.AddSeconds(-30),
            maximumContextAge: TimeSpan.FromSeconds(35));

        var result = service.Validate(request);

        Assert.True(result.CanPaste);
        Assert.Equal(AutoPasteCancelledReason.None, result.CancelledReason);
    }

    private static AutoPasteSecurityService CreateService(ActiveWindowIdentity activeWindow) =>
        new(new StubActiveWindowContextService(activeWindow), new FixedTimeProvider(Now));

    private static AutoPasteRequest CreateRequest(
        string? response = "respuesta",
        double confidence = 0.9,
        DateTime? capturedAtUtc = null,
        TimeSpan? maximumContextAge = null)
    {
        var context = new CaptureContext(
            "browser",
            "quiz",
            capturedAtUtc ?? Now.UtcDateTime.AddSeconds(-2),
            windowHandle: ExpectedWindow.WindowHandle,
            processId: ExpectedWindow.ProcessId,
            focusedControlHandle: ExpectedWindow.FocusedControlHandle);

        return new AutoPasteRequest(
            response,
            confidence,
            context,
            maximumContextAge ?? TimeSpan.FromSeconds(15),
            MinimumConfidence: 0.5);
    }

    private sealed class StubActiveWindowContextService(ActiveWindowIdentity identity)
        : IActiveWindowContextService
    {
        public ActiveWindowIdentity GetActiveWindowIdentity() => identity;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
