namespace Nuvio.Core.Models;

public sealed record WatchProgress(
    string MediaId,
    string? EpisodeId,
    TimeSpan Position,
    TimeSpan Duration,
    double Percent,
    DateTimeOffset UpdatedAt);
