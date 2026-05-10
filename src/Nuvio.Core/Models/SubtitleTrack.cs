namespace Nuvio.Core.Models;

public sealed record SubtitleTrack(
    string Id,
    string Label,
    Uri? Url,
    string? Language,
    bool IsDefault);
