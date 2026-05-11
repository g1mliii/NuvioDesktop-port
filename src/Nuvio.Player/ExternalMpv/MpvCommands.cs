using Nuvio.Core.Models;

namespace Nuvio.Player.ExternalMpv;

public static class MpvCommands
{
    public static IReadOnlyList<object?> LoadFile(StreamSource source)
    {
        var command = new List<object?> { "loadfile", source.Url.AbsoluteUri, "replace" };

        if (source.Headers.Count > 0)
        {
            command.Add(new Dictionary<string, object?>
            {
                ["http-header-fields"] = source.Headers.Select(header => $"{header.Key}: {header.Value}").ToArray()
            });
        }

        return command;
    }

    public static IReadOnlyList<object?> Play() => SetProperty("pause", false);

    public static IReadOnlyList<object?> Pause() => SetProperty("pause", true);

    public static IReadOnlyList<object?> Stop() => ["stop"];

    public static IReadOnlyList<object?> Seek(TimeSpan position) =>
        ["seek", position.TotalSeconds, "absolute"];

    public static IReadOnlyList<object?> SetVolume(int volume) =>
        SetProperty("volume", Math.Clamp(volume, 0, 100));

    public static IReadOnlyList<object?> SetFullscreen(bool isFullscreen) =>
        SetProperty("fullscreen", isFullscreen);

    public static IReadOnlyList<object?> SelectAudioTrack(string trackId) =>
        SetProperty("aid", TrackSelectorValue(trackId));

    public static IReadOnlyList<object?> SelectSubtitleTrack(string? trackId) =>
        SetProperty("sid", string.IsNullOrWhiteSpace(trackId) ? "no" : TrackSelectorValue(trackId));

    public static IReadOnlyList<object?> AddSubtitle(SubtitleTrack subtitle) =>
        ["sub-add", subtitle.Url!.AbsoluteUri, "auto", subtitle.Label, subtitle.Language ?? string.Empty];

    public static IReadOnlyList<object?> ObserveProperty(long observerId, string propertyName) =>
        ["observe_property", observerId, propertyName];

    public static IReadOnlyList<object?> RequestLogMessages(string minimumLevel) =>
        ["request_log_messages", minimumLevel];

    public static IReadOnlyList<object?> Quit() => ["quit"];

    private static IReadOnlyList<object?> SetProperty(string name, object? value) =>
        ["set_property", name, value];

    private static object TrackSelectorValue(string trackId) =>
        int.TryParse(trackId, out var numericId) ? numericId : trackId;
}
