namespace Nuvio.Player;

public sealed record PlayerOptions(
    string PreferredEngine,
    int InitialVolume,
    bool HardwareDecodingEnabled)
{
    public static PlayerOptions ExternalMpvDefault { get; } = new("external-mpv", 80, true);
}
