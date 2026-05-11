namespace Nuvio.Player;

public abstract record PlayerEvent(DateTimeOffset ObservedAt)
{
    public sealed record AvailabilityChanged(bool IsAvailable, string? Version, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlaybackPositionChanged(TimeSpan Position, TimeSpan? Duration, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlaybackStateChanged(bool IsPlaying, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record FileLoaded(DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlaybackEnded(string? Reason, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record BufferingStateChanged(bool IsBuffering, string? Summary, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record TrackListChanged(IReadOnlyList<PlayerTrack> Tracks, string? SelectedAudioTrackId, string? SelectedSubtitleTrackId, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlayerLog(string Message, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);

    public sealed record PlayerError(string Message, DateTimeOffset ObservedAt)
        : PlayerEvent(ObservedAt);
}
