using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Media;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly IAddonRepository _repository;
    private readonly INuvioHttpClient _httpClient;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;

    public CatalogService(
        IAddonRepository repository,
        INuvioHttpClient httpClient,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CatalogPage> BrowseAsync(
        string addonId,
        string type,
        string catalogId,
        int skip,
        CancellationToken cancellationToken,
        string? genre = null)
    {
        if (skip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skip));
        }

        var addon = await _repository.GetAsync(addonId, cancellationToken).ConfigureAwait(false)
            ?? throw new NuvioValidationException($"Addon '{addonId}' is not installed.");
        if (!addon.Enabled)
        {
            return new CatalogPage(addon.Id, addon.DisplayName, type, catalogId, skip, Array.Empty<CatalogItem>());
        }

        if (addon.Manifest is null)
        {
            throw new NuvioValidationException($"Addon '{addonId}' has no manifest.");
        }

        var items = await FetchCatalogAsync(addon, type, catalogId, skip, cancellationToken, genre).ConfigureAwait(false);
        return new CatalogPage(addon.Id, addon.DisplayName, type, catalogId, skip, items);
    }

    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(
        string query,
        IReadOnlyList<string>? types,
        CancellationToken cancellationToken)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Array.Empty<CatalogItem>();
        }

        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var searchableCatalogs = new List<(CatalogService Service, ManagedAddon Addon, AddonCatalog Catalog, string Query)>();
        foreach (var addon in addons)
        {
            if (!addon.Enabled || addon.Manifest is null)
            {
                continue;
            }

            foreach (var catalog in addon.Manifest.Catalogs)
            {
                if (types is { Count: > 0 } &&
                    !types.Any(type => type.Equals(catalog.Type, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (catalog.Extra.Any(extra => extra.Name.Equals("search", StringComparison.OrdinalIgnoreCase)))
                {
                    searchableCatalogs.Add((this, addon, catalog, trimmed));
                }
            }
        }

        if (searchableCatalogs.Count == 0)
        {
            return Array.Empty<CatalogItem>();
        }

        var results = await AddonFanOut
            .WhenAllAsync(
                searchableCatalogs,
                static (pair, token) => new ValueTask<IReadOnlyList<CatalogItem>>(
                    pair.Service.SafeSearchAsync(pair.Addon, pair.Catalog, pair.Query, token)),
                cancellationToken)
            .ConfigureAwait(false);

        var merged = new List<CatalogItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var addonResults in results)
        {
            foreach (var item in addonResults)
            {
                var key = StableKey(item);
                if (seen.Add(key))
                {
                    merged.Add(item);
                }
            }
        }

        return merged;
    }

    public async Task<IReadOnlyList<CatalogRail>> HomeRailsAsync(CancellationToken cancellationToken)
    {
        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = BuildHomeCandidates(addons)
            .Select(candidate => (Service: this, candidate.Addon, candidate.Catalog))
            .ToArray();

        var rails = await AddonFanOut
            .WhenAllAsync(
                candidates,
                static (candidate, token) => new ValueTask<CatalogRail?>(
                    candidate.Service.SafeRailAsync(candidate.Addon, candidate.Catalog, token)),
                cancellationToken)
            .ConfigureAwait(false);
        return rails.Where(rail => rail is not null).Select(rail => rail!).ToArray();
    }

    public async IAsyncEnumerable<CatalogRail> StreamHomeRailsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = BuildHomeCandidates(addons);

        // Fetch in bounded batches so Home can paint incrementally (progressive publish, parity with
        // upstream HomeRepository.refresh) instead of awaiting every addon before showing anything.
        foreach (var batch in candidates.Chunk(HomeRailDefaults.CatalogFetchBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fetches = batch
                .Select(candidate => SafeRailAsync(candidate.Addon, candidate.Catalog, cancellationToken))
                .ToArray();
            var rails = await Task.WhenAll(fetches).ConfigureAwait(false);
            foreach (var rail in rails)
            {
                if (rail is not null)
                {
                    yield return rail;
                }
            }
        }
    }

    private static List<(ManagedAddon Addon, AddonCatalog Catalog)> BuildHomeCandidates(
        IReadOnlyList<ManagedAddon> addons)
    {
        var candidates = new List<(ManagedAddon Addon, AddonCatalog Catalog)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addon in addons)
        {
            if (!addon.Enabled || addon.Manifest is null)
            {
                continue;
            }

            // One rail per eligible catalog (not just the first): a multi-catalog manifest like
            // Cinemeta should surface Popular, Top, etc. as separate rails. Dedupe by manifest:type:catalog.
            foreach (var catalog in addon.Manifest.Catalogs)
            {
                if (!IsHomeEligible(catalog))
                {
                    continue;
                }

                if (!seen.Add($"{addon.Id}:{catalog.Type}:{catalog.Id}"))
                {
                    continue;
                }

                candidates.Add((addon, catalog));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Home-eligible catalogs are those needing no required extra other than the desktop-exempted
    /// paging hints (skip/limit). This mirrors LiveCatalogDataSource.ResolveBrowseSelectionAsync so
    /// Home and Catalog browse agree. (Upstream is strict <c>.none { isRequired }</c>; the skip/limit
    /// exemption is a deliberate, more-permissive desktop choice — see phase-1-behavior-mapping.)
    /// </summary>
    public static bool IsHomeEligible(AddonCatalog catalog) =>
        !catalog.Extra.Any(extra =>
            extra.IsRequired &&
            !extra.Name.Equals("skip", StringComparison.OrdinalIgnoreCase) &&
            !extra.Name.Equals("limit", StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<CatalogItem>> SafeSearchAsync(
        ManagedAddon addon,
        AddonCatalog catalog,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = BuildCatalogUri(addon, catalog.Type, catalog.Id, skip: 0, query);
            return await FetchAndParseAsync(addon, uri, "search", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _diagnostics.RecordAddonFailure(addon, "search", ex, _timeProvider);
            return Array.Empty<CatalogItem>();
        }
    }

    private async Task<CatalogRail?> SafeRailAsync(
        ManagedAddon addon,
        AddonCatalog catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await FetchCatalogAsync(addon, catalog.Type, catalog.Id, skip: 0, cancellationToken).ConfigureAwait(false);
            if (items.Count == 0)
            {
                return null;
            }

            if (items.Count > HomeRailDefaults.CatalogPreviewFetchLimit)
            {
                items = items.Take(HomeRailDefaults.CatalogPreviewFetchLimit).ToArray();
            }

            return new CatalogRail(
                AddonId: addon.Id,
                AddonName: addon.DisplayName,
                Title: $"{catalog.Name} — {MediaTypeLabel.ForType(catalog.Type)}",
                Type: catalog.Type,
                CatalogId: catalog.Id,
                Items: items);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _diagnostics.RecordAddonFailure(addon, "catalog", ex, _timeProvider);
            return null;
        }
    }

    private async Task<IReadOnlyList<CatalogItem>> FetchCatalogAsync(
        ManagedAddon addon,
        string type,
        string catalogId,
        int skip,
        CancellationToken cancellationToken,
        string? genre = null)
    {
        var uri = BuildCatalogUri(addon, type, catalogId, skip, query: null, genre);
        return await FetchAndParseAsync(addon, uri, "catalog", cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CatalogItem>> FetchAndParseAsync(
        ManagedAddon addon,
        Uri uri,
        string resourceKind,
        CancellationToken cancellationToken)
    {
        var request = new NuvioHttpRequest(
            Uri: uri,
            MaxBytes: AddonFetchPolicy.Default.MaxCatalogBytes,
            Timeout: AddonFetchPolicy.Default.Timeout,
            ResourceKind: resourceKind,
            AddonId: addon.Id);

        var response = await _httpClient.GetStringAsync(request, cancellationToken).ConfigureAwait(false);
        return MediaPayloadParser.ParseCatalogItems(response.Body);
    }

    internal static Uri BuildCatalogUri(
        ManagedAddon addon,
        string type,
        string catalogId,
        int skip,
        string? query,
        string? genre = null)
    {
        List<string>? extras = null;
        if (!string.IsNullOrEmpty(query))
        {
            (extras ??= []).Add($"search={Uri.EscapeDataString(query)}");
        }

        if (!string.IsNullOrEmpty(genre))
        {
            (extras ??= []).Add($"genre={Uri.EscapeDataString(genre)}");
        }

        if (skip > 0)
        {
            (extras ??= []).Add($"skip={skip}");
        }

        return AddonEndpoints.Build(addon, "catalog", type, catalogId, extras);
    }

    internal static string StableKey(CatalogItem item) =>
        $"{item.Type}:{item.Id}";
}
