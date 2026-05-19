using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Subtitles;

namespace Nuvio.Core.Services;

public sealed class SubtitleService : ISubtitleService
{
    private readonly IAddonRepository _repository;
    private readonly INuvioHttpClient _httpClient;
    private readonly MetadataCache _cache;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;

    public SubtitleService(
        IAddonRepository repository,
        INuvioHttpClient httpClient,
        MetadataCache cache,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<SubtitleTrack>> FetchAsync(string type, string id, CancellationToken cancellationToken)
    {
        if (_cache.TryGetSubtitles(type, id, out var cached))
        {
            return cached;
        }

        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = addons
            .Where(addon => addon.Enabled && addon.Manifest is not null && addon.Supports("subtitles", type, id))
            .ToArray();

        if (candidates.Length == 0)
        {
            var empty = Array.Empty<SubtitleTrack>();
            _cache.SetSubtitles(type, id, empty);
            return empty;
        }

        var requests = candidates
            .Select(addon => (Service: this, Addon: addon, Type: type, Id: id))
            .ToArray();
        var groups = await AddonFanOut
            .WhenAllAsync(
                requests,
                static (request, token) => new ValueTask<IReadOnlyList<SubtitleTrack>>(
                    request.Service.SafeFetchAsync(request.Addon, request.Type, request.Id, token)),
                cancellationToken)
            .ConfigureAwait(false);

        var merged = new List<SubtitleTrack>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var subtitle in group)
            {
                var key = subtitle.Url?.ToString() ?? subtitle.Id;
                if (seen.Add(key))
                {
                    merged.Add(subtitle);
                }
            }
        }

        _cache.SetSubtitles(type, id, merged);
        return merged;
    }

    private async Task<IReadOnlyList<SubtitleTrack>> SafeFetchAsync(
        ManagedAddon addon,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildSubtitlesUri(addon, type, id);
            var request = new NuvioHttpRequest(
                Uri: uri,
                MaxBytes: SubtitlePayloadParser.MaxPayloadBytes,
                Timeout: AddonFetchPolicy.Default.Timeout,
                ResourceKind: "subtitles",
                AddonId: addon.Id);

            var response = await _httpClient.GetStringAsync(request, cancellationToken).ConfigureAwait(false);
            return SubtitlePayloadParser.Parse(response.Body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _diagnostics.RecordAddonFailure(addon, "subtitles", ex, _timeProvider);
            return Array.Empty<SubtitleTrack>();
        }
    }

    internal static Uri BuildSubtitlesUri(ManagedAddon addon, string type, string id) =>
        AddonEndpoints.Build(addon, "subtitles", type, id);
}
