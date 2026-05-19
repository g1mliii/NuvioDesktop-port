using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Media;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;
using Nuvio.Core.Net;

namespace Nuvio.Core.Services;

public sealed class MetadataService : IMetadataService
{
    private readonly IAddonRepository _repository;
    private readonly INuvioHttpClient _httpClient;
    private readonly ITmdbClient? _tmdbClient;
    private readonly MetadataCache _cache;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;

    public MetadataService(
        IAddonRepository repository,
        INuvioHttpClient httpClient,
        MetadataCache cache,
        ITmdbClient? tmdbClient = null,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tmdbClient = tmdbClient;
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MediaDetails?> GetDetailsAsync(string type, string id, CancellationToken cancellationToken)
    {
        if (_cache.TryGetDetails(type, id, out var cached))
        {
            return cached;
        }

        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = addons
            .Where(addon => addon.Enabled && addon.Manifest is not null && addon.Supports("meta", type, id))
            .ToArray();

        MediaDetails? best = null;
        if (candidates.Length > 0)
        {
            var requests = candidates
                .Select(addon => (Service: this, Addon: addon, Type: type, Id: id))
                .ToArray();
            var results = await AddonFanOut
                .WhenAllAsync(
                    requests,
                    static (request, token) => new ValueTask<MediaDetails?>(
                        request.Service.SafeFetchAsync(request.Addon, request.Type, request.Id, token)),
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var details in results)
            {
                if (details is null)
                {
                    continue;
                }

                if (IsComplete(details))
                {
                    best = details;
                    break;
                }

                best ??= details;
            }
        }

        if ((best is null || !IsComplete(best)) && _tmdbClient is { IsConfigured: true })
        {
            var fallback = await _tmdbClient.TryGetDetailsAsync(type, id, cancellationToken).ConfigureAwait(false);
            best = MergePreferringAddon(best, fallback);
        }

        if (best is not null)
        {
            _cache.SetDetails(type, id, best);
        }

        return best;
    }

    private async Task<MediaDetails?> SafeFetchAsync(
        ManagedAddon addon,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildMetaUri(addon, type, id);
            var request = new NuvioHttpRequest(
                Uri: uri,
                MaxBytes: AddonFetchPolicy.Default.MaxCatalogBytes,
                Timeout: AddonFetchPolicy.Default.Timeout,
                ResourceKind: "meta",
                AddonId: addon.Id);

            var response = await _httpClient.GetStringAsync(request, cancellationToken).ConfigureAwait(false);
            return MediaPayloadParser.ParseMediaDetails(response.Body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _diagnostics.RecordAddonFailure(addon, "meta", ex, _timeProvider);
            return null;
        }
    }

    private static bool IsComplete(MediaDetails details) =>
        !string.IsNullOrWhiteSpace(details.Name) &&
        details.PosterUrl is not null &&
        !string.IsNullOrWhiteSpace(details.ReleaseInfo);

    private static MediaDetails? MergePreferringAddon(MediaDetails? addonResult, MediaDetails? fallback)
    {
        if (addonResult is null)
        {
            return fallback;
        }

        if (fallback is null)
        {
            return addonResult;
        }

        return addonResult with
        {
            PosterUrl = addonResult.PosterUrl ?? fallback.PosterUrl,
            BackgroundUrl = addonResult.BackgroundUrl ?? fallback.BackgroundUrl,
            LogoUrl = addonResult.LogoUrl ?? fallback.LogoUrl,
            Description = string.IsNullOrWhiteSpace(addonResult.Description) ? fallback.Description : addonResult.Description,
            ReleaseInfo = string.IsNullOrWhiteSpace(addonResult.ReleaseInfo) ? fallback.ReleaseInfo : addonResult.ReleaseInfo,
            Runtime = string.IsNullOrWhiteSpace(addonResult.Runtime) ? fallback.Runtime : addonResult.Runtime,
            Genres = addonResult.Genres.Count > 0 ? addonResult.Genres : fallback.Genres,
            ExternalRatings = addonResult.ExternalRatings.Count > 0 ? addonResult.ExternalRatings : fallback.ExternalRatings,
            Cast = addonResult.Cast.Count > 0 ? addonResult.Cast : fallback.Cast,
            ProductionCompanies = addonResult.ProductionCompanies.Count > 0 ? addonResult.ProductionCompanies : fallback.ProductionCompanies,
            Trailers = addonResult.Trailers.Count > 0 ? addonResult.Trailers : fallback.Trailers,
            Links = addonResult.Links.Count > 0 ? addonResult.Links : fallback.Links,
            Videos = addonResult.Videos.Count > 0 ? addonResult.Videos : fallback.Videos
        };
    }

    internal static Uri BuildMetaUri(ManagedAddon addon, string type, string id) =>
        AddonEndpoints.Build(addon, "meta", type, id);
}
