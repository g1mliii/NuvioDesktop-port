using Nuvio.Core.Net;

namespace Nuvio.Core.Tests.Net;

public sealed class PerHostThrottlerTests
{
    [Fact]
    public async Task AcquireAsync_CapsConcurrencyPerHost()
    {
        var throttler = new PerHostThrottler(maxConcurrentPerHost: 4);
        var releasers = new List<IDisposable>();

        for (var i = 0; i < 4; i++)
        {
            releasers.Add(await throttler.AcquireAsync("addons.example.test", CancellationToken.None));
        }

        Assert.Equal(4, throttler.InFlight("addons.example.test"));

        var blocked = throttler.AcquireAsync("addons.example.test", CancellationToken.None);
        Assert.False(blocked.IsCompleted);

        releasers[0].Dispose();
        var newSlot = await blocked.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(newSlot);
        newSlot.Dispose();

        foreach (var releaser in releasers.Skip(1))
        {
            releaser.Dispose();
        }

        Assert.Equal(0, throttler.InFlight("addons.example.test"));
    }

    [Fact]
    public async Task AcquireAsync_DifferentHostsDoNotShareCapacity()
    {
        var throttler = new PerHostThrottler(maxConcurrentPerHost: 2);
        using var alphaA = await throttler.AcquireAsync("alpha.test", CancellationToken.None);
        using var alphaB = await throttler.AcquireAsync("alpha.test", CancellationToken.None);

        using var betaA = await throttler.AcquireAsync("beta.test", CancellationToken.None);

        Assert.Equal(2, throttler.InFlight("alpha.test"));
        Assert.Equal(1, throttler.InFlight("beta.test"));
    }

    [Fact]
    public async Task AcquireAsync_RespectsCancellation()
    {
        var throttler = new PerHostThrottler(maxConcurrentPerHost: 1);
        using var first = await throttler.AcquireAsync("alpha.test", CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            throttler.AcquireAsync("alpha.test", cts.Token));
    }
}
