namespace Nuvio.Core.Models;

public sealed record StreamSource(
    string Id,
    Uri Url,
    string? Title,
    string? QualityLabel,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<SubtitleTrack> Subtitles,
    bool IsUserProvided);
