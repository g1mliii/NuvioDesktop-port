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
        CancellationToken cancellationToken)
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

        var items = await FetchCatalogAsync(addon, type, catalogId, skip, cancellationToken).ConfigureAwait(false);
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
        var candidates = new List<(CatalogService Service, ManagedAddon Addon, AddonCatalog Catalog)>();
        foreach (var addon in addons)
        {
            if (!addon.Enabled || addon.Manifest is null)
            {
                continue;
            }

            var homeCatalog = addon.Manifest.Catalogs.FirstOrDefault(catalog =>
                !catalog.Extra.Any(extra =>
                    extra.IsRequired &&
                    !extra.Name.Equals("skip", StringComparison.OrdinalIgnoreCase) &&
                    !extra.Name.Equals("limit", StringComparison.OrdinalIgnoreCase)));
            if (homeCatalog is null)
            {
                continue;
            }

            candidates.Add((this, addon, homeCatalog));
        }

        var rails = await AddonFanOut
            .WhenAllAsync(
                candidates,
                static (candidate, token) => new ValueTask<CatalogRail?>(
                    candidate.Service.SafeRailAsync(candidate.Addon, candidate.Catalog, token)),
                cancellationToken)
            .ConfigureAwait(false);
        return rails.Where(rail => rail is not null).Select(rail => rail!).ToArray();
    }

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

            return new CatalogRail(
                AddonId: addon.Id,
                AddonName: addon.DisplayName,
                Title: $"{addon.DisplayName} — {catalog.Name}",
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
        CancellationToken cancellationToken)
    {
        var uri = BuildCatalogUri(addon, type, catalogId, skip, query: null);
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

    internal static Uri BuildCatalogUri(ManagedAddon addon, string type, string catalogId, int skip, string? query)
    {
        List<string>? extras = null;
        if (!string.IsNullOrEmpty(query))
        {
            (extras ??= []).Add($"search={Uri.EscapeDataString(query)}");
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
