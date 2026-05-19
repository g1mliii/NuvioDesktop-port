using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Streams;

namespace Nuvio.Core.Services;

public sealed class StreamResolver : IStreamResolver
{
    private readonly IAddonRepository _repository;
    private readonly INuvioHttpClient _httpClient;
    private readonly ISubtitleService _subtitleService;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;

    public StreamResolver(
        IAddonRepository repository,
        INuvioHttpClient httpClient,
        ISubtitleService subtitleService,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _subtitleService = subtitleService ?? throw new ArgumentNullException(nameof(subtitleService));
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ResolvedStreamGroup>> ResolveAsync(
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = addons
            .Where(addon => addon.Enabled && addon.Manifest is not null && addon.Supports("stream", type, id))
            .ToArray();

        if (candidates.Length == 0)
        {
            return Array.Empty<ResolvedStreamGroup>();
        }

        var subtitleTask = _subtitleService.FetchAsync(type, id, cancellationToken);
        var streamRequests = candidates
            .Select(addon => (Resolver: this, Addon: addon, Type: type, Id: id))
            .ToArray();
        var groups = await AddonFanOut
            .WhenAllAsync(
                streamRequests,
                static (request, token) => new ValueTask<ResolvedStreamGroup>(
                    request.Resolver.FetchGroupAsync(request.Addon, request.Type, request.Id, token)),
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SubtitleTrack> sharedSubtitles;
        try
        {
            sharedSubtitles = await subtitleTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            sharedSubtitles = Array.Empty<SubtitleTrack>();
        }

        if (sharedSubtitles.Count == 0)
        {
            return groups;
        }

        return groups.Select(group => AttachSubtitles(group, sharedSubtitles)).ToArray();
    }

    private async Task<ResolvedStreamGroup> FetchGroupAsync(
        ManagedAddon addon,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildStreamUri(addon, type, id);
            var request = new NuvioHttpRequest(
                Uri: uri,
                MaxBytes: AddonFetchPolicy.Default.MaxStreamBytes,
                Timeout: AddonFetchPolicy.Default.Timeout,
                ResourceKind: "stream",
                AddonId: addon.Id);

            var response = await _httpClient.GetStringAsync(request, cancellationToken).ConfigureAwait(false);
            var parsed = StreamPayloadParser.Parse(response.Body, addon.DisplayName, addon.Id);

            var sources = new List<StreamSource>();
            var filtered = 0;
            var streamIndex = 0;
            foreach (var stream in parsed)
            {
                if (!stream.HasDirectPlaybackSource)
                {
                    filtered++;
                    continue;
                }

                var sourceId = $"{addon.Id}:{id}:{++streamIndex}";
                sources.Add(StreamSourceMapper.ToStreamSource(stream, sourceId));
            }

            if (filtered > 0)
            {
                _diagnostics?.Record(new NetworkDiagnosticEvent(
                    Timestamp: _timeProvider.GetUtcNow(),
                    Kind: NetworkEventKind.Skipped,
                    Host: addon.ManifestUrl.Host,
                    StatusCode: null,
                    DurationMs: null,
                    AddonId: addon.Id,
                    ResourceKind: "stream",
                    Message: $"{addon.Id} filtered {filtered} non-direct streams"));
            }

            return new ResolvedStreamGroup(
                AddonId: addon.Id,
                AddonName: addon.DisplayName,
                Streams: sources,
                FilteredCount: filtered,
                Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _diagnostics.RecordAddonFailure(addon, "stream", ex, _timeProvider);
            return new ResolvedStreamGroup(
                AddonId: addon.Id,
                AddonName: addon.DisplayName,
                Streams: Array.Empty<StreamSource>(),
                FilteredCount: 0,
                Error: ex.Message);
        }
    }

    private static ResolvedStreamGroup AttachSubtitles(ResolvedStreamGroup group, IReadOnlyList<SubtitleTrack> shared)
    {
        if (group.Streams.Count == 0)
        {
            return group;
        }

        var augmented = new List<StreamSource>(group.Streams.Count);
        foreach (var source in group.Streams)
        {
            if (source.Subtitles.Count == 0)
            {
                augmented.Add(source with { Subtitles = shared });
                continue;
            }

            var merged = new List<SubtitleTrack>(source.Subtitles);
            var seen = new HashSet<string>(merged.Select(s => s.Url?.ToString() ?? s.Id), StringComparer.Ordinal);
            foreach (var subtitle in shared)
            {
                var key = subtitle.Url?.ToString() ?? subtitle.Id;
                if (seen.Add(key))
                {
                    merged.Add(subtitle);
                }
            }

            augmented.Add(source with { Subtitles = merged });
        }

        return group with { Streams = augmented };
    }

    internal static Uri BuildStreamUri(ManagedAddon addon, string type, string id) =>
        AddonEndpoints.Build(addon, "stream", type, id);
}
