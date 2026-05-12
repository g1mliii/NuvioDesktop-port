using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
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
    private readonly PlaceholderPageViewModel _addonsPage;
    private readonly PlaceholderPageViewModel _settingsPage;
    private readonly CancellationTokenSource _disposeCancellation = new();
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

    public MainWindowViewModel()
        : this(
            PlatformInfoProvider.Current(),
            MpvDiscoveryResult.NotFound("Checking for mpv without blocking app startup."),
            new DesktopFixtureService(),
            new ExternalMpvPlayerEngineFactory())
    {
    }

    public MainWindowViewModel(PlatformInfo platformInfo)
        : this(
            platformInfo,
            MpvDiscoveryResult.NotFound("mpv discovery was not supplied."),
            new DesktopFixtureService(),
            new ExternalMpvPlayerEngineFactory())
    {
    }

    public MainWindowViewModel(PlatformInfo platformInfo, MpvDiscoveryResult mpvDiscovery)
        : this(
            platformInfo,
            mpvDiscovery,
            new DesktopFixtureService(),
            new ExternalMpvPlayerEngineFactory())
    {
    }

    public MainWindowViewModel(
        PlatformInfo platformInfo,
        MpvDiscoveryResult mpvDiscovery,
        IDesktopFixtureService fixtures,
        IPlayerEngineFactory playerEngineFactory)
    {
        _platformInfo = platformInfo;
        PlatformName = platformInfo.Family.ToString();
        RuntimeIdentifier = platformInfo.RuntimeIdentifier;
        PlatformDescription = platformInfo.Description;

        NavigateCommand = new AsyncRelayCommand<DesktopRoute>(route => NavigateAsync(route ?? DesktopRoute.Home));
        BackCommand = new AsyncRelayCommand(GoBackAsync);
        FocusSearchCommand = new AsyncRelayCommand(() => NavigateAsync(DesktopRoute.Search));

        Player = new PlayerViewModel(playerEngineFactory, GoBackAsync);
        _homePage = new HomePageViewModel(fixtures, OpenDetailsAsync);
        _searchPage = new SearchPageViewModel(fixtures, OpenDetailsAsync);
        _catalogPage = new CatalogPageViewModel(fixtures, OpenDetailsAsync);
        _detailsPage = new DetailsPageViewModel(fixtures, PlayStreamAsync);
        _addonsPage = new PlaceholderPageViewModel(
            "Addons",
            "Phase 3 keeps addons fixture-only. Live addon install and networking start in Phase 4.");
        _settingsPage = new PlaceholderPageViewModel(
            "Settings",
            "Desktop settings will grow from the mobile settings model after the fixture shell is stable.");
        _currentPage = _homePage;

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

    public PlayerViewModel Player { get; }

    public string AppName { get; } = "Nuvio Desktop";

    public string PhaseStatus { get; } = "Phase 3 fixture desktop shell";

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
            return key is Key.Space or Key.Left or Key.Right or Key.Escape;
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
        }

        return false;
    }

    private async Task OpenDetailsAsync(CatalogItem item)
    {
        await NavigateAsync(DesktopRoute.Details(item.Id));
    }

    private async Task PlayStreamAsync(StreamSource source, MediaDetails details)
    {
        await NavigateAsync(DesktopRoute.Player);
        await Player.LoadAndPlayAsync(source, details, _disposeCancellation.Token);
    }

    private async Task NavigateInternalAsync(DesktopRoute route)
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var cancellationToken = _navigationCancellation.Token;

        CurrentRoute = route;
        UpdateNavigationSelection();
        StatusMessage = $"Route: {route.Label}";

        try
        {
            switch (route.Kind)
            {
                case DesktopRouteKind.Home:
                    CurrentPage = _homePage;
                    await _homePage.LoadAsync(cancellationToken);
                    break;
                case DesktopRouteKind.Search:
                    CurrentPage = _searchPage;
                    break;
                case DesktopRouteKind.Catalog:
                    CurrentPage = _catalogPage;
                    await _catalogPage.LoadAsync(cancellationToken);
                    break;
                case DesktopRouteKind.Details:
                    CurrentPage = _detailsPage;
                    if (string.IsNullOrWhiteSpace(route.MediaId))
                    {
                        throw new InvalidOperationException("Details route requires a media id.");
                    }

                    await _detailsPage.LoadAsync(route.MediaId, cancellationToken);
                    break;
                case DesktopRouteKind.Player:
                    CurrentPage = Player;
                    break;
                case DesktopRouteKind.Addons:
                    CurrentPage = _addonsPage;
                    break;
                case DesktopRouteKind.Settings:
                    CurrentPage = _settingsPage;
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

    private bool IsSearchShortcut(KeyModifiers modifiers)
    {
        return _platformInfo.Family == PlatformFamily.MacOS
            ? modifiers.HasFlag(KeyModifiers.Meta)
            : modifiers.HasFlag(KeyModifiers.Control);
    }

    private static bool RoutesEqual(DesktopRoute first, DesktopRoute second) =>
        first.Kind == second.Kind && string.Equals(first.MediaId, second.MediaId, StringComparison.Ordinal);

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
        await Player.DisposeAsync();
        _disposeCancellation.Dispose();
    }
}
