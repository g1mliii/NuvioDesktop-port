namespace Nuvio.Core.Models;

public sealed record WatchProgress(
    string MediaId,
    string? EpisodeId,
    TimeSpan Position,
    TimeSpan Duration,
    double Percent,
    DateTimeOffset UpdatedAt,
    // Display metadata captured at playback time so Continue Watching cards render without a
    // render-time metadata lookup. Mirrors upstream WatchProgressEntry (title/poster/background/
    // parentMetaType). Nullable with defaults so existing positional constructions keep compiling;
    // old rows read these back as null and fall back to the title initial.
    string? MediaType = null,
    string? Title = null,
    Uri? PosterUrl = null,
    Uri? BackgroundUrl = null);
