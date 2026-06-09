using Nuvio.Core.Addons;
using Nuvio.Core.Models;
using Nuvio.Core.Services;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;

namespace Nuvio.Desktop.Tests.ViewModels;

public sealed class LiveCatalogDataSourceTests
{
    [Fact]
    public async Task GetCatalogPageAsync_ReportsHasMoreWhenPageIsFull()
    {
        var repository = new InMemoryAddonRepository();
        await SeedAddonAsync(repository);

        var catalog = new StubCatalogService(BuildPage(ICatalogService.PageSize));
        var metadata = new StubMetadataService();
        var streams = new StubStreamResolver();

        var dataSource = new LiveCatalogDataSource(repository, catalog, metadata, streams);
        var page = await dataSource.GetCatalogPageAsync(0, CancellationToken.None);

        Assert.True(page.HasMore);
        Assert.Equal(ICatalogService.PageSize, page.NextSkip);
    }

    [Fact]
    public async Task CatalogViewModel_ShowsEmptyStateWhenNoAddonsConfigured()
    {
        var repository = new InMemoryAddonRepository();
        var catalog = new StubCatalogService(new List<CatalogItem>());
        var metadata = new StubMetadataService();
        var streams = new StubStreamResolver();
        var dataSource = new LiveCatalogDataSource(repository, catalog, metadata, streams);
        var viewModel = new CatalogPageViewModel(dataSource, _ => Task.CompletedTask);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsEmpty);
        Assert.Equal(0, viewModel.ItemCount);
    }

    [Fact]
    public async Task HomeViewModel_PropagatesErrorFromDataSource()
    {
        var dataSource = new FailingDataSource();
        var viewModel = new HomePageViewModel(dataSource, _ => Task.CompletedTask);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasError);
        Assert.Contains("boom", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CatalogViewModel_DisposeCancelsInFlightLoadMore()
    {
        var dataSource = new BlockingLoadMoreDataSource();
        var viewModel = new CatalogPageViewModel(dataSource, _ => Task.CompletedTask);
        await viewModel.LoadAsync(CancellationToken.None);

        var loadMore = viewModel.LoadMoreCommand.ExecuteAsync(null);
        await dataSource.LoadMoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Dispose();
        await loadMore.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(dataSource.LoadMoreToken.IsCancellationRequested);
    }

    [Fact]
    public async Task DetailsViewModel_ClearsProviderErrorsWhenNextLoadFails()
    {
        var dataSource = new SequencedDetailsDataSource();
        var viewModel = new DetailsPageViewModel(dataSource, (_, _) => Task.CompletedTask);

        await viewModel.LoadAsync("first", "movie", CancellationToken.None);
        Assert.True(viewModel.HasProviderErrors);

        await viewModel.LoadAsync("second", "movie", CancellationToken.None);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.HasProviderErrors);
        Assert.Equal(string.Empty, viewModel.ProviderErrorSummary);
    }

    private static async Task SeedAddonAsync(IAddonRepository repository)
    {
        var manifest = new AddonManifest(
            Id: "addon.one",
            Name: "Alpha",
            Description: string.Empty,
            Version: "1.0.0",
            LogoUrl: null,
            Resources: [new AddonResource("catalog", ["movie"], Array.Empty<string>())],
            Types: ["movie"],
            IdPrefixes: Array.Empty<string>(),
            Catalogs: [new AddonCatalog("movie", "top", "Top", Array.Empty<AddonExtraProperty>())],
            BehaviorHints: new AddonBehaviorHints(),
            TransportUrl: new Uri("https://addons.example.test/manifest.json"));

        await repository.UpsertAsync(new ManagedAddon(
            Id: "addon.one",
            ManifestUrl: new Uri("https://addons.example.test/manifest.json"),
            Manifest: manifest,
            Enabled: true,
            SortOrder: 0,
            LastError: null,
            LastRefreshedAt: DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private static List<CatalogItem> BuildPage(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new CatalogItem($"id-{i}", "movie", $"Movie {i}", null, null, null))
            .ToList();

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyList<CatalogItem> _items;

        public StubCatalogService(IReadOnlyList<CatalogItem> items) => _items = items;

        public Task<CatalogPage> BrowseAsync(string addonId, string type, string catalogId, int skip, CancellationToken cancellationToken, string? genre = null) =>
            Task.FromResult(new CatalogPage(addonId, addonId, type, catalogId, skip, _items));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, IReadOnlyList<string>? types, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

        public Task<IReadOnlyList<CatalogRail>> HomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogRail>>(Array.Empty<CatalogRail>());

        public async IAsyncEnumerable<CatalogRail> StreamHomeRailsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var rail in await HomeRailsAsync(cancellationToken))
            {
                yield return rail;
            }
        }
    }

    private sealed class StubMetadataService : IMetadataService
    {
        public Task<MediaDetails?> GetDetailsAsync(string type, string id, CancellationToken cancellationToken) =>
            Task.FromResult<MediaDetails?>(null);
    }

    private sealed class StubStreamResolver : IStreamResolver
    {
        public Task<IReadOnlyList<ResolvedStreamGroup>> ResolveAsync(string type, string id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ResolvedStreamGroup>>(Array.Empty<ResolvedStreamGroup>());
    }

    private sealed class FailingDataSource : ICatalogDataSource
    {
        public string ModeLabel => "failing";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class BlockingLoadMoreDataSource : ICatalogDataSource
    {
        public TaskCompletionSource LoadMoreStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken LoadMoreToken { get; private set; }

        public string ModeLabel => "blocking";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DesktopHomeRail>>(Array.Empty<DesktopHomeRail>());

        public async Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken)
        {
            if (skip == 0)
            {
                return new DesktopCatalogPage(BuildPage(ICatalogService.PageSize), HasMore: true, NextSkip: ICatalogService.PageSize);
            }

            LoadMoreToken = cancellationToken;
            LoadMoreStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class SequencedDetailsDataSource : ICatalogDataSource
    {
        private int _calls;

        public string ModeLabel => "details";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DesktopHomeRail>>(Array.Empty<DesktopHomeRail>());

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            Task.FromResult(new DesktopCatalogPage(Array.Empty<CatalogItem>(), HasMore: false, NextSkip: 0));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(new FixtureDetailState(
                    Details: new MediaDetails(
                        Id: mediaId,
                        Type: mediaType ?? "movie",
                        Name: "First",
                        PosterUrl: null,
                        BackgroundUrl: null,
                        LogoUrl: null,
                        Description: "first",
                        ReleaseInfo: "2026",
                        Runtime: "90 min",
                        Genres: Array.Empty<string>(),
                        ExternalRatings: Array.Empty<MediaExternalRating>(),
                        Cast: Array.Empty<MediaPerson>(),
                        ProductionCompanies: Array.Empty<MediaCompany>(),
                        Trailers: Array.Empty<MediaTrailer>(),
                        Links: Array.Empty<MediaLink>(),
                        Videos: Array.Empty<MediaVideo>()),
                    Streams: Array.Empty<StreamItem>(),
                    FailedProviders: ["broken provider"]));
            }

            throw new InvalidOperationException("details failed");
        }
    }
}
