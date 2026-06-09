using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Core.Progress;
using Nuvio.Core.Services;
using Nuvio.Core.Settings;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public enum HomeEmptyState
{
    None,
    NoAddons,
    AllFailed,
}

public sealed class HomePageViewModel : ViewModelBase
{
    private const int ContinueWatchingLimit = 20;

    private readonly ICatalogDataSource _dataSource;
    private readonly Func<CatalogItem, Task> _openDetailsAsync;
    private readonly IAsyncRelayCommand<CatalogItem> _openDetailsCommand;
    private readonly IDesktopImageLoader? _imageLoader;
    private readonly IWatchProgressRepository? _progressRepository;
    private readonly ISettingsStore? _settingsStore;

    private readonly List<DesktopHomeRail> _loadedRails = [];
    private readonly List<(int Order, int Arrival)> _railSortKeys = [];
    private HomeCatalogSettings _homeCatalogSettings = HomeCatalogSettings.Default;
    private bool _hasContinueWatching;
    private int _railArrivalCounter;
    private bool _isLoading;
    private bool _loaded;
    private string _errorMessage = string.Empty;
    private HomeEmptyState _emptyState = HomeEmptyState.None;
    private HomeHeroViewModel? _hero;
    private HomeCustomizationViewModel? _customization;

    public HomePageViewModel(
        ICatalogDataSource dataSource,
        Func<CatalogItem, Task> openDetailsAsync,
        IDesktopImageLoader? imageLoader = null,
        IWatchProgressRepository? progressRepository = null,
        ISettingsStore? settingsStore = null)
    {
        _dataSource = dataSource;
        _openDetailsAsync = openDetailsAsync;
        _openDetailsCommand = PosterGridBuilder.CreateOpenCommand(openDetailsAsync);
        _imageLoader = imageLoader;
        _progressRepository = progressRepository;
        _settingsStore = settingsStore;
        OpenCustomizationCommand = new RelayCommand(OpenCustomization, () => _loaded && _settingsStore is not null);
        CloseCustomizationCommand = new RelayCommand(() => Customization = null);
    }

    public ObservableCollection<HomeSectionViewModel> Sections { get; } = [];

    public string ModeLabel => _dataSource.ModeLabel;

    public bool IsFixtureMode => string.Equals(_dataSource.ModeLabel, "Fixture data", StringComparison.OrdinalIgnoreCase);

    public IRelayCommand OpenCustomizationCommand { get; }

    public IRelayCommand CloseCustomizationCommand { get; }

    public HomeHeroViewModel? Hero
    {
        get => _hero;
        private set
        {
            if (SetProperty(ref _hero, value))
            {
                OnPropertyChanged(nameof(HasHero));
            }
        }
    }

    public bool HasHero => Hero is { HasItems: true };

    public HomeCustomizationViewModel? Customization
    {
        get => _customization;
        private set
        {
            if (SetProperty(ref _customization, value))
            {
                OnPropertyChanged(nameof(IsCustomizing));
            }
        }
    }

    public bool IsCustomizing => Customization is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public HomeEmptyState EmptyState
    {
        get => _emptyState;
        private set
        {
            if (SetProperty(ref _emptyState, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(EmptyMessage));
                OnPropertyChanged(nameof(EmptyHint));
            }
        }
    }

    public bool IsEmpty => !IsLoading && EmptyState != HomeEmptyState.None && !HasError;

    public string EmptyMessage => EmptyState switch
    {
        HomeEmptyState.NoAddons => "No catalog addons installed",
        HomeEmptyState.AllFailed => "No home rows available",
        _ => string.Empty,
    };

    public string EmptyHint => EmptyState switch
    {
        HomeEmptyState.NoAddons =>
            "Install and enable a catalog addon (for example Cinemeta) from the Addons page to populate Home.",
        HomeEmptyState.AllFailed =>
            "Your installed addons returned no rows. Check that they are enabled and that your network is reachable, then retry.",
        _ => string.Empty,
    };

    public bool IsLoaded => _loaded && !HasError;

