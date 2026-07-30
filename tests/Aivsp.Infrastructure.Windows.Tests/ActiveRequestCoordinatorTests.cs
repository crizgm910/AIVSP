using Aivsp.Presentation.Wpf.Services;

namespace Aivsp.Infrastructure.Windows.Tests;

public sealed class ActiveRequestCoordinatorTests
{
    [Fact]
    public async Task TryStart_SecondRequestCancelsFirstAndRunsLatest()
    {
        using var coordinator = new ActiveRequestCoordinator();
        var firstCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(coordinator.TryStart(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                firstCancelled.TrySetResult();
            }
        }));

        Assert.True(coordinator.TryStart(_ =>
        {
            secondCompleted.TrySetResult();
            return Task.CompletedTask;
        }));

        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAsync_CancelsAndWaitsForActiveRequestCleanup()
    {
        using var coordinator = new ActiveRequestCoordinator();
        var requestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(coordinator.TryStart(async cancellationToken =>
        {
            requestStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await Task.Yield();
                cleanupFinished.TrySetResult();
            }
        }));

        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.StopAsync(CancellationToken.None);

        Assert.True(cleanupFinished.Task.IsCompletedSuccessfully);
        Assert.False(coordinator.TryStart(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task StopAsync_IsIdempotentWhenNoRequestIsActive()
    {
        using var coordinator = new ActiveRequestCoordinator();

        await coordinator.StopAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);

        Assert.False(coordinator.TryStart(_ => Task.CompletedTask));
    }
}
