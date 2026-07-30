using System;
using Aivsp.Infrastructure.Windows.Services;

namespace Aivsp.Infrastructure.Windows.Tests;

public class NamedMutexSingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_AllowsOnlyOneGuardForTheSameName()
    {
        var mutexName = CreateUniqueMutexName();
        using var first = new NamedMutexSingleInstanceGuard(mutexName);
        using var second = new NamedMutexSingleInstanceGuard(mutexName);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void Dispose_ReleasesMutexForANewGuard()
    {
        var mutexName = CreateUniqueMutexName();

        using (var first = new NamedMutexSingleInstanceGuard(mutexName))
        {
            Assert.True(first.TryAcquire());
        }

        using var next = new NamedMutexSingleInstanceGuard(mutexName);
        Assert.True(next.TryAcquire());
    }

    [Fact]
    public void TryAcquire_IsIdempotentForOwningGuard()
    {
        using var guard = new NamedMutexSingleInstanceGuard(CreateUniqueMutexName());

        Assert.True(guard.TryAcquire());
        Assert.True(guard.TryAcquire());
    }

    [Fact]
    public void TryAcquire_AfterDispose_Throws()
    {
        var guard = new NamedMutexSingleInstanceGuard(CreateUniqueMutexName());
        guard.Dispose();

        Assert.Throws<ObjectDisposedException>(() => guard.TryAcquire());
    }

    private static string CreateUniqueMutexName() =>
        $@"Local\AIVSP.Tests.{Guid.NewGuid():N}";
}
