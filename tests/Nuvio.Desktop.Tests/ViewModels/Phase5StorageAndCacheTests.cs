using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Addons;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;
using Nuvio.Core.Progress;
using Nuvio.Core.Settings;
using Nuvio.Data;
using Nuvio.Data.Images;
using Nuvio.Data.Sqlite;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;
using Nuvio.Player;

namespace Nuvio.Desktop.Tests.ViewModels;

public sealed class Phase5StorageAndCacheTests
{
    [Fact]
    public async Task SettingsPage_LoadsSavesAndClearsCache()
    {
        var settingsStore = new FakeSettingsStore
        {
            Settings = DesktopSettings.Default with { Theme = ThemeMode.Light, InitialVolume = 25 }
        };
        var cacheMaintenance = new FakeCacheMaintenanceService();
        using var decodedCache = new DecodedImageMemoryCache(32);
        var viewModel = new SettingsPageViewModel(settingsStore, cacheMaintenance, decodedCache);

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.InitialVolume = 67;
        viewModel.DecodedImageMemoryItemLimit = 40;
        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.ClearCacheCommand.ExecuteAsync(null);

        Assert.Equal(67, settingsStore.Settings.InitialVolume);
        Assert.Equal(40, decodedCache.Limit);
        Assert.Equal(1, cacheMaintenance.ClearCount);
        Assert.Contains("kept", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearCache_RemovesMetadataAndImagesButPreservesUserData()
    {
        using var paths = TempPlatformPaths.Create();
        var storage = SqliteStorage.Open(paths);
        var settingsStore = new SqliteSettingsStore(storage);
        var addons = new SqliteAddonRepository(storage);
        var progress = new SqliteWatchProgressRepository(storage);
        var metadata = new SqliteMetadataCache(storage);
        var diskImages = new DiskImageCache(storage);
        using var decoded = new DecodedImageMemoryCache(8);
        var maintenance = new DesktopCacheMaintenanceService(metadata, diskImages, decoded);

        await settingsStore.SaveAsync(DesktopSettings.Default with { InitialVolume = 44 }, CancellationToken.None);
        await addons.UpsertAsync(BuildAddon("addon.one"), CancellationToken.None);
        await progress.UpsertAsync(
            new WatchProgress("movie-1", null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), 25, DateTimeOffset.UtcNow),
            CancellationToken.None);
        metadata.SetDetails("movie", "movie-1", BuildDetails("movie-1"));
        await diskImages.StoreAsync(new Uri("https://images.example.test/poster.png"), new MemoryStream(PngBytes), "image/png", CancellationToken.None);

        await maintenance.ClearCacheAsync(CancellationToken.None);

        Assert.Equal(44, (await settingsStore.LoadAsync(CancellationToken.None)).InitialVolume);
        Assert.Single(await addons.ListAsync(CancellationToken.None));
        Assert.NotNull(await progress.GetAsync("movie-1", null, CancellationToken.None));
        Assert.False(metadata.TryGetDetails("movie", "movie-1", out _));
        Assert.Null(await diskImages.GetAsync(new Uri("https://images.example.test/poster.png"), CancellationToken.None));
    }

    [Fact]
    public async Task ProgressRecorder_DebouncesPositionWritesAndFlushesOnPause()
    {
        var repository = new RecordingProgressRepository();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-29T00:00:00Z"));
        var recorder = new PlayerProgressRecorder(repository, time, TimeSpan.FromSeconds(5));
        recorder.Start(BuildDetails("movie-1"));

        await recorder.RecordAsync(new PlayerEvent.PlaybackPositionChanged(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(100), time.GetUtcNow()), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(2));
        await recorder.RecordAsync(new PlayerEvent.PlaybackPositionChanged(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(100), time.GetUtcNow()), CancellationToken.None);

        Assert.Single(repository.Upserts);

        await recorder.RecordAsync(new PlayerEvent.PlaybackStateChanged(IsPlaying: false, time.GetUtcNow()), CancellationToken.None);

        Assert.Equal(2, repository.Upserts.Count);
        Assert.Equal(TimeSpan.FromSeconds(4), repository.Upserts[^1].Position);
    }

    [Fact]
    public async Task Player_EscapeReturnFlushesProgressBeforeNavigation()
    {
        var recorder = new RecordingPlayerProgressRecorder();
        var returned = false;
        var viewModel = new PlayerViewModel(
            new UnusedPlayerEngineFactory(),
            () =>
            {
                returned = true;
                return Task.CompletedTask;
            },
            action => action(),
            progressRecorder: recorder);

        await viewModel.ExitFullscreenOrReturnAsync();

        Assert.True(returned);
        Assert.Single(recorder.Flushes);
        Assert.False(recorder.Flushes[0]);
    }

    [Fact]
    public async Task DetailsPage_LoadsCachedPosterAndBackdropImages()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var imageLoader = new FakeImageLoader();
            var details = BuildDetails("movie-1") with
            {
                PosterUrl = new Uri("https://images.example.test/poster.png"),
                BackgroundUrl = new Uri("https://images.example.test/backdrop.png")
            };
            var viewModel = new DetailsPageViewModel(
                new SingleDetailsDataSource(details),
                (_, _) => Task.CompletedTask,
                imageLoader);

            await viewModel.LoadAsync("movie-1", "movie", CancellationToken.None);

            Assert.True(viewModel.HasPosterImage);
            Assert.True(viewModel.HasBackdropImage);
            Assert.Equal(2, imageLoader.Loaded.Count);
        });
    }

    [Fact]
    public async Task DecodedImageMemoryCache_RespectsItemLimit()
    {
        await RunOnUiThreadAsync(async () =>
        {
            using var cache = new DecodedImageMemoryCache(1);

            await cache.GetOrAddAsync("one", _ => Task.FromResult<Stream>(new MemoryStream(PngBytes)), CancellationToken.None);
            await cache.GetOrAddAsync("two", _ => Task.FromResult<Stream>(new MemoryStream(PngBytes)), CancellationToken.None);

            Assert.Equal(1, cache.Count);
        });
    }

    [Fact]
    public async Task SettingsPageView_RendersPhase5Controls()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = new SettingsPageViewModel(
                new FakeSettingsStore(),
                new FakeCacheMaintenanceService());
            await viewModel.LoadAsync(CancellationToken.None);

            var view = new SettingsPageView
            {
                DataContext = viewModel
            };
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 650
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.NotNull(view.FindControl<ComboBox>("ThemeModeCombo"));
                Assert.NotNull(view.FindControl<ComboBox>("PlayerModeCombo"));
                Assert.NotNull(view.FindControl<CheckBox>("HardwareDecodingToggle"));
                Assert.NotNull(view.FindControl<Slider>("InitialVolumeSlider"));
                Assert.NotNull(view.FindControl<NumericUpDown>("DiskCacheLimitInput"));
                Assert.NotNull(view.FindControl<Button>("ClearCacheButton"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ManagedAddon BuildAddon(string id)
    {
        var manifestUrl = new Uri($"https://{id}.example.test/manifest.json");
        var manifest = new AddonManifest(
            id,
            "Addon",
            string.Empty,
            "1.0.0",
            null,
            [new AddonResource("catalog", ["movie"], [])],
            ["movie"],
            [],
            [],
            new AddonBehaviorHints(),
            manifestUrl);

        return new ManagedAddon(id, manifestUrl, manifest, true, 0, null, DateTimeOffset.UtcNow);
    }

    private static MediaDetails BuildDetails(string id) =>
        new(
            id,
            "movie",
            "Movie",
            null,
            null,
            null,
            "Description",
            "2026",
            "100 min",
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private static readonly object SetupLock = new();
    private static HeadlessUnitTestSession? s_testSession;

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        HeadlessUnitTestSession session;
        lock (SetupLock)
        {
            session = s_testSession ??= HeadlessUnitTestSession.StartNew(typeof(App));
        }

        return session.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public DesktopSettings Settings { get; set; } = DesktopSettings.Default;

        public Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCacheMaintenanceService : ICacheMaintenanceService
    {
        public int ClearCount { get; private set; }
        public long LastDiskCacheLimit { get; private set; }

        public Task ClearCacheAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }

        public Task UpdateDiskCacheLimitAsync(long maxCacheBytes, CancellationToken cancellationToken)
        {
            LastDiskCacheLimit = maxCacheBytes;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProgressRepository : IWatchProgressRepository
    {
        public List<WatchProgress> Upserts { get; } = [];

        public Task<WatchProgress?> GetAsync(string mediaId, string? episodeId, CancellationToken cancellationToken) =>
            Task.FromResult<WatchProgress?>(Upserts.LastOrDefault());

        public Task UpsertAsync(WatchProgress progress, CancellationToken cancellationToken)
        {
            Upserts.Add(progress);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string mediaId, string? episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<WatchProgress>> RecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WatchProgress>>(Upserts.Take(limit).ToArray());
    }

    private sealed class RecordingPlayerProgressRecorder : IPlayerProgressRecorder
    {
        public List<bool> Flushes { get; } = [];

        public void Start(MediaDetails details, string? episodeId = null)
        {
        }

        public Task RecordAsync(PlayerEvent playerEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FlushAsync(bool isEnded, CancellationToken cancellationToken)
        {
            Flushes.Add(isEnded);
            return Task.CompletedTask;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnusedPlayerEngineFactory : IPlayerEngineFactory
    {
        public IPlayerEngine Create(PlayerOptions options) =>
            throw new InvalidOperationException("This test does not start playback.");
    }

    private sealed class FakeImageLoader : IDesktopImageLoader
    {
        public List<Uri> Loaded { get; } = [];

        public Task<IImage?> LoadAsync(Uri sourceUrl, int decodePixelWidth, CancellationToken cancellationToken)
        {
            Loaded.Add(sourceUrl);
            IImage image = new Bitmap(new MemoryStream(PngBytes));
            return Task.FromResult<IImage?>(image);
        }
    }

    private sealed class SingleDetailsDataSource : ICatalogDataSource
    {
        private readonly MediaDetails _details;

        public SingleDetailsDataSource(MediaDetails details)
        {
            _details = details;
        }

        public string ModeLabel => "single";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DesktopHomeRail>>([]);

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            Task.FromResult(new DesktopCatalogPage([], false, 0));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>([]);

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
            Task.FromResult(new FixtureDetailState(_details, [], []));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private sealed class TempPlatformPaths : IPlatformPaths, IDisposable
    {
        private TempPlatformPaths(string root)
        {
            Root = root;
            AppDataDirectory = Path.Combine(root, "data");
            CacheDirectory = Path.Combine(root, "cache");
            LogDirectory = Path.Combine(root, "data", "logs");
            DatabasePath = Path.Combine(root, "data", StoragePlan.Default.DatabaseFileName);
            BackupDirectory = Path.Combine(root, "data", "backups");
            ImageCacheDirectory = Path.Combine(root, "cache", StoragePlan.Default.ImageCacheDirectoryName);
        }

        private string Root { get; }

        public string AppDataDirectory { get; }

        public string CacheDirectory { get; }

        public string LogDirectory { get; }

        public string DatabasePath { get; }

        public string BackupDirectory { get; }

        public string ImageCacheDirectory { get; }

        public static TempPlatformPaths Create() =>
            new(Path.Combine(Path.GetTempPath(), "nuvio-desktop-tests", Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
