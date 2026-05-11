namespace Nuvio.Player;

public enum PlayerTrackType
{
    Unknown,
    Audio,
    Subtitle,
    Video
}

public sealed record PlayerTrack(
    string Id,
    PlayerTrackType Type,
    string Label,
    string? Language,
    bool IsSelected,
    bool IsDefault,
    bool IsExternal);
