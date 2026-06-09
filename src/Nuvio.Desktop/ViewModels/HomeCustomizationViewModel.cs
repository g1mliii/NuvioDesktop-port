using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Settings;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

/// <summary>
/// Home customization surface (Phase 11.9 / overlaps Phase 12.8): reorder, enable/disable, rename, and
/// toggle hero sourcing per rail, plus a global hero on/off. Saving persists a <see cref="HomeCatalogSettings"/>
/// snapshot and reloads Home.
/// </summary>
public sealed class HomeCustomizationViewModel : ViewModelBase
{
    private readonly Func<HomeCatalogSettings, CancellationToken, Task> _saveAsync;
    private bool _heroEnabled;
    private bool _isSaving;

    public HomeCustomizationViewModel(
        IReadOnlyList<DesktopHomeRail> rails,
        HomeCatalogSettings settings,
        Func<HomeCatalogSettings, CancellationToken, Task> saveAsync)
    {
        _saveAsync = saveAsync;
        _heroEnabled = settings.HeroEnabled;

        // Present rows in the same order Home renders them (explicit order first, then discovery order).
        var ordered = rails
            .Select((rail, arrival) => (rail, preference: settings.Find(rail.Key), arrival))
            .OrderBy(entry => entry.preference?.Order ?? int.MaxValue)
            .ThenBy(entry => entry.arrival);
        Rows = new ObservableCollection<HomeCustomizationRowViewModel>(
            ordered.Select(entry => new HomeCustomizationRowViewModel(entry.rail, entry.preference)));

        MoveUpCommand = new RelayCommand<HomeCustomizationRowViewModel>(MoveUp);
        MoveDownCommand = new RelayCommand<HomeCustomizationRowViewModel>(MoveDown);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsSaving);
    }

    public ObservableCollection<HomeCustomizationRowViewModel> Rows { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public bool HeroEnabled
    {
        get => _heroEnabled;
        set => SetProperty(ref _heroEnabled, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Builds the snapshot the rows currently describe (row index becomes the persisted order).</summary>
    public HomeCatalogSettings BuildSettings()
    {
        var preferences = Rows
            .Select((row, index) => new HomeCatalogPreference(
                Key: row.Key,
                Order: index,
                Enabled: row.Enabled,
                CustomTitle: row.HasCustomTitle ? row.CustomTitle : null,
                HeroSourceEnabled: row.HeroSourceEnabled))
            .ToList();
        return new HomeCatalogSettings { HeroEnabled = HeroEnabled, Preferences = preferences };
    }

    private void MoveUp(HomeCustomizationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        if (index > 0)
        {
            Rows.Move(index, index - 1);
        }
    }

    private void MoveDown(HomeCustomizationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        if (index >= 0 && index < Rows.Count - 1)
        {
            Rows.Move(index, index + 1);
        }
    }

    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            await _saveAsync(BuildSettings(), CancellationToken.None);
        }
        finally
        {
            IsSaving = false;
        }
    }
}

public sealed class HomeCustomizationRowViewModel : ViewModelBase
{
    private string _customTitle;
    private bool _enabled;
    private bool _heroSourceEnabled;

    public HomeCustomizationRowViewModel(DesktopHomeRail rail, HomeCatalogPreference? preference)
    {
        Key = rail.Key;
        DefaultTitle = rail.Title;
        _customTitle = preference?.CustomTitle ?? rail.Title;
        _enabled = preference?.Enabled ?? true;
        _heroSourceEnabled = preference?.HeroSourceEnabled ?? true;
    }

    public string Key { get; }

    public string DefaultTitle { get; }

    public string CustomTitle
    {
        get => _customTitle;
        set => SetProperty(ref _customTitle, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public bool HeroSourceEnabled
    {
        get => _heroSourceEnabled;
        set => SetProperty(ref _heroSourceEnabled, value);
    }

    public bool HasCustomTitle =>
        !string.IsNullOrWhiteSpace(CustomTitle) &&
        !string.Equals(CustomTitle, DefaultTitle, StringComparison.Ordinal);
}
