namespace Nuvio.Player;

public abstract record PlayerEvent(DateTimeOffset ObservedAt)
{
    public sealed record AvailabilityChanged(bool IsAvailable, string? Version, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlaybackPositionChanged(TimeSpan Position, TimeSpan? Duration, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlaybackStateChanged(bool IsPlaying, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlayerError(string Message, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);
}
