namespace Nuvio.Core.Models;

public sealed record PlaybackSnapshot(
    bool IsLoading = true,
    bool IsPlaying = false,
    bool IsEnded = false,
    TimeSpan Duration = default,
    TimeSpan Position = default,
    TimeSpan BufferedPosition = default,
    float PlaybackSpeed = 1f);
