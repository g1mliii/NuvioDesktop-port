using Nuvio.Core.Addons;
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
        return rails.Select(rail => new DesktopHomeRail(rail.Title, rail.Items)).ToArray();
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
            cancellationToken).ConfigureAwait(false);

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

            var catalog = addon.Manifest.Catalogs.FirstOrDefault(item =>
                !item.Extra.Any(extra => extra.IsRequired &&
                    !extra.Name.Equals("skip", StringComparison.OrdinalIgnoreCase) &&
                    !extra.Name.Equals("limit", StringComparison.OrdinalIgnoreCase)));
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
