namespace Nuvio.Core.Models;

public sealed record StreamItem(
    string? Name,
    string? Description,
    Uri? Url,
    string? InfoHash,
    int? FileIdx,
    Uri? ExternalUrl,
    string ProviderName,
    string ProviderAddonId,
    string? QualityLabel,
    StreamBehaviorHints BehaviorHints,
    IReadOnlyList<SubtitleTrack> Subtitles)
{
    public string StreamLabel => string.IsNullOrWhiteSpace(Name) ? "Stream" : Name;

    public string? StreamSubtitle => Description;

    public Uri? DirectPlaybackUrl => Url ?? ExternalUrl;

    public bool HasDirectPlaybackSource => DirectPlaybackUrl is not null;

    public bool HasTorrentSource => !string.IsNullOrWhiteSpace(InfoHash);

    public bool HasPlayableSource => HasDirectPlaybackSource;
}

public sealed record StreamBehaviorHints(
    string? BingeGroup = null,
    bool NotWebReady = false,
    long? VideoSize = null,
    string? Filename = null,
    StreamProxyHeaders? ProxyHeaders = null);

public sealed record StreamProxyHeaders(
    IReadOnlyDictionary<string, string>? Request,
    IReadOnlyDictionary<string, string>? Response);

public static class StreamSourceMapper
{
    public static StreamSource ToStreamSource(StreamItem stream, string id)
    {
        var url = stream.DirectPlaybackUrl
            ?? throw new ArgumentException("Stream does not have a direct playback URL.", nameof(stream));

        return new StreamSource(
            id,
            url,
            stream.Name,
            stream.QualityLabel,
            stream.BehaviorHints.ProxyHeaders?.Request ?? new Dictionary<string, string>(),
            stream.Subtitles,
            IsUserProvided: false);
    }
}
