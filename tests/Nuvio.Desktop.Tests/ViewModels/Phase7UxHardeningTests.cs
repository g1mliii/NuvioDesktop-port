using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nuvio.Core.Models;
using Nuvio.Core.Settings;
using Nuvio.Data;
using Nuvio.Data.Sqlite;
using Nuvio.Desktop;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;
using Nuvio.Player;

namespace Nuvio.Desktop.Tests.ViewModels;

public sealed class Phase7UxHardeningTests
{
    private static readonly object SetupLock = new();
    private static HeadlessUnitTestSession? s_testSession;

    // ---- A: Theme ----

    [Theory]
    [InlineData(ThemeMode.System)]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ThemeController_MapsThemeModeToVariant(ThemeMode mode)
    {
        var expected = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        Assert.Equal(expected, ThemeController.ToVariant(mode));
    }

    [Fact]
    public async Task SettingsPage_ChangingTheme_AppliesLiveAndOnSave()
    {
        var controller = new RecordingThemeController();
        var viewModel = new SettingsPageViewModel(
            new RecordingSettingsStore(),
            new NoopCacheMaintenance(),
            themeController: controller);

        viewModel.SelectedTheme = viewModel.ThemeOptions.Single(option => option.Value == ThemeMode.Dark);
        Assert.Equal(ThemeMode.Dark, controller.LastApplied);

        viewModel.SelectedTheme = viewModel.ThemeOptions.Single(option => option.Value == ThemeMode.Light);
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(ThemeMode.Light, controller.LastApplied);
    }

