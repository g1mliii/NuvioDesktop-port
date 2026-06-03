using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Core.Services;
using Nuvio.Core.Settings;
using Nuvio.Desktop;
using Nuvio.Desktop.Controls;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;
using Nuvio.Player;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nuvio.Desktop.Tests;

public sealed class AvaloniaShellSmokeTests
{
    private static readonly object SetupLock = new();
    private static HeadlessUnitTestSession? s_testSession;

    [Fact]
    public void MainWindowViewModel_DefaultConstructor_DoesNotRunBlockingMpvDiscovery()
    {
        var viewModel = MainWindowViewModel.CreateFixture();

        Assert.Equal("Not found", viewModel.MpvStatus);
        Assert.Equal("Not detected", viewModel.MpvPath);
        Assert.Equal("Unavailable", viewModel.MpvVersion);
        Assert.Contains("without blocking app startup", viewModel.MpvMessage);
    }

    [Fact]
    public async Task Navigation_TransitionsAndBackStackAreDeterministic()
    {
        var viewModel = CreateViewModel();

        await viewModel.NavigateAsync(DesktopRoute.Search);
        await viewModel.NavigateAsync(DesktopRoute.Catalog);

        Assert.Equal(DesktopRouteKind.Catalog, viewModel.CurrentRoute.Kind);
        Assert.True(viewModel.NavigationItems.Single(item => item.Route.Kind == DesktopRouteKind.Catalog).IsSelected);
        Assert.True(viewModel.CanGoBack);

        await viewModel.GoBackAsync();

        Assert.Equal(DesktopRouteKind.Search, viewModel.CurrentRoute.Kind);
        Assert.True(viewModel.NavigationItems.Single(item => item.Route.Kind == DesktopRouteKind.Search).IsSelected);
    }

    [Fact]
    public async Task MainWindowViewModel_DisposeIsIdempotentAndStopsFurtherNavigation()
    {
        var viewModel = CreateViewModel();

        await viewModel.NavigateAsync(DesktopRoute.Search);
        await viewModel.DisposeAsync();
        await viewModel.DisposeAsync();
        await viewModel.NavigateAsync(DesktopRoute.Catalog);

        Assert.Equal(DesktopRouteKind.Search, viewModel.CurrentRoute.Kind);
    }

