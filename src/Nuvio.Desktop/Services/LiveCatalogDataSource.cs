using System.Runtime.CompilerServices;
using Nuvio.Core.Addons;
using Nuvio.Core.Media;
using Nuvio.Core.Models;
using Nuvio.Core.Services;
using Nuvio.Core.Validation;
using Nuvio.Desktop.Models;

namespace Nuvio.Desktop.Services;

public sealed class LiveCatalogDataSource : ICatalogDataSource
{
    private readonly IAddonRepository _repository;
    private readonly ICatalogService _catalogService;
    private readonly IMetadataService _metadataService;
    private readonly IStreamResolver _streamResolver;
    private string? _activeBrowseAddonId;
    private string? _activeBrowseType;
    private string? _activeBrowseCatalogId;
    private string? _activeGenre;

    public LiveCatalogDataSource(
        IAddonRepository repository,
        ICatalogService catalogService,
        IMetadataService metadataService,
        IStreamResolver streamResolver)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _streamResolver = streamResolver ?? throw new ArgumentNullException(nameof(streamResolver));
    }

    public string ModeLabel => "Live addons";

    public async Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken)
    {
        var rails = await _catalogService.HomeRailsAsync(cancellationToken).ConfigureAwait(false);
        return rails.Select(ToRail).ToArray();
    }

    public async IAsyncEnumerable<DesktopHomeRail> StreamHomeRailsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var rail in _catalogService.StreamHomeRailsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return ToRail(rail);
        }
    }

    public async Task<bool> HasCatalogCapableAddonsAsync(CancellationToken cancellationToken)
    {
        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return addons.Any(addon =>
            addon.Enabled &&
            addon.Manifest is not null &&
            addon.Manifest.Catalogs.Any(CatalogService.IsHomeEligible));
    }

    public async Task<IReadOnlyList<DesktopCatalogChoice>> GetCatalogChoicesAsync(CancellationToken cancellationToken)
    {
        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var choices = new List<DesktopCatalogChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addon in addons)
        {
            if (!addon.Enabled || addon.Manifest is null)
            {
                continue;
            }

            foreach (var catalog in addon.Manifest.Catalogs)
            {
                if (!CatalogService.IsHomeEligible(catalog))
                {
                    continue;
                }

                if (!seen.Add($"{addon.Id}:{catalog.Type}:{catalog.Id}"))
                {
                    continue;
                }

                var genres = catalog.Extra
                    .FirstOrDefault(extra => extra.Name.Equals("genre", StringComparison.OrdinalIgnoreCase))?
                    .Options ?? Array.Empty<string>();
                choices.Add(new DesktopCatalogChoice(
                    AddonId: addon.Id,
                    AddonName: addon.DisplayName,
                    Type: catalog.Type,
                    CatalogId: catalog.Id,
                    DisplayName: $"{catalog.Name} — {MediaTypeLabel.ForType(catalog.Type)}",
                    Genres: genres));
            }
        }

        return choices;
    }

    public void SelectCatalog(DesktopCatalogChoice choice, string? genre)
    {
        ArgumentNullException.ThrowIfNull(choice);
        _activeBrowseAddonId = choice.AddonId;
        _activeBrowseType = choice.Type;
        _activeBrowseCatalogId = choice.CatalogId;
        _activeGenre = string.IsNullOrWhiteSpace(genre) ? null : genre;
    }

    public async Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken)
    {
        var selection = await ResolveBrowseSelectionAsync(cancellationToken).ConfigureAwait(false);
        if (selection is null)
        {
            return new DesktopCatalogPage(Array.Empty<CatalogItem>(), HasMore: false, NextSkip: 0);
        }

        var page = await _catalogService.BrowseAsync(
            selection.Value.AddonId,
            selection.Value.Type,
            selection.Value.CatalogId,
            skip,
            cancellationToken,
            _activeGenre).ConfigureAwait(false);

        var hasMore = page.Items.Count >= ICatalogService.PageSize;
        return new DesktopCatalogPage(page.Items, hasMore, skip + page.Items.Count);
    }

    public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
        _catalogService.SearchAsync(query, types: null, cancellationToken);

    public async Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken)
    {
        var type = mediaType ?? "movie";

        var detailsTask = _metadataService.GetDetailsAsync(type, mediaId, cancellationToken);
        var streamsTask = _streamResolver.ResolveAsync(type, mediaId, cancellationToken);

        await Task.WhenAll(detailsTask, streamsTask).ConfigureAwait(false);

        var details = detailsTask.Result ?? throw new NuvioValidationException(
            $"No addon returned metadata for {type}:{mediaId}.");

        var groups = streamsTask.Result;
        var streamItems = new List<StreamItem>();
        var failedProviders = new List<string>();
        foreach (var group in groups)
        {
            if (group.Error is not null && group.Streams.Count == 0)
            {
                failedProviders.Add(group.AddonName);
            }

            foreach (var source in group.Streams)
            {
                var description = source.QualityLabel ?? source.Title;
                streamItems.Add(new StreamItem(
                    Name: source.Title,
                    Description: description,
                    Url: source.Url,
                    InfoHash: null,
                    FileIdx: null,
                    ExternalUrl: null,
                    ProviderName: group.AddonName,
                    ProviderAddonId: group.AddonId,
                    QualityLabel: source.QualityLabel,
                    BehaviorHints: source.Headers.Count == 0
                        ? new StreamBehaviorHints()
                        : new StreamBehaviorHints(
                            ProxyHeaders: new StreamProxyHeaders(source.Headers, null)),
                    Subtitles: source.Subtitles));
            }
        }

        return new FixtureDetailState(details, streamItems, failedProviders);
    }

    private static DesktopHomeRail ToRail(CatalogRail rail) =>
        new(
            Title: rail.Title,
            Items: rail.Items,
            Key: $"{rail.AddonId}:{rail.Type}:{rail.CatalogId}",
            AddonId: rail.AddonId,
            AddonName: rail.AddonName,
            Type: rail.Type,
            CatalogId: rail.CatalogId);

    private async Task<(string AddonId, string Type, string CatalogId)?> ResolveBrowseSelectionAsync(CancellationToken cancellationToken)
    {
        if (_activeBrowseAddonId is not null && _activeBrowseType is not null && _activeBrowseCatalogId is not null)
        {
            return (_activeBrowseAddonId, _activeBrowseType, _activeBrowseCatalogId);
        }

        var addons = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var addon in addons)
        {
            if (!addon.Enabled || addon.Manifest is null)
            {
                continue;
            }

            var catalog = addon.Manifest.Catalogs.FirstOrDefault(CatalogService.IsHomeEligible);
            if (catalog is null)
            {
                continue;
            }

            _activeBrowseAddonId = addon.Id;
            _activeBrowseType = catalog.Type;
            _activeBrowseCatalogId = catalog.Id;
            return (addon.Id, catalog.Type, catalog.Id);
        }

        return null;
    }
}
