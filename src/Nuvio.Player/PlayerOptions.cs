namespace Nuvio.Player;

public sealed record PlayerOptions(
    string PreferredEngine,
    int InitialVolume,
    bool HardwareDecodingEnabled)
{
    public IReadOnlyDictionary<string, string> MpvOptions { get; init; } = ExternalMpvQualityDefaults.Options;

    public static PlayerOptions ExternalMpvDefault { get; } = new("external-mpv", 80, true);
}
