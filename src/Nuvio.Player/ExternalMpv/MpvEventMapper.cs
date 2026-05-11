using System.Text.Json;

namespace Nuvio.Player.ExternalMpv;

public sealed class MpvEventMapper
{
    private TimeSpan _position;
    private TimeSpan? _duration;
    private IReadOnlyList<PlayerTrack> _tracks = [];
    private string? _selectedAudioTrackId;
    private string? _selectedSubtitleTrackId;

    public IReadOnlyList<PlayerEvent> Map(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventElement))
        {
            return [];
        }

        var observedAt = DateTimeOffset.UtcNow;
        var eventName = eventElement.GetString();

        return eventName switch
        {
            "property-change" => MapPropertyChange(root, observedAt),
            "file-loaded" => [new PlayerEvent.FileLoaded(observedAt)],
            "end-file" => [new PlayerEvent.PlaybackEnded(ReadString(root, "reason"), observedAt)],
            "log-message" => MapLogMessage(root, observedAt),
            "shutdown" => [new PlayerEvent.PlaybackEnded("shutdown", observedAt)],
            _ => []
        };
    }

    private IReadOnlyList<PlayerEvent> MapPropertyChange(JsonElement root, DateTimeOffset observedAt)
    {
        var name = ReadString(root, "name");
        if (name is null || !root.TryGetProperty("data", out var data))
        {
            return [];
        }

        switch (name)
        {
            case "pause":
                return data.ValueKind == JsonValueKind.True || data.ValueKind == JsonValueKind.False
                    ? [new PlayerEvent.PlaybackStateChanged(!data.GetBoolean(), observedAt)]
                    : [];
            case "time-pos":
                if (TryReadSeconds(data, out var position))
                {
                    _position = position;
                    return [new PlayerEvent.PlaybackPositionChanged(_position, _duration, observedAt)];
                }

                return [];
            case "duration":
                if (TryReadSeconds(data, out var duration))
                {
                    _duration = duration;
                    return [new PlayerEvent.PlaybackPositionChanged(_position, _duration, observedAt)];
                }

                return [];
            case "demuxer-cache-state":
                return [new PlayerEvent.BufferingStateChanged(IsBuffering(data), Summarize(data), observedAt)];
            case "track-list":
                _tracks = ReadTracks(data);
                RefreshSelectedTracksFromList();
                return [new PlayerEvent.TrackListChanged(_tracks, _selectedAudioTrackId, _selectedSubtitleTrackId, observedAt)];
            case "aid":
                _selectedAudioTrackId = TrackId(data);
                return [new PlayerEvent.TrackListChanged(_tracks, _selectedAudioTrackId, _selectedSubtitleTrackId, observedAt)];
            case "sid":
                _selectedSubtitleTrackId = TrackId(data);
                return [new PlayerEvent.TrackListChanged(_tracks, _selectedAudioTrackId, _selectedSubtitleTrackId, observedAt)];
            default:
                return [];
        }
    }

    private static IReadOnlyList<PlayerEvent> MapLogMessage(JsonElement root, DateTimeOffset observedAt)
    {
        var message = MpvCommandLogRedactor.Redact(ReadString(root, "text") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        var level = ReadString(root, "level");
        return level is "error" or "fatal"
            ? [new PlayerEvent.PlayerError(message, observedAt)]
            : [new PlayerEvent.PlayerLog(message, observedAt)];
    }

    private static IReadOnlyList<PlayerTrack> ReadTracks(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tracks = new List<PlayerTrack>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadNumberOrString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var type = ReadString(item, "type") switch
            {
                "audio" => PlayerTrackType.Audio,
                "sub" => PlayerTrackType.Subtitle,
                "video" => PlayerTrackType.Video,
                _ => PlayerTrackType.Unknown
            };

            var title = ReadString(item, "title");
            var language = ReadString(item, "lang");
            var label = title ?? language ?? $"{type} {id}";

            tracks.Add(new PlayerTrack(
                id,
                type,
                label,
                language,
                ReadBool(item, "selected"),
                ReadBool(item, "default"),
                ReadBool(item, "external")));
        }

        return tracks;
    }

    private void RefreshSelectedTracksFromList()
    {
        _selectedAudioTrackId = _tracks.FirstOrDefault(track => track.Type == PlayerTrackType.Audio && track.IsSelected)?.Id ?? _selectedAudioTrackId;
        _selectedSubtitleTrackId = _tracks.FirstOrDefault(track => track.Type == PlayerTrackType.Subtitle && track.IsSelected)?.Id ?? _selectedSubtitleTrackId;
    }

    private static bool TryReadSeconds(JsonElement data, out TimeSpan value)
    {
        if (data.ValueKind == JsonValueKind.Number && data.TryGetDouble(out var seconds))
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        value = TimeSpan.Zero;
        return false;
    }

    private static bool IsBuffering(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (data.TryGetProperty("underrun", out var underrun) && underrun.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return underrun.GetBoolean();
        }

        if (data.TryGetProperty("eof", out var eof) && eof.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return false;
        }

        return false;
    }

    private static string? TrackId(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.False || data.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (data.ValueKind == JsonValueKind.Number)
        {
            return data.GetInt32().ToString();
        }

        var value = data.GetString();
        return value is "no" or "auto" ? null : value;
    }

    private static string Summarize(JsonElement data) =>
        MpvCommandLogRedactor.Redact(data.GetRawText());

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNumberOrString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32().ToString(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }

    private static bool ReadBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
}
