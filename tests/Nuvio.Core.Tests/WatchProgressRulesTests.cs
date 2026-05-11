using Nuvio.Core.Models;
using Nuvio.Core.Progress;

namespace Nuvio.Core.Tests;

public sealed class WatchProgressRulesTests
{
    [Fact]
    public void ShouldStoreProgress_MatchesMobileThreshold()
    {
        Assert.False(WatchProgressRules.ShouldStoreProgress(TimeSpan.FromMilliseconds(999), TimeSpan.FromMinutes(10)));
        Assert.True(WatchProgressRules.ShouldStoreProgress(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Normalize_CompletesAtNinetyPercent()
    {
        var progress = new WatchProgress(
            MediaId: "movie-1",
            EpisodeId: null,
            Position: TimeSpan.FromMinutes(90),
            Duration: TimeSpan.FromMinutes(100),
            Percent: 0,
            UpdatedAt: DateTimeOffset.UnixEpoch);

        var normalized = WatchProgressRules.Normalize(progress);

        Assert.Equal(TimeSpan.FromMinutes(100), normalized.Position);
        Assert.Equal(100, normalized.Percent);
    }

    [Fact]
    public void Normalize_ClampsNegativePositionAndComputesPercent()
    {
        var progress = new WatchProgress(
            MediaId: "movie-1",
            EpisodeId: null,
            Position: TimeSpan.FromMinutes(-1),
            Duration: TimeSpan.FromMinutes(100),
            Percent: 50,
            UpdatedAt: DateTimeOffset.UnixEpoch);

        var normalized = WatchProgressRules.Normalize(progress);

        Assert.Equal(TimeSpan.Zero, normalized.Position);
        Assert.Equal(0, normalized.Percent);
    }
}