    [Fact]
    public async Task Theme_AppliesVariantAndTokensResolveInBothVariants()
    {
        await RunOnUiThreadAsync(() =>
        {
            new ThemeController().Apply(ThemeMode.Dark);
            Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);

            Assert.True(Avalonia.Application.Current!.TryGetResource("BackgroundBrush", ThemeVariant.Dark, out var dark));
            Assert.NotNull(dark);
            Assert.True(Avalonia.Application.Current!.TryGetResource("BackgroundBrush", ThemeVariant.Light, out var light));
            Assert.NotNull(light);

            new ThemeController().Apply(ThemeMode.System);
            Assert.Equal(ThemeVariant.Default, Avalonia.Application.Current!.RequestedThemeVariant);
            return Task.CompletedTask;
        });
    }

    // ---- C / H: Responsive layout + TV focus mode ----

    [Fact]
    public async Task ResponsiveLayout_TransitionsAcrossWidthBreakpoints()
    {
        var viewModel = CreateViewModel();

        try
        {
            viewModel.UpdateLayoutForWidth(900);
            Assert.Equal(DesktopLayoutMode.Compact, viewModel.LayoutMode);
            Assert.True(viewModel.IsCompactLayout);
            Assert.False(viewModel.IsSidebarExpanded);
            Assert.Equal(64d, viewModel.SidebarWidth);

            viewModel.UpdateLayoutForWidth(1200);
            Assert.Equal(DesktopLayoutMode.Normal, viewModel.LayoutMode);
            Assert.True(viewModel.IsSidebarExpanded);

            viewModel.UpdateLayoutForWidth(1600);
            Assert.Equal(DesktopLayoutMode.Wide, viewModel.LayoutMode);
            Assert.True(viewModel.PosterCardWidth > 150d);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task TvFocusMode_ForcesTvLayoutRegardlessOfWidth()
    {
        var viewModel = CreateViewModel();

        try
        {
            viewModel.UpdateLayoutForWidth(1600);

            viewModel.ApplyPersistedShellSettings(DesktopSettings.Default with { TvFocusMode = true });

            Assert.True(viewModel.TvFocusMode);
            Assert.Equal(DesktopLayoutMode.Tv, viewModel.LayoutMode);
            Assert.True(viewModel.IsTvLayout);

            // Resizing while TV focus mode is on keeps the Tv bucket.
            viewModel.UpdateLayoutForWidth(800);
            Assert.Equal(DesktopLayoutMode.Tv, viewModel.LayoutMode);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task Responsive_ResizeStressKeepsVirtualizationBoundedAndCollapsesSidebar()
    {
        await RunOnUiThreadAsync(async () =>
        {
            // Large catalog so virtualization must stay bounded across resizes.
            var bigViewModel = MainWindowViewModel.CreateFixture(
                platformInfo: new PlatformInfo(PlatformFamily.Windows, "win-x64", "win test"),
                mpvDiscovery: MpvDiscoveryResult.NotFound("test"),
                fixtures: new DesktopFixtureService(itemCount: 1_000),
                playerEngineFactory: new RecordingEngineFactory());
            var window = new MainWindow { Width = 1180, Height = 760, DataContext = bigViewModel };

            try
            {
                window.Show();
                await bigViewModel.NavigateAsync(DesktopRoute.Catalog);
                window.UpdateLayout();

                foreach (var width in new[] { 1600d, 900d, 1300d, 850d, 1500d })
                {
                    bigViewModel.UpdateLayoutForWidth(width);
                    window.Width = width;
                    window.UpdateLayout();
                }

                var sidebar = FindByName<Border>(window, "SidebarBorder");
                Assert.NotNull(sidebar);

                bigViewModel.UpdateLayoutForWidth(850);
                window.UpdateLayout();
                Assert.True(bigViewModel.IsCompactLayout);
                Assert.Equal(64d, sidebar!.Width);

                var realizedCards = window.GetVisualDescendants()
                    .OfType<Avalonia.Controls.Button>()
                    .Count(button => button.Name == "PosterCard");
                Assert.InRange(realizedCards, 1, 250);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
                await bigViewModel.DisposeAsync();
            }
        });
    }

    // ---- E: Window-level fullscreen ----

    [Fact]
    public async Task Fullscreen_TogglesChromeVisibilityAndExitsOnEscape()
    {
        var engineFactory = new RecordingEngineFactory();
        var viewModel = CreateViewModel(engineFactory);

        try
        {
            await viewModel.NavigateAsync(DesktopRoute.Player);

            await viewModel.Player.ToggleFullscreenCommand.ExecuteAsync(null);
            Assert.True(viewModel.IsPlayerFullscreen);
            Assert.False(viewModel.IsChromeVisible);
            Assert.False(viewModel.IsInWindowMenuVisible);

            // Escape exits fullscreen first (does not leave the player).
            Assert.True(await viewModel.HandleShortcutAsync(Key.Escape, KeyModifiers.None));
            Assert.False(viewModel.IsPlayerFullscreen);
            Assert.True(viewModel.IsChromeVisible);
            Assert.Equal(DesktopRouteKind.Player, viewModel.CurrentRoute.Kind);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task Fullscreen_F11TogglesAndNavigatingAwayClearsIt()
    {
        var viewModel = CreateViewModel(new RecordingEngineFactory());

        try
        {
            await viewModel.NavigateAsync(DesktopRoute.Player);

            Assert.True(viewModel.CanHandleShortcut(Key.F11, KeyModifiers.None));
            Assert.True(await viewModel.HandleShortcutAsync(Key.F11, KeyModifiers.None));
            Assert.True(viewModel.IsPlayerFullscreen);

            await viewModel.NavigateAsync(DesktopRoute.Home);
            Assert.False(viewModel.IsPlayerFullscreen);
            Assert.True(viewModel.IsChromeVisible);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task Fullscreen_PromotesWindowStateAndRestoresOnExit()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = CreateViewModel(new RecordingEngineFactory());
            var window = new MainWindow { Width = 1180, Height = 760, DataContext = viewModel };

            try
            {
                window.Show();
                await viewModel.NavigateAsync(DesktopRoute.Player);
                window.UpdateLayout();

                await viewModel.Player.ToggleFullscreenCommand.ExecuteAsync(null);
                window.UpdateLayout();
                Assert.Equal(WindowState.FullScreen, window.WindowState);

                await viewModel.Player.ToggleFullscreenCommand.ExecuteAsync(null);
                window.UpdateLayout();
                Assert.NotEqual(WindowState.FullScreen, window.WindowState);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }

    // ---- F: Platform menus ----

    [Fact]
    public async Task PlatformMenu_InWindowVisibleOnWindowsHiddenOnMac()
    {
        var windows = CreateViewModel(platformInfo: new PlatformInfo(PlatformFamily.Windows, "win-x64", "win"));
        var mac = CreateViewModel(platformInfo: new PlatformInfo(PlatformFamily.MacOS, "osx-arm64", "mac"));

        try
        {
            Assert.False(windows.IsMacOS);
            Assert.True(windows.IsInWindowMenuVisible);

            Assert.True(mac.IsMacOS);
            Assert.False(mac.IsInWindowMenuVisible);
        }
        finally
        {
            await windows.DisposeAsync();
            await mac.DisposeAsync();
        }
    }

    [Fact]
    public async Task PlatformMenu_NativeMenuConstructionExposesCoreItems()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = CreateViewModel(platformInfo: new PlatformInfo(PlatformFamily.MacOS, "osx-arm64", "mac"));

            try
            {
                var menu = MainWindow.BuildNativeMenu(viewModel);

                var headers = menu.Items
                    .OfType<NativeMenuItem>()
                    .Select(item => item.Header)
                    .ToList();

                Assert.Contains("Nuvio Desktop", headers);
                Assert.Contains("File", headers);
                Assert.Contains("Browse", headers);
                Assert.Contains("Playback", headers);
                Assert.Contains("View", headers);
                Assert.Contains("Window", headers);

                var commandHeaders = menu.Items
                    .OfType<NativeMenuItem>()
                    .SelectMany(item => item.Menu?.Items.OfType<NativeMenuItem>() ?? Enumerable.Empty<NativeMenuItem>())
                    .Select(item => item.Header)
                    .ToList();

                Assert.Contains("Back", commandHeaders);
                Assert.Contains("Search", commandHeaders);
                Assert.Contains("Play / Pause", commandHeaders);
                Assert.Contains("Seek Back", commandHeaders);
                Assert.Contains("Seek Forward", commandHeaders);
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }

    // ---- G: File dialog -> playback ----

    [Fact]
    public async Task OpenMediaFile_PlaysLocalFileThroughEngine()
    {
        var engineFactory = new RecordingEngineFactory();
        var viewModel = CreateViewModel(engineFactory);

        try
        {
            await viewModel.PlayLocalFileAsync(@"C:\media\example.mkv");

            Assert.Equal(DesktopRouteKind.Player, viewModel.CurrentRoute.Kind);
            Assert.Equal(["Initialize", "Load", "Play"], engineFactory.Engine.Calls);
            Assert.NotNull(engineFactory.Engine.LoadedSource);
            Assert.True(engineFactory.Engine.LoadedSource!.IsUserProvided);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task OpenMediaFile_AcceptsUnixStyleLocalPath()
    {
        var engineFactory = new RecordingEngineFactory();
        var viewModel = CreateViewModel(engineFactory);

        try
        {
            await viewModel.PlayLocalFileAsync("/home/subai/video sample.mkv");

            Assert.Equal("file", engineFactory.Engine.LoadedSource?.Url.Scheme);
            Assert.Equal("/home/subai/video sample.mkv", engineFactory.Engine.LoadedSource?.Url.LocalPath);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task OpenMediaFileCommand_RaisesRequestEvent()
    {
        var viewModel = CreateViewModel();
        var raised = false;

        try
        {
            viewModel.OpenMediaFileRequested += () => raised = true;
            viewModel.OpenMediaFileCommand.Execute(null);

            Assert.True(raised);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    // ---- I: Mini-player ----

    [Fact]
    public async Task MiniPlayer_ToggleFlipsLayoutFlag()
    {
        var player = new PlayerViewModel(new RecordingEngineFactory(), () => Task.CompletedTask, action => action());

        try
        {
            Assert.False(player.IsMiniMode);
            Assert.True(player.IsFullPlayer);

            await player.ToggleMiniModeCommand.ExecuteAsync(null);
            Assert.True(player.IsMiniMode);
            Assert.False(player.IsFullPlayer);

            await player.ToggleMiniModeCommand.ExecuteAsync(null);
            Assert.False(player.IsMiniMode);
        }
        finally
        {
            await player.DisposeAsync();
        }
    }

    // ---- J: Accessibility ----

    [Fact]
    public async Task Accessibility_KeyControlsExposeAutomationNames()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = CreateViewModel(new RecordingEngineFactory());
            var window = new MainWindow { Width = 1180, Height = 760, DataContext = viewModel };

            try
            {
                window.Show();
                await viewModel.NavigateAsync(DesktopRoute.Home);
                window.UpdateLayout();

                var back = FindByName<Button>(window, "BackButton");
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(back!)));
                var search = FindByName<Button>(window, "SearchButton");
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(search!)));

                await viewModel.NavigateAsync(DesktopRoute.Player);
                window.UpdateLayout();
                var playPause = FindByName<Button>(window, "PlayPauseButton");
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(playPause!)));
            }
            finally
            {
                window.DataContext = null;
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task TvFocusMode_AppliesFocusClassToRenderedControls()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = CreateViewModel(new RecordingEngineFactory());
            var window = new MainWindow { Width = 1180, Height = 760, DataContext = viewModel };

            try
            {
                window.Show();
                viewModel.ApplyPersistedShellSettings(DesktopSettings.Default with { TvFocusMode = true });
                window.UpdateLayout();

                var back = FindByName<Button>(window, "BackButton");
                Assert.NotNull(back);
                Assert.Contains("tv-focus", back!.Classes);

                await viewModel.NavigateAsync(DesktopRoute.Player);
                window.UpdateLayout();

                var playPause = FindByName<Button>(window, "PlayPauseButton");
                Assert.NotNull(playPause);
                Assert.Contains("tv-focus", playPause!.Classes);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }

    // ---- K: High-DPI decode sizing ----

    [Fact]
    public void ImageDecodeSizing_PosterAndBackdropBoundedAt2x()
    {
        Assert.Equal(360, ImageDecodeSizing.PosterDecodeWidth(180, 2d));
        // A very large layout × scaling is clamped to the hard ceiling.
        Assert.Equal(ImageDecodeSizing.MaxPosterDecodeWidth, ImageDecodeSizing.PosterDecodeWidth(4000, 2d));
        Assert.Equal(ImageDecodeSizing.MaxBackdropDecodeWidth, ImageDecodeSizing.BackdropDecodeWidth(4000, 2d));
        Assert.InRange(
            ImageDecodeSizing.PosterDecodeWidth(180, 2d),
            ImageDecodeSizing.MinPosterDecodeWidth,
            ImageDecodeSizing.MaxPosterDecodeWidth);
    }

    [Fact]
    public async Task DecodedImageCache_DecodesToBoundedWidth()
    {
        await RunOnUiThreadAsync(async () =>
        {
            using var cache = new DecodedImageMemoryCache(8);
            var pngBytes = BuildLargePng(2000, 3000);
            const int decodeWidth = 360;

            var image = await cache.GetOrAddAsync(
                $"poster@w{decodeWidth}",
                _ => Task.FromResult<Stream>(new MemoryStream(pngBytes)),
                CancellationToken.None,
                decode: stream => Bitmap.DecodeToWidth(stream, decodeWidth));

            var bitmap = Assert.IsType<Bitmap>(image);
            Assert.Equal(decodeWidth, bitmap.PixelSize.Width);
        });
    }

    [Fact]
    public async Task DetailsImageDecode_UsesShellMetricsAndRenderScaling()
    {
        var imageLoader = new RecordingImageLoader();
        var viewModel = new MainWindowViewModel(
            new PlatformInfo(PlatformFamily.Windows, "win-x64", "win test"),
            MpvDiscoveryResult.NotFound("test"),
            new SingleDetailsDataSource(),
            addonService: null,
            new RecordingEngineFactory(),
            imageLoader: imageLoader);

        try
        {
            viewModel.UpdateShellMetrics(1440, 1.5);
            await viewModel.NavigateAsync(DesktopRoute.Details("movie-1", "movie"));

            Assert.Equal(2, imageLoader.DecodePixelWidths.Count);
            Assert.Equal(270, imageLoader.DecodePixelWidths[0]);
            Assert.Equal(ImageDecodeSizing.MaxBackdropDecodeWidth, imageLoader.DecodePixelWidths[1]);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    // ---- H: TvFocusMode persistence round-trip ----

    [Fact]
    public async Task Settings_TvFocusModeRoundTripsThroughSqlite()
    {
        using var paths = TempPaths.Create();
        var storage = SqliteStorage.Open(paths);
        var store = new SqliteSettingsStore(storage);

        await store.SaveAsync(DesktopSettings.Default with { TvFocusMode = true }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.TvFocusMode);
    }

    // ---- helpers ----

    private static MainWindowViewModel CreateViewModel(
        IPlayerEngineFactory? engineFactory = null,
        PlatformInfo? platformInfo = null) =>
        MainWindowViewModel.CreateFixture(
            platformInfo: platformInfo ?? new PlatformInfo(PlatformFamily.Windows, "win-x64", "win test"),
            mpvDiscovery: MpvDiscoveryResult.NotFound("test"),
            playerEngineFactory: engineFactory ?? new RecordingEngineFactory());

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

    private static T? FindByName<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    private static byte[] BuildLargePng(int width, int height)
    {
        // A solid bitmap encoded to PNG so DecodeToWidth has a real, large source to downscale.
        using var pixels = new WriteableBitmap(
            new Avalonia.PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        using var stream = new MemoryStream();
        pixels.Save(stream);
        return stream.ToArray();
    }

    private sealed class RecordingThemeController : IThemeController
    {
        public ThemeMode? LastApplied { get; private set; }

        public void Apply(ThemeMode mode) => LastApplied = mode;
    }

    private sealed class RecordingSettingsStore : ISettingsStore
    {
        public DesktopSettings Settings { get; private set; } = DesktopSettings.Default;

        public Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopCacheMaintenance : ICacheMaintenanceService
    {
        public Task ClearCacheAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateDiskCacheLimitAsync(long limitBytes, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingImageLoader : IDesktopImageLoader
    {
        public List<int> DecodePixelWidths { get; } = [];

        public Task<IImage?> LoadAsync(Uri sourceUrl, int decodePixelWidth, CancellationToken cancellationToken)
        {
            DecodePixelWidths.Add(decodePixelWidth);
            return Task.FromResult<IImage?>(null);
        }
    }

    private sealed class SingleDetailsDataSource : ICatalogDataSource
    {
        public string ModeLabel => "test";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DesktopHomeRail>>([]);

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            Task.FromResult(new DesktopCatalogPage([], HasMore: false, NextSkip: 0));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>([]);

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken)
        {
            var details = new MediaDetails(
                mediaId,
                mediaType ?? "movie",
                "Movie One",
                new Uri("https://images.example.test/poster.png"),
                new Uri("https://images.example.test/backdrop.png"),
                null,
                "Test details",
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<MediaExternalRating>(),
                Array.Empty<MediaPerson>(),
                Array.Empty<MediaCompany>(),
                Array.Empty<MediaTrailer>(),
                Array.Empty<MediaLink>(),
                Array.Empty<MediaVideo>());

            return Task.FromResult(new FixtureDetailState(details, [], null));
        }
    }

    private sealed class RecordingEngineFactory : IPlayerEngineFactory
    {
        public RecordingEngine Engine { get; } = new();

        public IPlayerEngine Create(PlayerOptions options) => Engine;
    }

    private sealed class RecordingEngine : IPlayerEngine
    {
        private readonly Channel<PlayerEvent> _events = Channel.CreateUnbounded<PlayerEvent>();

        public List<string> Calls { get; } = [];

        public StreamSource? LoadedSource { get; private set; }

        public Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken)
        {
            Calls.Add("Initialize");
            return Task.CompletedTask;
        }

        public Task LoadAsync(StreamSource source, CancellationToken cancellationToken)
        {
            Calls.Add("Load");
            LoadedSource = source;
            return Task.CompletedTask;
        }

        public Task PlayAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Play");
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Pause");
            return Task.CompletedTask;
        }

        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken)
        {
            Calls.Add("Seek");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Stop");
            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken)
        {
            Calls.Add("Fullscreen");
            return Task.CompletedTask;
        }

        public Task SelectAudioTrackAsync(string trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SelectSubtitleTrackAsync(string? trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<PlayerEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var playerEvent in _events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return playerEvent;
            }
        }

        public ValueTask DisposeAsync()
        {
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempPaths : IPlatformPaths, IDisposable
    {
        private TempPaths(string root)
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

        public static TempPaths Create() =>
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