    /// <summary>The rails loaded on the most recent refresh, regardless of enabled/disabled state — used to
    /// build the customization surface.</summary>
    public IReadOnlyList<DesktopHomeRail> LoadedRails => _loadedRails;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        EmptyState = HomeEmptyState.None;
        Sections.Clear();
        _loadedRails.Clear();
        _railSortKeys.Clear();
        _hasContinueWatching = false;
        _railArrivalCounter = 0;
        Hero = null;
        Customization = null;
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(IsFixtureMode));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
        OpenCustomizationCommand.NotifyCanExecuteChanged();

        try
        {
            _homeCatalogSettings = _settingsStore is null
                ? HomeCatalogSettings.Default
                : (await _settingsStore.LoadHomeCatalogSettingsAsync(cancellationToken)).Normalize();

            await AddContinueWatchingSectionAsync(cancellationToken);

            var heroItems = new List<CatalogItem>();
            await foreach (var rail in _dataSource.StreamHomeRailsAsync(cancellationToken).ConfigureAwait(true))
            {
                _loadedRails.Add(rail);
                PublishRail(rail, heroItems);
            }

            BuildHero(heroItems, cancellationToken);

            if (RailSectionCount == 0 && !_hasContinueWatching)
            {
                var hasAddons = await _dataSource.HasCatalogCapableAddonsAsync(cancellationToken);
                EmptyState = hasAddons ? HomeEmptyState.AllFailed : HomeEmptyState.NoAddons;
            }

            _loaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsLoaded));
            OpenCustomizationCommand.NotifyCanExecuteChanged();
        }
    }

    private int RailSectionCount => _railSortKeys.Count;

    private void PublishRail(DesktopHomeRail rail, List<CatalogItem> heroItems)
    {
        var preference = _homeCatalogSettings.Find(rail.Key);
        if (preference is { Enabled: false })
        {
            return;
        }

        var title = !string.IsNullOrWhiteSpace(preference?.CustomTitle) ? preference!.CustomTitle! : rail.Title;
        var section = new HomeSectionViewModel(
            title,
            PosterGridBuilder.BuildCards(rail.Items, _openDetailsCommand, _imageLoader));

        var sortKey = (Order: preference?.Order ?? int.MaxValue, Arrival: _railArrivalCounter++);
        InsertRailSection(section, sortKey);

        // Per-rail hero source gating: items only feed the hero when the rail opts in (default true).
        if (preference?.HeroSourceEnabled ?? true)
        {
            heroItems.AddRange(rail.Items);
        }
    }

    private void InsertRailSection(HomeSectionViewModel section, (int Order, int Arrival) sortKey)
    {
        var railBaseIndex = _hasContinueWatching ? 1 : 0;
        var insertOffset = _railSortKeys.Count;
        for (var i = 0; i < _railSortKeys.Count; i++)
        {
            if (CompareSortKeys(sortKey, _railSortKeys[i]) < 0)
            {
                insertOffset = i;
                break;
            }
        }

        Sections.Insert(railBaseIndex + insertOffset, section);
        _railSortKeys.Insert(insertOffset, sortKey);
    }

    private static int CompareSortKeys((int Order, int Arrival) left, (int Order, int Arrival) right)
    {
        var byOrder = left.Order.CompareTo(right.Order);
        return byOrder != 0 ? byOrder : left.Arrival.CompareTo(right.Arrival);
    }

    private async Task AddContinueWatchingSectionAsync(CancellationToken cancellationToken)
    {
        if (_progressRepository is null)
        {
            return;
        }

        IReadOnlyList<WatchProgress> recent;
        try
        {
            recent = await _progressRepository.RecentAsync(ContinueWatchingLimit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return;
        }

        var cards = new List<PosterCardViewModel>();
        foreach (var entry in recent)
        {
            if (IsEffectivelyCompleted(entry))
            {
                continue;
            }

            var item = new CatalogItem(
                Id: entry.MediaId,
                Type: string.IsNullOrWhiteSpace(entry.MediaType) ? "movie" : entry.MediaType!,
                Name: string.IsNullOrWhiteSpace(entry.Title) ? entry.MediaId : entry.Title!,
                PosterUrl: entry.PosterUrl,
                BackgroundUrl: entry.BackgroundUrl,
                ReleaseInfo: null);
            cards.Add(new PosterCardViewModel(item, _openDetailsCommand, _imageLoader, ProgressFraction(entry)));
        }

        if (cards.Count == 0)
        {
            return;
        }

        Sections.Insert(0, new HomeSectionViewModel("Continue Watching", cards));
        _hasContinueWatching = true;
    }

    private void BuildHero(IReadOnlyList<CatalogItem> railItems, CancellationToken cancellationToken)
    {
        if (!_homeCatalogSettings.HeroEnabled || railItems.Count == 0)
        {
            Hero = null;
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<CatalogItem>();
        foreach (var item in railItems)
        {
            if (seen.Add($"{item.Type}:{item.Id}"))
            {
                distinct.Add(item);
            }
        }

        var shuffled = SeededShuffle(distinct, StableSeed(_loadedRails))
            .Take(HomeRailDefaults.HeroItemLimit)
            .ToArray();
        Hero = shuffled.Length == 0
            ? null
            : new HomeHeroViewModel(shuffled, _openDetailsAsync, _imageLoader, cancellationToken);
    }

    private void OpenCustomization()
    {
        if (_settingsStore is null)
        {
            return;
        }

        Customization = new HomeCustomizationViewModel(
            _loadedRails,
            _homeCatalogSettings,
            SaveCustomizationAsync);
    }

    private async Task SaveCustomizationAsync(HomeCatalogSettings settings, CancellationToken cancellationToken)
    {
        if (_settingsStore is not null)
        {
            await _settingsStore.SaveHomeCatalogSettingsAsync(settings, cancellationToken);
        }

        Customization = null;
        _loaded = false;
        await LoadAsync(cancellationToken);
    }

    private static bool IsEffectivelyCompleted(WatchProgress entry)
    {
        if (entry.Percent >= WatchProgressRules.CompletionThresholdFraction * 100d)
        {
            return true;
        }

        return entry.Duration > TimeSpan.Zero && (entry.Duration - entry.Position) <= TimeSpan.FromSeconds(30);
    }

    private static double ProgressFraction(WatchProgress entry)
    {
        if (entry.Duration > TimeSpan.Zero)
        {
            return Math.Clamp(entry.Position.TotalMilliseconds / entry.Duration.TotalMilliseconds, 0d, 1d);
        }

        return Math.Clamp(entry.Percent / 100d, 0d, 1d);
    }

    private static List<T> SeededShuffle<T>(IReadOnlyList<T> items, int seed)
    {
        var list = items.ToList();
        var rng = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    // Deterministic, process-stable seed (string.GetHashCode is randomized per process) so the hero
    // sample is stable across refreshes with the same rails. FNV-1a over the ordered rail keys.
    private static int StableSeed(IReadOnlyList<DesktopHomeRail> rails)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var rail in rails)
            {
                foreach (var ch in rail.Key)
                {
                    hash = (hash ^ ch) * 16777619u;
                }

                hash = (hash ^ (byte)';') * 16777619u;
            }

            return (int)hash;
        }
    }
}
