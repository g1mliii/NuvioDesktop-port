using Nuvio.Core.Models;

namespace Nuvio.Core.Progress;

public static class WatchProgressRules
{
    public const double CompletionThresholdFraction = 0.90;
    public static readonly TimeSpan ProgressStoreThreshold = TimeSpan.FromSeconds(1);

    public static bool ShouldStoreProgress(TimeSpan position, TimeSpan duration) =>
        position >= ProgressStoreThreshold;

    public static bool IsProgressComplete(TimeSpan position, TimeSpan duration, bool isEnded)
    {
        if (isEnded)
        {
            return true;
        }

        if (duration <= TimeSpan.Zero)
        {
            return false;
        }

        return position.TotalMilliseconds / duration.TotalMilliseconds >= CompletionThresholdFraction;
    }

    public static WatchProgress Normalize(WatchProgress progress, bool isEnded = false)
    {
        var position = progress.Position < TimeSpan.Zero ? TimeSpan.Zero : progress.Position;
        var duration = progress.Duration < TimeSpan.Zero ? TimeSpan.Zero : progress.Duration;
        var percent = progress.Percent;

        if (duration > TimeSpan.Zero)
        {
            percent = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100d, 0d, 100d);
        }
        else
        {
            percent = Math.Clamp(percent, 0d, 100d);
        }

        if (IsProgressComplete(position, duration, isEnded))
        {
            position = duration > TimeSpan.Zero ? duration : position;
            percent = 100d;
        }

        return progress with
        {
            Position = position,
            Duration = duration,
            Percent = percent
        };
    }
}
