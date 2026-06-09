using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Nuvio.Core.Models;
using Nuvio.Core.Progress;
using Nuvio.Core.Services;
using Nuvio.Core.Settings;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;

namespace Nuvio.Desktop.Tests.ViewModels;

public sealed class Phase11HomeTests
{
    private static readonly ICommand NoopCommand = PosterGridBuilder.CreateOpenCommand(_ => Task.CompletedTask);

    [Fact]
    public async Task PosterCard_LoadsImage_AndFallsBackToInitialOnFailure()
    {
        var item = new CatalogItem("id1", "movie", "Name", new Uri("https://images.example.test/p.jpg"), null, null);

        var ok = new PosterCardViewModel(item, NoopCommand, new FakeImageLoader(_ => new FakeImage()));
        Assert.True(ok.ShowInitial);
        await ok.EnsureImageAsync(120, CancellationToken.None);
        Assert.NotNull(ok.PosterImage);
        Assert.False(ok.ShowInitial);
        ok.CancelImageLoad();

        var failing = new PosterCardViewModel(item, NoopCommand,
            new FakeImageLoader(_ => throw new InvalidOperationException("boom")));
        await failing.EnsureImageAsync(120, CancellationToken.None);
        Assert.Null(failing.PosterImage);
        Assert.True(failing.ShowInitial);
    }

