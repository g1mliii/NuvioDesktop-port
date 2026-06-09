using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Core.Progress;
using Nuvio.Core.Services;
using Nuvio.Core.Settings;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Platform;

namespace Nuvio.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly PlatformInfo _platformInfo;
    private readonly Stack<DesktopRoute> _backStack = new();
    private readonly HomePageViewModel _homePage;
    private readonly SearchPageViewModel _searchPage;
    private readonly CatalogPageViewModel _catalogPage;
    private readonly DetailsPageViewModel _detailsPage;
    private readonly ViewModelBase _addonsPage;
    private readonly ViewModelBase _settingsPage;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly IWatchProgressRepository? _progressRepository;
    private CancellationTokenSource? _navigationCancellation;
    private ViewModelBase _currentPage;
    private DesktopRoute _currentRoute = DesktopRoute.Home;
    private int _isDisposed;
    private string _mpvStatus = string.Empty;
    private string _mpvPath = string.Empty;
    private string _mpvVersion = string.Empty;
    private string _mpvSource = string.Empty;
    private string _mpvMessage = string.Empty;
    private string _statusMessage = "Fixture shell ready";
    private DesktopLayoutMode _layoutMode = DesktopLayoutMode.Normal;
    private bool _tvFocusMode;
    private bool _isPlayerFullscreen;

    // Width breakpoints (DIPs). Below Compact collapses the nav rail; above Wide enlarges cards/targets.
    internal const double CompactWidthThreshold = 1000d;
    internal const double WideWidthThreshold = 1400d;
    private const double DetailsPosterLayoutWidth = 180d;
    private const double ContentHorizontalMargin = 52d;

    public static MainWindowViewModel CreateFixture(
        PlatformInfo? platformInfo = null,
        MpvDiscoveryResult? mpvDiscovery = null,
        IDesktopFixtureService? fixtures = null,
        IPlayerEngineFactory? playerEngineFactory = null) =>
        new(
            platformInfo ?? PlatformInfoProvider.Current(),
            mpvDiscovery ?? MpvDiscoveryResult.NotFound("Checking for mpv without blocking app startup."),
            new FixtureCatalogDataSource(fixtures ?? new DesktopFixtureService()),
            addonService: null,
            playerEngineFactory ?? new ExternalMpvPlayerEngineFactory());

    private readonly IAsyncDisposable? _servicesOwner;

    public MainWindowViewModel(
        PlatformInfo platformInfo,
        MpvDiscoveryResult mpvDiscovery,
        ICatalogDataSource dataSource,
        IAddonService? addonService,
        IPlayerEngineFactory playerEngineFactory,
        IAsyncDisposable? servicesOwner = null,
        INetworkDiagnostics? addonDiagnostics = null,
        ISettingsStore? settingsStore = null,
        ICacheMaintenanceService? cacheMaintenance = null,
        DecodedImageMemoryCache? decodedImageMemoryCache = null,
        IDesktopImageLoader? imageLoader = null,
        IWatchProgressRepository? progressRepository = null,
        IThemeController? themeController = null)
    {
        _servicesOwner = servicesOwner;
        _platformInfo = platformInfo;
        PlatformName = platformInfo.Family.ToString();
        RuntimeIdentifier = platformInfo.RuntimeIdentifier;
        PlatformDescription = platformInfo.Description;

        NavigateCommand = new AsyncRelayCommand<DesktopRoute>(route => NavigateAsync(route ?? DesktopRoute.Home));
        BackCommand = new AsyncRelayCommand(GoBackAsync);
        FocusSearchCommand = new AsyncRelayCommand(() => NavigateAsync(DesktopRoute.Search));
        OpenMediaFileCommand = new RelayCommand(() => OpenMediaFileRequested?.Invoke());
        ToggleFullscreenCommand = new AsyncRelayCommand(ToggleFullscreenAsync);
        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke());

        _progressRepository = progressRepository;
        var progressRecorder = progressRepository is null
            ? null
            : new PlayerProgressRecorder(progressRepository);
        Player = new PlayerViewModel(
            playerEngineFactory,
            GoBackAsync,
            settingsStore: settingsStore,
            progressRecorder: progressRecorder);
        Player.PropertyChanged += OnPlayerPropertyChanged;
        _homePage = new HomePageViewModel(dataSource, OpenDetailsAsync, imageLoader, progressRepository, settingsStore);
        _searchPage = new SearchPageViewModel(dataSource, OpenDetailsAsync, imageLoader);
        _catalogPage = new CatalogPageViewModel(dataSource, OpenDetailsAsync, imageLoader);
        _detailsPage = new DetailsPageViewModel(dataSource, PlayStreamAsync, imageLoader);

        _addonsPage = addonService is null
            ? new PlaceholderPageViewModel(
                "Addons",
                "Live addon management starts in Phase 4. Switch to live mode to install Stremio-compatible addons.")
            : new AddonsPageViewModel(addonService, addonDiagnostics);

        _settingsPage = settingsStore is not null && cacheMaintenance is not null
            ? new SettingsPageViewModel(settingsStore, cacheMaintenance, decodedImageMemoryCache, themeController)
            : new PlaceholderPageViewModel(
                "Settings",
                "Desktop settings will grow from the mobile settings model after the fixture shell is stable.");

        if (_settingsPage is SettingsPageViewModel typedSettingsPage)
        {
            typedSettingsPage.TvFocusModeChanged += OnTvFocusModeChanged;
        }
        _currentPage = _homePage;
        StatusMessage = dataSource.ModeLabel;

        NavigationItems =
        [
            new(DesktopRoute.Home, NavigateCommand),
            new(DesktopRoute.Search, NavigateCommand),
            new(DesktopRoute.Catalog, NavigateCommand),
            new(DesktopRoute.Addons, NavigateCommand),
            new(DesktopRoute.Settings, NavigateCommand)
        ];

        ApplyMpvDiscovery(mpvDiscovery);
        UpdateNavigationSelection();
        _ = NavigateAsync(DesktopRoute.Home, addToBackStack: false);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public IAsyncRelayCommand<DesktopRoute> NavigateCommand { get; }

    public IAsyncRelayCommand BackCommand { get; }

    public IAsyncRelayCommand FocusSearchCommand { get; }

    /// <summary>Asks the view to show a native file picker (handled in code-behind); selection flows back via
    /// <see cref="PlayLocalFileAsync"/>. Exposed as a command so menus/buttons bind to it and it stays testable.</summary>
    public IRelayCommand OpenMediaFileCommand { get; }

    public IAsyncRelayCommand ToggleFullscreenCommand { get; }

    public IRelayCommand QuitCommand { get; }

    /// <summary>Raised when the user invokes "Open media file…"; the view shows the StorageProvider picker.</summary>
    public event Action? OpenMediaFileRequested;

    /// <summary>Raised when the user invokes Quit from the native/app menu; the view closes the window.</summary>
    public event Action? QuitRequested;

    public PlayerViewModel Player { get; }

    public bool IsMacOS => _platformInfo.Family == PlatformFamily.MacOS;

    /// <summary>The in-window menu is used on Windows/Linux; macOS uses a NativeMenu instead. Also hidden while
    /// the player is fullscreen.</summary>
    public bool IsInWindowMenuVisible => IsChromeVisible && !IsMacOS;

    public string AppName { get; } = "Nuvio Desktop";

    public string PhaseStatus { get; } = "Phase 4 live addon shell";

    public string PlatformName { get; }

    public string RuntimeIdentifier { get; }

    public string PlatformDescription { get; }

    public string SearchShortcutLabel => _platformInfo.Family == PlatformFamily.MacOS ? "Cmd+F" : "Ctrl+F";

    public string SearchMenuHeader => $"_Search ({SearchShortcutLabel})";

    public KeyGesture BackKeyGesture { get; } = new(Key.Escape);

    public KeyGesture PlayPauseKeyGesture { get; } = new(Key.Space);

    public KeyGesture SeekBackwardKeyGesture { get; } = new(Key.Left);

    public KeyGesture SeekForwardKeyGesture { get; } = new(Key.Right);

    public KeyGesture SearchKeyGesture => _platformInfo.Family == PlatformFamily.MacOS
        ? new KeyGesture(Key.F, KeyModifiers.Meta)
        : new KeyGesture(Key.F, KeyModifiers.Control);

    public KeyGesture OpenMediaFileKeyGesture => _platformInfo.Family == PlatformFamily.MacOS
        ? new KeyGesture(Key.O, KeyModifiers.Meta)
        : new KeyGesture(Key.O, KeyModifiers.Control);

    public KeyGesture FullscreenKeyGesture { get; } = new(Key.F11);

    public KeyGesture QuitKeyGesture => _platformInfo.Family == PlatformFamily.MacOS
        ? new KeyGesture(Key.Q, KeyModifiers.Meta)
        : new KeyGesture(Key.Q, KeyModifiers.Control);

    public DesktopRoute CurrentRoute
    {
        get => _currentRoute;
        private set
        {
            if (SetProperty(ref _currentRoute, value))
            {
                OnPropertyChanged(nameof(CurrentRouteLabel));
            }
        }
    }

    public string CurrentRouteLabel => CurrentRoute.Label;

    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public bool CanGoBack => _backStack.Count > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string MpvStatus
    {
        get => _mpvStatus;
        private set => SetProperty(ref _mpvStatus, value);
    }

    public string MpvPath
    {
        get => _mpvPath;
        private set => SetProperty(ref _mpvPath, value);
    }

    public string MpvVersion
    {
        get => _mpvVersion;
        private set => SetProperty(ref _mpvVersion, value);
    }

    public string MpvSource
    {
        get => _mpvSource;
        private set => SetProperty(ref _mpvSource, value);
    }

    public string MpvMessage
    {
        get => _mpvMessage;
        private set => SetProperty(ref _mpvMessage, value);
    }

    public string MpvPolicy { get; } = "MPV Manager is setup-only; Nuvio runs mpv directly.";

    public void ApplyMpvDiscovery(MpvDiscoveryResult mpvDiscovery)
    {
        MpvStatus = mpvDiscovery.StatusLabel;
        MpvPath = mpvDiscovery.ExecutablePath ?? "Not detected";
        MpvVersion = mpvDiscovery.Version ?? "Unavailable";
        MpvSource = mpvDiscovery.SourceLabel;
        MpvMessage = mpvDiscovery.Message ?? "Nuvio will launch the detected mpv binary directly through JSON IPC.";
    }

    // ---- Responsive layout (stream C) + TV focus mode (stream H) ----

    public DesktopLayoutMode LayoutMode
    {
        get => _layoutMode;
        private set
        {
            if (SetProperty(ref _layoutMode, value))
            {
                OnPropertyChanged(nameof(IsCompactLayout));
                OnPropertyChanged(nameof(IsSidebarExpanded));
                OnPropertyChanged(nameof(IsTvLayout));
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(PosterCardWidth));
                OnPropertyChanged(nameof(PosterCardHeight));
            }
        }
    }

    public bool TvFocusMode
    {
        get => _tvFocusMode;
        private set
        {
            if (SetProperty(ref _tvFocusMode, value))
            {
                RecomputeLayoutMode();
            }
        }
    }

    /// <summary>True when the nav rail should collapse to an icon-only strip.</summary>
    public bool IsCompactLayout => LayoutMode == DesktopLayoutMode.Compact;

    public bool IsSidebarExpanded => LayoutMode != DesktopLayoutMode.Compact;

    public bool IsTvLayout => LayoutMode == DesktopLayoutMode.Tv;

    public double SidebarWidth => LayoutMode switch
    {
        DesktopLayoutMode.Compact => 64d,
        DesktopLayoutMode.Tv => 260d,
        _ => 220d
    };

    public double PosterCardWidth => LayoutMode switch
    {
        DesktopLayoutMode.Compact => 132d,
        DesktopLayoutMode.Wide => 168d,
        DesktopLayoutMode.Tv => 188d,
        _ => 150d
    };

    public double PosterCardHeight => LayoutMode switch
    {
        DesktopLayoutMode.Compact => 224d,
        DesktopLayoutMode.Wide => 280d,
        DesktopLayoutMode.Tv => 312d,
        _ => 252d
    };

    private double _lastKnownWidth = 1180d;
    private double _lastKnownRenderScaling = 1d;

    /// <summary>Called by the window's SizeChanged handler to recompute the responsive layout bucket.</summary>
    public void UpdateLayoutForWidth(double width)
    {
        UpdateShellMetrics(width, _lastKnownRenderScaling);
    }

    public void UpdateShellMetrics(double width, double renderScaling)
    {
        if (width > 0)
        {
            _lastKnownWidth = width;
        }

        _lastKnownRenderScaling = renderScaling > 0 ? renderScaling : 1d;
        RecomputeLayoutMode();
        UpdateDetailsImageDecodeContext();
    }

    private void RecomputeLayoutMode()
    {
        if (_tvFocusMode)
        {
            LayoutMode = DesktopLayoutMode.Tv;
            return;
        }

        LayoutMode = _lastKnownWidth < CompactWidthThreshold
            ? DesktopLayoutMode.Compact
            : _lastKnownWidth >= WideWidthThreshold
                ? DesktopLayoutMode.Wide
                : DesktopLayoutMode.Normal;
    }

    private void UpdateDetailsImageDecodeContext()
    {
        var chromeWidth = IsChromeVisible ? SidebarWidth : 0d;
        var contentWidth = Math.Max(ImageDecodeSizing.MinBackdropDecodeWidth, _lastKnownWidth - chromeWidth - ContentHorizontalMargin);
        _detailsPage.UpdateImageDecodeContext(DetailsPosterLayoutWidth, contentWidth, _lastKnownRenderScaling);
    }

    private void OnTvFocusModeChanged(bool enabled) => TvFocusMode = enabled;

    /// <summary>Applies persisted shell preferences (currently TV focus mode) at startup, before the window
    /// shows, so the initial layout matches the saved setting without waiting for the Settings page to load.</summary>
    public void ApplyPersistedShellSettings(DesktopSettings settings)
    {
        TvFocusMode = settings.TvFocusMode;
    }

    // ---- Window-level fullscreen (stream E) ----

    /// <summary>Mirrors <see cref="PlayerViewModel.IsFullscreenIntent"/> at the window level so the shell can
    /// promote to <c>WindowState.FullScreen</c> and hide chrome. Only true while on the player route.</summary>
    public bool IsPlayerFullscreen
    {
        get => _isPlayerFullscreen;
        private set
        {
            if (SetProperty(ref _isPlayerFullscreen, value))
            {
                OnPropertyChanged(nameof(IsChromeVisible));
                OnPropertyChanged(nameof(IsInWindowMenuVisible));
            }
        }
    }

    /// <summary>Inverse of <see cref="IsPlayerFullscreen"/>; chrome (menu, sidebar, top bar, footer) binds to this.</summary>
    public bool IsChromeVisible => !IsPlayerFullscreen;

    public async Task ToggleFullscreenAsync()
    {
        if (CurrentRoute.Kind != DesktopRouteKind.Player)
        {
            return;
        }

        await Player.ToggleFullscreenCommand.ExecuteAsync(null);
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullscreenIntent))
        {
            IsPlayerFullscreen = Player.IsFullscreenIntent && CurrentRoute.Kind == DesktopRouteKind.Player;
        }
    }

    public Task NavigateAsync(DesktopRoute route) => NavigateAsync(route, addToBackStack: true);

    public async Task NavigateAsync(DesktopRoute route, bool addToBackStack)
    {
        if (Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        if (addToBackStack && !RoutesEqual(CurrentRoute, route))
        {
            _backStack.Push(CurrentRoute);
            OnPropertyChanged(nameof(CanGoBack));
        }

        await NavigateInternalAsync(route);
    }

    public async Task GoBackAsync()
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        var route = _backStack.Pop();
        OnPropertyChanged(nameof(CanGoBack));
        await NavigateInternalAsync(route);
    }

    public bool CanHandleShortcut(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F && IsSearchShortcut(modifiers))
        {
            return true;
        }

        if (CurrentRoute.Kind == DesktopRouteKind.Player)
        {
            return key is Key.Space or Key.Left or Key.Right or Key.Escape or Key.F11;
        }

        return key == Key.Escape && CanGoBack;
    }

    public async Task<bool> HandleShortcutAsync(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F && IsSearchShortcut(modifiers))
        {
            await NavigateAsync(DesktopRoute.Search);
            return true;
        }

        if (CurrentRoute.Kind != DesktopRouteKind.Player)
        {
            if (key == Key.Escape && CanGoBack)
            {
                await GoBackAsync();
                return true;
            }

            return false;
        }

        switch (key)
        {
            case Key.Space:
                await Player.TogglePlayPauseCommand.ExecuteAsync(null);
                return true;
            case Key.Left:
                await Player.SeekBackwardCommand.ExecuteAsync(null);
                return true;
            case Key.Right:
                await Player.SeekForwardCommand.ExecuteAsync(null);
                return true;
            case Key.Escape:
                await Player.ExitFullscreenOrReturnAsync();
                return true;
            case Key.F11:
                await Player.ToggleFullscreenCommand.ExecuteAsync(null);
                return true;
        }

        return false;
    }

    private async Task OpenDetailsAsync(CatalogItem item)
    {
        await NavigateAsync(DesktopRoute.Details(item.Id, item.Type));
    }

    private async Task PlayStreamAsync(StreamSource source, MediaDetails details)
    {
        await NavigateAsync(DesktopRoute.Player);
        var resumeFrom = await ResolveResumePositionAsync(details);
        await Player.LoadAndPlayAsync(source, details, _disposeCancellation.Token, resumeFrom);
    }

    /// <summary>Looks up stored watch progress so playback can resume where the user left off. Best-effort:
    /// any failure or a trivial/near-complete position resolves to null (start from the beginning).</summary>
    private async Task<TimeSpan?> ResolveResumePositionAsync(MediaDetails details)
    {
        if (_progressRepository is null)
        {
            return null;
        }

        try
        {
            var progress = await _progressRepository.GetAsync(details.Id, null, _disposeCancellation.Token);
            if (progress is null || progress.Position <= TimeSpan.FromSeconds(5))
            {
                return null;
            }

            // Don't resume an effectively-finished title; let it start over.
            var nearEnd = progress.Percent >= WatchProgressRules.CompletionThresholdFraction * 100d
                || (progress.Duration > TimeSpan.Zero && progress.Duration - progress.Position <= TimeSpan.FromSeconds(30));
            return nearEnd ? null : progress.Position;
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Plays a local file chosen via the native file picker (stream G). The view supplies the path;
    /// playback flows through the same <see cref="IPlayerEngine"/> as addon streams.</summary>
    public async Task PlayLocalFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        var source = new StreamSource(
            $"local:{filePath}",
            CreateLocalFileUri(filePath),
            name,
            null,
            new Dictionary<string, string>(),
            Array.Empty<SubtitleTrack>(),
            IsUserProvided: true);
        var details = new MediaDetails(
            $"local:{filePath}",
            "movie",
            name,
            null,
            null,
            null,
            "Local media file",
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<MediaExternalRating>(),
            Array.Empty<MediaPerson>(),
            Array.Empty<MediaCompany>(),
            Array.Empty<MediaTrailer>(),
            Array.Empty<MediaLink>(),
            Array.Empty<MediaVideo>());

        await PlayStreamAsync(source, details);
    }

    internal static Uri CreateLocalFileUri(string filePath) =>
        new UriBuilder(Uri.UriSchemeFile, string.Empty, -1, filePath).Uri;

    private async Task NavigateInternalAsync(DesktopRoute route)
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var cancellationToken = _navigationCancellation.Token;

        CancelPageOperations(CurrentRoute, route);
        CurrentRoute = route;
        if (route.Kind != DesktopRouteKind.Player)
        {
            // Drive the exit through the player so its fullscreen intent and the window-level flag stay
            // coupled; otherwise returning to the player later would leave the F11 toggle out of sync.
            await Player.ExitFullscreenAsync();
            if (IsPlayerFullscreen)
            {
                IsPlayerFullscreen = false;
            }
        }

        UpdateNavigationSelection();
        StatusMessage = $"Route: {route.Label}";

        try
        {
            switch (route.Kind)
            {
                case DesktopRouteKind.Home:
                    CurrentPage = _homePage;
                    if (!_homePage.IsLoaded)
                    {
                        await _homePage.LoadAsync(cancellationToken);
                    }
                    break;
                case DesktopRouteKind.Search:
                    CurrentPage = _searchPage;
                    break;
                case DesktopRouteKind.Catalog:
                    CurrentPage = _catalogPage;
                    if (!_catalogPage.IsLoaded)
                    {
                        await _catalogPage.LoadAsync(cancellationToken);
                    }
                    break;
                case DesktopRouteKind.Details:
                    CurrentPage = _detailsPage;
                    if (string.IsNullOrWhiteSpace(route.MediaId))
                    {
                        throw new InvalidOperationException("Details route requires a media id.");
                    }

                    UpdateDetailsImageDecodeContext();
                    await _detailsPage.LoadAsync(route.MediaId, route.MediaType, cancellationToken);
                    break;
                case DesktopRouteKind.Player:
                    CurrentPage = Player;
                    break;
                case DesktopRouteKind.Addons:
                    CurrentPage = _addonsPage;
                    if (_addonsPage is AddonsPageViewModel addonsPage)
                    {
                        await addonsPage.LoadAsync(cancellationToken);
                    }
                    break;
                case DesktopRouteKind.Settings:
                    CurrentPage = _settingsPage;
                    if (_settingsPage is SettingsPageViewModel settingsPage)
                    {
                        await settingsPage.LoadAsync(cancellationToken);
                    }

                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void UpdateNavigationSelection()
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Route.Kind == CurrentRoute.Kind;
        }
    }

    private void CancelPageOperations(DesktopRoute currentRoute, DesktopRoute nextRoute)
    {
        if (currentRoute.Kind == nextRoute.Kind)
        {
            return;
        }

        if (currentRoute.Kind == DesktopRouteKind.Search)
        {
            _searchPage.CancelPendingSearch();
        }

        if (currentRoute.Kind == DesktopRouteKind.Catalog)
        {
            _catalogPage.CancelPendingRequests();
        }

        if (currentRoute.Kind == DesktopRouteKind.Addons && _addonsPage is AddonsPageViewModel addonsPage)
        {
            addonsPage.CancelPendingOperations();
        }
    }

    private bool IsSearchShortcut(KeyModifiers modifiers)
    {
        return _platformInfo.Family == PlatformFamily.MacOS
            ? modifiers.HasFlag(KeyModifiers.Meta)
            : modifiers.HasFlag(KeyModifiers.Control);
    }

    private static bool RoutesEqual(DesktopRoute first, DesktopRoute second) =>
        first.Kind == second.Kind &&
        string.Equals(first.MediaId, second.MediaId, StringComparison.Ordinal) &&
        string.Equals(first.MediaType, second.MediaType, StringComparison.Ordinal);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await _disposeCancellation.CancelAsync();
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = null;
        _searchPage.Dispose();
        _catalogPage.Dispose();
        if (_addonsPage is AddonsPageViewModel addonsPage)
        {
            addonsPage.Dispose();
        }

        if (_settingsPage is SettingsPageViewModel typedSettingsPage)
        {
            typedSettingsPage.TvFocusModeChanged -= OnTvFocusModeChanged;
        }

        Player.PropertyChanged -= OnPlayerPropertyChanged;
        await Player.DisposeAsync();
        _disposeCancellation.Dispose();
        if (_servicesOwner is not null)
        {
            await _servicesOwner.DisposeAsync();
        }
    }
}