    [Fact]
    public async Task Search_CancelsStaleRequest()
    {
        var service = new SlowSearchFixtureService();
        var search = new SearchPageViewModel(new FixtureCatalogDataSource(service), _ => Task.CompletedTask);

        var staleSearch = search.SearchAsync("first");
        await service.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await search.SearchAsync("second");
        await staleSearch;

        Assert.True(service.FirstSearchToken.IsCancellationRequested);
        Assert.Equal(1, search.ResultCount);
        Assert.Contains("second", search.Rows.Single().Items.Single().Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixtureService_MapsCatalogDetailsAndPlayableStreams()
    {
        var service = new DesktopFixtureService(itemCount: 12);
        var catalog = await service.GetCatalogAsync(CancellationToken.None);
        var details = await service.GetDetailsAsync(catalog[0].Id, CancellationToken.None);

        Assert.Equal(12, catalog.Count);
        Assert.Equal(catalog[0].Id, details.Details.Id);
        Assert.NotEmpty(details.Details.Genres);
        Assert.All(details.Streams, stream => Assert.True(stream.HasPlayableSource));
    }

    [Fact]
    public async Task DetailsPlayAction_RoutesThroughIPlayerEngine()
    {
        var engineFactory = new RecordingPlayerEngineFactory();
        var viewModel = CreateViewModel(engineFactory: engineFactory);

        await viewModel.NavigateAsync(DesktopRoute.Details("fixture-0001"));
        var details = Assert.IsType<DetailsPageViewModel>(viewModel.CurrentPage);
        var playCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(details.Streams[0].PlayCommand);

        await playCommand.ExecuteAsync(null);

        Assert.Equal(DesktopRouteKind.Player, viewModel.CurrentRoute.Kind);
        Assert.Same(engineFactory.Engine, engineFactory.CreatedEngines.Single());
        Assert.Equal(["Initialize", "Load", "Play"], engineFactory.Engine.Calls);
        Assert.Equal(details.Streams[0].Source, engineFactory.Engine.LoadedSource);
    }

    [Fact]
    public async Task Player_DisposesEngineWhenPlaybackStartupFails()
    {
        var engineFactory = new LoadFailingPlayerEngineFactory();
        var viewModel = new PlayerViewModel(
            engineFactory,
            () => Task.CompletedTask,
            action => action());

        await viewModel.LoadAndPlayAsync(CreateStreamSource(), CreateMediaDetails(), CancellationToken.None);

        Assert.True(engineFactory.Engine.IsDisposed);
        Assert.Equal("Player error", viewModel.Status);
        Assert.Contains("fixture load failure", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Player_CoalescesHighFrequencyStatusEventsBeforeUiDispatch()
    {
        var engineFactory = new RecordingPlayerEngineFactory();
        var uiDispatchCount = 0;
        var viewModel = new PlayerViewModel(
            engineFactory,
            () => Task.CompletedTask,
            action =>
            {
                Interlocked.Increment(ref uiDispatchCount);
                action();
            });

        await viewModel.LoadAndPlayAsync(CreateStreamSource(), CreateMediaDetails(), CancellationToken.None);
        await Task.Delay(50);
        Interlocked.Exchange(ref uiDispatchCount, 0);

        for (var index = 0; index < 30; index++)
        {
            engineFactory.Engine.WriteEvent(new PlayerEvent.PlaybackPositionChanged(
                TimeSpan.FromSeconds(index),
                TimeSpan.FromMinutes(2),
                DateTimeOffset.UtcNow));
            engineFactory.Engine.WriteEvent(new PlayerEvent.PlayerLog($"log {index}", DateTimeOffset.UtcNow));
        }

        // Wait for the coalescer to drain the 60 queued events and dispatch the latest values,
        // rather than racing a fixed delay against the async observe loop (flaky under CI load).
        await WaitForConditionAsync(() =>
            viewModel.Position == TimeSpan.FromSeconds(29) && viewModel.Status == "log 29");

        Assert.Equal(TimeSpan.FromSeconds(29), viewModel.Position);
        Assert.Equal("log 29", viewModel.Status);
        // The 60 high-frequency events must collapse into only a handful of UI dispatches; the exact
        // count depends on how many 250ms coalescing windows the drain spans, so allow a small range.
        Assert.InRange(uiDispatchCount, 1, 12);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task PlayerView_HidesPlaceholder_WhenRenderSourceIsActive()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var engineFactory = new RecordingRenderablePlayerEngineFactory();
            var viewModel = new PlayerViewModel(
                engineFactory,
                () => Task.CompletedTask,
                action => action(),
                settingsStore: new FakeSettingsStore
                {
                    Settings = DesktopSettings.Default with { PlayerMode = PlayerMode.LibMpv }
                });
            var window = new Window
            {
                Width = 900,
                Height = 560,
                Content = new PlayerView { DataContext = viewModel }
            };

            try
            {
                await viewModel.LoadAndPlayAsync(CreateStreamSource(), CreateMediaDetails(), CancellationToken.None);
                window.Show();
                window.UpdateLayout();

                var videoHost = Assert.IsType<LibMpvVideoView>(FindVisualDescendant<LibMpvVideoView>(window, "VideoHost"));
                var placeholder = Assert.IsType<StackPanel>(FindVisualDescendant<StackPanel>(window, "PlaybackPlaceholder"));

                Assert.Same(engineFactory.Engine, viewModel.VideoSource);
                Assert.Same(engineFactory.Engine, videoHost.Engine);
                Assert.True(videoHost.IsVisible);
                Assert.False(placeholder.IsVisible);
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task Player_FallsBackToExternalMpv_WhenRenderSourceReportsFailure()
    {
        var engineFactory = new RenderFailureFallbackPlayerEngineFactory();
        var viewModel = new PlayerViewModel(
            engineFactory,
            () => Task.CompletedTask,
            action => action(),
            settingsStore: new FakeSettingsStore
            {
                Settings = DesktopSettings.Default with { PlayerMode = PlayerMode.LibMpv }
            });

        try
        {
            await viewModel.LoadAndPlayAsync(CreateStreamSource(), CreateMediaDetails(), CancellationToken.None);

            Assert.Same(engineFactory.RenderEngine, viewModel.VideoSource);

            engineFactory.RenderEngine.ReportRenderFailure(new InvalidOperationException("OpenGL unavailable"));

            await WaitForConditionAsync(() => engineFactory.ExternalEngine.Calls.Contains("Play") && viewModel.VideoSource is null);

            Assert.True(engineFactory.RenderEngine.IsDisposed);
            Assert.Equal(["Initialize", "Load", "Play"], engineFactory.ExternalEngine.Calls);
            Assert.Contains("render failed", viewModel.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("external mpv", viewModel.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(viewModel.ErrorMessage);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task LibMpvVideoView_DisposesRenderSession_WhenRenderSourceClears()
    {
        await RunOnUiThreadAsync(() =>
        {
            var engine = new RecordingRenderablePlayerEngine();
            var view = new LibMpvVideoView { Engine = engine };

            Assert.True(view.TryCreateSessionForTesting(engine, _ => IntPtr.Zero));
            var session = Assert.Single(engine.Sessions);
            Assert.False(session.IsDisposed);

            view.Engine = null;

            Assert.True(session.IsDisposed);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LibMpvVideoView_ReportsRenderFailure_WhenSessionCreationFails()
    {
        await RunOnUiThreadAsync(() =>
        {
            var engine = new RecordingRenderablePlayerEngine { ThrowOnCreateSession = true };
            var view = new LibMpvVideoView { Engine = engine };

            Assert.False(view.TryCreateSessionForTesting(engine, _ => IntPtr.Zero));

            var failure = Assert.IsType<InvalidOperationException>(engine.ReportedRenderFailure);
            Assert.Equal("render session failure", failure.Message);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task KeyboardShortcuts_SearchAndPlayerCommandsAreDeterministic()
    {
        var engineFactory = new RecordingPlayerEngineFactory();
        var viewModel = CreateViewModel(engineFactory: engineFactory);

        Assert.True(viewModel.CanHandleShortcut(Key.F, KeyModifiers.Control));
        Assert.False(viewModel.CanHandleShortcut(Key.Space, KeyModifiers.None));
        Assert.True(await viewModel.HandleShortcutAsync(Key.F, KeyModifiers.Control));
        Assert.Equal(DesktopRouteKind.Search, viewModel.CurrentRoute.Kind);

        await viewModel.NavigateAsync(DesktopRoute.Details("fixture-0001"));
        var details = Assert.IsType<DetailsPageViewModel>(viewModel.CurrentPage);
        var playCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(details.Streams[0].PlayCommand);
        await playCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanHandleShortcut(Key.Space, KeyModifiers.None));
        Assert.True(viewModel.CanHandleShortcut(Key.Left, KeyModifiers.None));
        Assert.True(viewModel.CanHandleShortcut(Key.Right, KeyModifiers.None));
        Assert.True(viewModel.CanHandleShortcut(Key.Escape, KeyModifiers.None));
        Assert.True(await viewModel.HandleShortcutAsync(Key.Space, KeyModifiers.None));
        Assert.True(await viewModel.HandleShortcutAsync(Key.Left, KeyModifiers.None));
        Assert.True(await viewModel.HandleShortcutAsync(Key.Right, KeyModifiers.None));
        Assert.Contains("Pause", engineFactory.Engine.Calls);
        Assert.Equal(2, engineFactory.Engine.Calls.Count(call => call == "Seek"));

        Assert.True(await viewModel.HandleShortcutAsync(Key.Escape, KeyModifiers.None));
        Assert.Equal(DesktopRouteKind.Details, viewModel.CurrentRoute.Kind);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task PlatformShortcutGestures_UseControlOnWindowsAndMetaOnMac()
    {
        var windowsViewModel = CreateViewModel();
        Assert.Equal("Ctrl+F", windowsViewModel.SearchShortcutLabel);
        Assert.Equal(Key.F, windowsViewModel.SearchKeyGesture.Key);
        Assert.Equal(KeyModifiers.Control, windowsViewModel.SearchKeyGesture.KeyModifiers);

        var macViewModel = CreateViewModel(platformInfo: new PlatformInfo(
            PlatformFamily.MacOS,
            "osx-arm64",
            "macOS test platform"));
        Assert.Equal("Cmd+F", macViewModel.SearchShortcutLabel);
        Assert.Equal(Key.F, macViewModel.SearchKeyGesture.Key);
        Assert.Equal(KeyModifiers.Meta, macViewModel.SearchKeyGesture.KeyModifiers);
        Assert.True(await macViewModel.HandleShortcutAsync(Key.F, KeyModifiers.Meta));
        Assert.Equal(DesktopRouteKind.Search, macViewModel.CurrentRoute.Kind);
    }

    [Fact]
    public async Task MainWindow_RendersHomeSearchDetailsAndPlayerRoutes()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = CreateViewModel(engineFactory: new RecordingPlayerEngineFactory());
            var window = new MainWindow
            {
                DataContext = viewModel
            };

            try
            {
                window.Show();
                await viewModel.NavigateAsync(DesktopRoute.Home);
                window.UpdateLayout();
                Assert.NotNull(window.FindControl<ContentControl>("RouteHost"));
                Assert.NotNull(window.FindControl<TextBlock>("PlatformNameText"));
                Assert.NotNull(window.FindControl<TextBlock>("MpvStatusText"));

                await viewModel.NavigateAsync(DesktopRoute.Search);
                window.UpdateLayout();
                Assert.NotNull(FindVisualDescendant<TextBox>(window, "SearchBox"));

                await viewModel.NavigateAsync(DesktopRoute.Details("fixture-0001"));
                window.UpdateLayout();
                Assert.NotNull(FindVisualDescendant<ItemsControl>(window, "StreamRows"));

                await viewModel.NavigateAsync(DesktopRoute.Player);
                window.UpdateLayout();
                Assert.NotNull(FindVisualDescendant<Button>(window, "PlayPauseButton"));
            }
            finally
            {
                await CloseHeadlessWindowAsync(window, viewModel);
            }
        });
    }

    [Fact]
    public async Task CatalogRoute_VirtualizesThousandItemPosterGrid()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = CreateViewModel(fixtures: new DesktopFixtureService(itemCount: 1_000));
            var window = new MainWindow
            {
                Width = 1180,
                Height = 760,
                DataContext = viewModel
            };

            try
            {
                window.Show();
                await viewModel.NavigateAsync(DesktopRoute.Catalog);
                window.UpdateLayout();

                Assert.NotNull(FindVisualDescendant<ListBox>(window, "CatalogRowsList"));
                Assert.Contains(window.GetVisualDescendants(), control => control is VirtualizingStackPanel);

                var realizedPosterCards = window.GetVisualDescendants()
                    .OfType<Button>()
                    .Count(button => button.Name == "PosterCard");

                Assert.InRange(realizedPosterCards, 1, 250);
            }
            finally
            {
                await CloseHeadlessWindowAsync(window, viewModel);
            }
        });
    }

    [Fact]
    public async Task MainWindowViewModel_AddonsPageReceivesDiagnostics()
    {
        var diagnostics = new NetworkDiagnostics();
        diagnostics.Record(new NetworkDiagnosticEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Kind: NetworkEventKind.Failure,
            Host: "addons.example.test",
            StatusCode: 500,
            DurationMs: 25,
            AddonId: "addon.test",
            ResourceKind: "manifest",
            Message: "manifest failed"));
        var viewModel = new MainWindowViewModel(
            new PlatformInfo(PlatformFamily.Windows, "win-x64", "Windows test platform"),
            MpvDiscoveryResult.NotFound("mpv not supplied"),
            new EmptyCatalogDataSource(),
            new NoopAddonService(),
            new RecordingPlayerEngineFactory(),
            addonDiagnostics: diagnostics);

        await viewModel.NavigateAsync(DesktopRoute.Addons);

        var addonsPage = Assert.IsType<AddonsPageViewModel>(viewModel.CurrentPage);
        Assert.Single(addonsPage.RecentEvents);
        Assert.Equal("addons.example.test", addonsPage.RecentEvents[0].Host);
        await viewModel.DisposeAsync();
    }

    private static MainWindowViewModel CreateViewModel(
        IDesktopFixtureService? fixtures = null,
        IPlayerEngineFactory? engineFactory = null,
        PlatformInfo? platformInfo = null)
    {
        var mpvDiscovery = MpvDiscoveryResult.Found(
            @"C:\mpv\mpv.exe",
            MpvInstallationSource.CommonLocation,
            "mpv 0.41.0");

        return MainWindowViewModel.CreateFixture(
            platformInfo: platformInfo ?? new PlatformInfo(
                PlatformFamily.Windows,
                "win-x64",
                "Windows test platform"),
            mpvDiscovery: mpvDiscovery,
            fixtures: fixtures ?? new DesktopFixtureService(),
            playerEngineFactory: engineFactory ?? new RecordingPlayerEngineFactory());
    }

    private static StreamSource CreateStreamSource()
    {
        return new StreamSource(
            "fixture-stream",
            new Uri("https://cdn.example.test/video.m3u8"),
            "Fixture 1080p",
            "1080p",
            new Dictionary<string, string>(),
            Array.Empty<SubtitleTrack>(),
            IsUserProvided: false);
    }

    private static MediaDetails CreateMediaDetails()
    {
        return new MediaDetails(
            "fixture-0001",
            "movie",
            "Fixture Movie",
            null,
            null,
            null,
            "Fixture metadata",
            "2026",
            "112 min",
            Array.Empty<string>(),
            Array.Empty<MediaExternalRating>(),
            Array.Empty<MediaPerson>(),
            Array.Empty<MediaCompany>(),
            Array.Empty<MediaTrailer>(),
            Array.Empty<MediaLink>(),
            Array.Empty<MediaVideo>());
    }

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

    private static async Task WaitForConditionAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private static async Task CloseHeadlessWindowAsync(MainWindow window, MainWindowViewModel viewModel)
    {
        window.DataContext = null;
        window.Close();
        await viewModel.DisposeAsync();
    }

    private static T? FindVisualDescendant<T>(Control root, string name)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => control.Name == name);
    }

    private sealed class SlowSearchFixtureService : IDesktopFixtureService
    {
        private readonly DesktopFixtureService _inner = new(itemCount: 4);

        public TaskCompletionSource FirstSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken FirstSearchToken { get; private set; }

        public Task<IReadOnlyList<FixtureHomeSection>> GetHomeSectionsAsync(CancellationToken cancellationToken) =>
            _inner.GetHomeSectionsAsync(cancellationToken);

        public Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(CancellationToken cancellationToken) =>
            _inner.GetCatalogAsync(cancellationToken);

        public async Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (query.Equals("first", StringComparison.OrdinalIgnoreCase))
            {
                FirstSearchToken = cancellationToken;
                FirstSearchStarted.SetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return [new CatalogItem(
                $"search-{query}",
                "movie",
                $"Result for {query}",
                new Uri("https://images.example.test/search.jpg"),
                null,
                "2026")];
        }

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, CancellationToken cancellationToken) =>
            _inner.GetDetailsAsync(mediaId, cancellationToken);
    }

    private sealed class EmptyCatalogDataSource : ICatalogDataSource
    {
        public string ModeLabel => "empty";

        public Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DesktopHomeRail>>(Array.Empty<DesktopHomeRail>());

        public Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken) =>
            Task.FromResult(new DesktopCatalogPage(Array.Empty<CatalogItem>(), HasMore: false, NextSkip: 0));

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class NoopAddonService : IAddonService
    {
        public Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedAddon>>(Array.Empty<ManagedAddon>());

        public Task<ManagedAddon> InstallAsync(string rawManifestUrl, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<ManagedAddon> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");

        public Task<ManagedAddon> RefreshAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");

        public Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingPlayerEngineFactory : IPlayerEngineFactory
    {
        public RecordingPlayerEngine Engine { get; } = new();

        public List<RecordingPlayerEngine> CreatedEngines { get; } = [];

        public IPlayerEngine Create(PlayerOptions options)
        {
            CreatedEngines.Add(Engine);
            return Engine;
        }
    }

    private class RecordingPlayerEngine : IPlayerEngine
    {
        private readonly Channel<PlayerEvent> _events = Channel.CreateUnbounded<PlayerEvent>();

        public List<string> Calls { get; } = [];

        public StreamSource? LoadedSource { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken)
        {
            Calls.Add("Initialize");
            WriteEvent(new PlayerEvent.AvailabilityChanged(true, "mpv test", DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task LoadAsync(StreamSource source, CancellationToken cancellationToken)
        {
            Calls.Add("Load");
            LoadedSource = source;
            WriteEvent(new PlayerEvent.FileLoaded(DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task PlayAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Play");
            WriteEvent(new PlayerEvent.PlaybackStateChanged(true, DateTimeOffset.UtcNow));
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

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken)
        {
            Calls.Add("Volume");
            return Task.CompletedTask;
        }

        public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken)
        {
            Calls.Add("Fullscreen");
            return Task.CompletedTask;
        }

        public Task SelectAudioTrackAsync(string trackId, CancellationToken cancellationToken)
        {
            Calls.Add("AudioTrack");
            return Task.CompletedTask;
        }

        public Task SelectSubtitleTrackAsync(string? trackId, CancellationToken cancellationToken)
        {
            Calls.Add("SubtitleTrack");
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<PlayerEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var playerEvent in _events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return playerEvent;
            }
        }

        public void WriteEvent(PlayerEvent playerEvent)
        {
            _events.Writer.TryWrite(playerEvent);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRenderablePlayerEngineFactory : IPlayerEngineFactory
    {
        public RecordingRenderablePlayerEngine Engine { get; } = new();

        public IPlayerEngine Create(PlayerOptions options) => Engine;
    }

    private sealed class RecordingRenderablePlayerEngine : RecordingPlayerEngine, IPlayerRenderSource
    {
        public List<RecordingRenderSession> Sessions { get; } = [];

        public bool ThrowOnCreateSession { get; init; }

        public Exception? ReportedRenderFailure { get; private set; }

        public event EventHandler<PlayerRenderFailureEventArgs>? RenderFailed;

        public IPlayerRenderSession CreateRenderSession(Func<string, IntPtr> getProcAddress, Action onUpdate)
        {
            if (ThrowOnCreateSession)
            {
                throw new InvalidOperationException("render session failure");
            }

            var session = new RecordingRenderSession();
            Sessions.Add(session);
            return session;
        }

        public void ReportRenderFailure(Exception exception)
        {
            ReportedRenderFailure = exception;
            RenderFailed?.Invoke(this, new PlayerRenderFailureEventArgs(exception));
        }
    }

    private sealed class RenderFailureFallbackPlayerEngineFactory : IPlayerEngineFactory
    {
        public RecordingRenderablePlayerEngine RenderEngine { get; } = new();

        public RecordingPlayerEngine ExternalEngine { get; } = new();

        public IPlayerEngine Create(PlayerOptions options) =>
            string.Equals(options.PreferredEngine, SelectingPlayerEngineFactory.LibMpvEngineId, StringComparison.Ordinal)
                ? RenderEngine
                : ExternalEngine;
    }

    private sealed class RecordingRenderSession : IPlayerRenderSession
    {
        public bool IsDisposed { get; private set; }

        public bool Update() => true;

        public void Render(int framebufferObject, int width, int height)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
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

    private sealed class LoadFailingPlayerEngineFactory : IPlayerEngineFactory
    {
        public LoadFailingPlayerEngine Engine { get; } = new();

        public IPlayerEngine Create(PlayerOptions options) => Engine;
    }

    private sealed class LoadFailingPlayerEngine : IPlayerEngine
    {
        private readonly Channel<PlayerEvent> _events = Channel.CreateUnbounded<PlayerEvent>();

        public bool IsDisposed { get; private set; }

        public Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken)
        {
            _events.Writer.TryWrite(new PlayerEvent.AvailabilityChanged(true, "mpv test", DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task LoadAsync(StreamSource source, CancellationToken cancellationToken)
        {
            throw new IOException("fixture load failure");
        }

        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken) => Task.CompletedTask;

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
            IsDisposed = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