    [Fact]
    public async Task Home_Hero_PopulatesDistinctItems()
    {
        var rails = new[]
        {
            Rail("a", "m1", "m2"),
            Rail("b", "m2", "m3"), // m2 duplicated across rails
        };
        var vm = new HomePageViewModel(new FakeHomeDataSource(rails), _ => Task.CompletedTask);

        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.HasHero);
        Assert.NotNull(vm.Hero);
        Assert.True(vm.Hero!.Items.Count <= HomeRailDefaults.HeroItemLimit);
        Assert.Equal(3, vm.Hero.Items.Count); // m1, m2, m3 after dedupe
        Assert.NotNull(vm.Hero.SelectedItem);
    }

    [Fact]
    public async Task Hero_BackdropLoad_IsCancellable()
    {
        var item = new CatalogItem("id", "movie", "N", null, new Uri("https://images.example.test/b.jpg"), null);
        var hero = new HomeHeroItemViewModel(item, NoopCommand, new FakeImageLoader(_ => throw new OperationCanceledException()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await hero.EnsureBackdropAsync(cts.Token); // must not throw

        Assert.Null(hero.BackdropImage);
    }

    [Fact]
    public async Task Home_ContinueWatching_FiltersCompletedAndShowsProgress()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            new WatchProgress("done", "", TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(20), 95, now,
                MediaType: "movie", Title: "Done"),
            new WatchProgress("wip", "", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20), 25, now,
                MediaType: "movie", Title: "In Progress", PosterUrl: new Uri("https://images.example.test/p.jpg")),
        };
        var vm = new HomePageViewModel(
            new FakeHomeDataSource(),
            _ => Task.CompletedTask,
            progressRepository: new FakeWatchProgressRepository(entries));

        await vm.LoadAsync(CancellationToken.None);

        var continueWatching = vm.Sections.FirstOrDefault(section => section.Title == "Continue Watching");
        Assert.NotNull(continueWatching);
        Assert.Single(continueWatching!.Items);
        var card = continueWatching.Items[0];
        Assert.Equal("In Progress", card.Name);
        Assert.True(card.ShowProgress);
        Assert.Equal(0.25, card.Progress, precision: 3);
    }

    [Fact]
    public async Task Home_ProgressivePublish_AppendsSectionsIncrementally()
    {
        var rails = new[] { Rail("a", "m1"), Rail("b", "m2"), Rail("c", "m3") };
        var dataSource = new GatedHomeDataSource(rails);
        var vm = new HomePageViewModel(dataSource, _ => Task.CompletedTask);

        var loadTask = vm.LoadAsync(CancellationToken.None);
        await dataSource.FirstRailPublished.Task;

        // Only the first rail has been published; the rest are gated.
        Assert.Single(vm.Sections);
        Assert.True(vm.IsLoading);

        dataSource.ReleaseRemaining.SetResult();
        await loadTask;

        Assert.Equal(3, vm.Sections.Count);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Home_LiveMode_HasNoFixtureCopy()
    {
        var vm = new HomePageViewModel(
            new FakeHomeDataSource(new[] { Rail("a", "m1") }, modeLabel: "Live addons"),
            _ => Task.CompletedTask);

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("Live addons", vm.ModeLabel);
        Assert.False(vm.IsFixtureMode);
    }

    [Fact]
    public async Task Home_EmptyState_DistinguishesNoAddonsFromAllFailed()
    {
        var noAddons = new HomePageViewModel(new FakeHomeDataSource(hasAddons: false), _ => Task.CompletedTask);
        await noAddons.LoadAsync(CancellationToken.None);
        Assert.Equal(HomeEmptyState.NoAddons, noAddons.EmptyState);
        Assert.True(noAddons.IsEmpty);

        var allFailed = new HomePageViewModel(new FakeHomeDataSource(hasAddons: true), _ => Task.CompletedTask);
        await allFailed.LoadAsync(CancellationToken.None);
        Assert.Equal(HomeEmptyState.AllFailed, allFailed.EmptyState);

        Assert.NotEqual(noAddons.EmptyMessage, allFailed.EmptyMessage);
    }

    [Fact]
    public async Task Home_AppliesCatalogSettings_ReorderRenameDisable()
    {
        var rails = new[] { Rail("a", "m1"), Rail("b", "m2"), Rail("c", "m3") };
        var settings = new HomeCatalogSettings
        {
            HeroEnabled = true,
            Preferences =
            [
                new HomeCatalogPreference("b", 0),
                new HomeCatalogPreference("a", 1, CustomTitle: "Renamed A"),
                new HomeCatalogPreference("c", 2, Enabled: false),
            ]
        };
        var vm = new HomePageViewModel(
            new FakeHomeDataSource(rails),
            _ => Task.CompletedTask,
            settingsStore: new FakeSettingsStore(settings));

        await vm.LoadAsync(CancellationToken.None);

        var titles = vm.Sections.Select(section => section.Title).ToArray();
        Assert.Equal(["b title", "Renamed A"], titles);
    }

    private static DesktopHomeRail Rail(string key, params string[] itemIds)
    {
        var ids = itemIds.Length == 0 ? [key + "-1"] : itemIds;
        var items = ids
            .Select(id => new CatalogItem(id, "movie", id, null, null, null))
            .ToArray();
        return new DesktopHomeRail(
            Title: key + " title",
            Items: items,
            Key: key,
            AddonId: key,
            AddonName: key,
            Type: "movie",
            CatalogId: key);
    }

    private sealed class FakeImage : IImage
    {
        public Size Size => new(10, 10);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }
    }

    private sealed class FakeImageLoader : IDesktopImageLoader
    {
        private readonly Func<Uri, IImage?> _factory;

        public FakeImageLoader(Func<Uri, IImage?> factory) => _factory = factory;

        public Task<IImage?> LoadAsync(Uri sourceUrl, int decodePixelWidth, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(_factory(sourceUrl));
            }
            catch (Exception ex)
            {
                return Task.FromException<IImage?>(ex);
            }
        }
    }

    private sealed class FakeHomeDataSource : ICatalogDataSource
    {
        private readonly IReadOnlyList<DesktopHomeRail> _rails;
        private readonly bool _hasAddons;

        public FakeHomeDataSource(
            IReadOnlyList<DesktopHomeRail>? rails = null,
            string modeLabel = "Live addons",
            bool hasAddons = true)
        {
            _rails = rails ?? Array.Empty<DesktopHomeRail>();
            ModeLabel = modeLabel;
            _hasAddons = hasAddons;
        }

        public string ModeLabel { get; }

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_rails);

        public Task<bool> HasCatalogCapableAddonsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_hasAddons);

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            Task.FromResult(new DesktopCatalogPage(Array.Empty<CatalogItem>(), HasMore: false, NextSkip: 0));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class GatedHomeDataSource : ICatalogDataSource
    {
        private readonly IReadOnlyList<DesktopHomeRail> _rails;

        public GatedHomeDataSource(IReadOnlyList<DesktopHomeRail> rails) => _rails = rails;

        public TaskCompletionSource FirstRailPublished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRemaining { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ModeLabel => "Live addons";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_rails);

        public async IAsyncEnumerable<DesktopHomeRail> StreamHomeRailsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return _rails[0];
            FirstRailPublished.SetResult();
            await ReleaseRemaining.Task;
            for (var i = 1; i < _rails.Count; i++)
            {
                yield return _rails[i];
            }
        }

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            Task.FromResult(new DesktopCatalogPage(Array.Empty<CatalogItem>(), HasMore: false, NextSkip: 0));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeWatchProgressRepository : IWatchProgressRepository
    {
        private readonly IReadOnlyList<WatchProgress> _recent;

        public FakeWatchProgressRepository(IReadOnlyList<WatchProgress> recent) => _recent = recent;

        public Task<WatchProgress?> GetAsync(string mediaId, string? episodeId, CancellationToken cancellationToken) =>
            Task.FromResult<WatchProgress?>(null);

        public Task UpsertAsync(WatchProgress progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RemoveAsync(string mediaId, string? episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<WatchProgress>> RecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult(_recent);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private HomeCatalogSettings _home;

        public FakeSettingsStore(HomeCatalogSettings home) => _home = home;

        public Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DesktopSettings.Default);

        public Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<HomeCatalogSettings> LoadHomeCatalogSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_home);

        public Task SaveHomeCatalogSettingsAsync(HomeCatalogSettings settings, CancellationToken cancellationToken)
        {
            _home = settings;
            return Task.CompletedTask;
        }
    }
}
