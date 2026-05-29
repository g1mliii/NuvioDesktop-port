using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Settings;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class SettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly ICacheMaintenanceService _cacheMaintenance;
    private readonly DecodedImageMemoryCache? _decodedImageMemoryCache;
    private ThemeModeOption _selectedTheme;
    private PlayerModeOption _selectedPlayerMode;
    private bool _hardwareDecodingEnabled;
    private int _initialVolume;
    private decimal _imageDiskCacheLimitMb;
    private int _decodedImageMemoryItemLimit;
    private bool _isBusy;
    private bool _isLoaded;
    private string _statusMessage = "Settings ready";

    public SettingsPageViewModel(
        ISettingsStore settingsStore,
        ICacheMaintenanceService cacheMaintenance,
        DecodedImageMemoryCache? decodedImageMemoryCache = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _cacheMaintenance = cacheMaintenance ?? throw new ArgumentNullException(nameof(cacheMaintenance));
        _decodedImageMemoryCache = decodedImageMemoryCache;
        ThemeOptions =
        [
            new ThemeModeOption("System", ThemeMode.System),
            new ThemeModeOption("Light", ThemeMode.Light),
            new ThemeModeOption("Dark", ThemeMode.Dark)
        ];
        PlayerModeOptions =
        [
            new PlayerModeOption("External mpv", PlayerMode.ExternalMpv)
        ];
        _selectedTheme = ThemeOptions[0];
        _selectedPlayerMode = PlayerModeOptions[0];
        _hardwareDecodingEnabled = DesktopSettings.Default.HardwareDecodingEnabled;
        _initialVolume = DesktopSettings.Default.InitialVolume;
        _imageDiskCacheLimitMb = BytesToMegabytes(DesktopSettings.Default.ImageDiskCacheLimitBytes);
        _decodedImageMemoryItemLimit = DesktopSettings.Default.DecodedImageMemoryItemLimit;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsBusy);
    }

    public IReadOnlyList<ThemeModeOption> ThemeOptions { get; }

    public IReadOnlyList<PlayerModeOption> PlayerModeOptions { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ClearCacheCommand { get; }

    public ThemeModeOption SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, value);
    }

    public PlayerModeOption SelectedPlayerMode
    {
        get => _selectedPlayerMode;
        set => SetProperty(ref _selectedPlayerMode, value);
    }

    public bool HardwareDecodingEnabled
    {
        get => _hardwareDecodingEnabled;
        set => SetProperty(ref _hardwareDecodingEnabled, value);
    }

    public int InitialVolume
    {
        get => _initialVolume;
        set => SetProperty(ref _initialVolume, Math.Clamp(value, 0, 100));
    }

    public decimal ImageDiskCacheLimitMb
    {
        get => _imageDiskCacheLimitMb;
        set => SetProperty(ref _imageDiskCacheLimitMb, Math.Clamp(value, 64m, 500m));
    }

    public int DecodedImageMemoryItemLimit
    {
        get => _decodedImageMemoryItemLimit;
        set => SetProperty(ref _decodedImageMemoryItemLimit, Math.Clamp(value, 16, 512));
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
                ClearCacheCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await _settingsStore.LoadAsync(cancellationToken));
            _isLoaded = true;
            StatusMessage = "Settings loaded";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var settings = BuildSettings();
            await _settingsStore.SaveAsync(settings, CancellationToken.None);
            _decodedImageMemoryCache?.SetLimit(settings.DecodedImageMemoryItemLimit);
            await _cacheMaintenance.UpdateDiskCacheLimitAsync(settings.ImageDiskCacheLimitBytes, CancellationToken.None);
            StatusMessage = "Settings saved";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearCacheAsync()
    {
        IsBusy = true;
        try
        {
            await _cacheMaintenance.ClearCacheAsync(CancellationToken.None);
            StatusMessage = "Cache cleared; settings, addons, and watch progress were kept";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(DesktopSettings settings)
    {
        settings = settings.Normalize();
        SelectedTheme = ThemeOptions.First(option => option.Value == settings.Theme);
        SelectedPlayerMode = PlayerModeOptions[0];
        HardwareDecodingEnabled = settings.HardwareDecodingEnabled;
        InitialVolume = settings.InitialVolume;
        ImageDiskCacheLimitMb = BytesToMegabytes(settings.ImageDiskCacheLimitBytes);
        DecodedImageMemoryItemLimit = settings.DecodedImageMemoryItemLimit;
    }

    private DesktopSettings BuildSettings() =>
        new DesktopSettings(
            SelectedTheme.Value,
            SelectedPlayerMode.Value,
            InitialVolume,
            HardwareDecodingEnabled,
            MegabytesToBytes(ImageDiskCacheLimitMb),
            DecodedImageMemoryItemLimit,
            DesktopSettings.DefaultMetadataCacheTtl).Normalize();

    private static decimal BytesToMegabytes(long bytes) =>
        decimal.Round(bytes / 1024m / 1024m, 0, MidpointRounding.AwayFromZero);

    private static long MegabytesToBytes(decimal megabytes) =>
        Convert.ToInt64(megabytes * 1024m * 1024m);
}

public sealed record ThemeModeOption(string Label, ThemeMode Value);

public sealed record PlayerModeOption(string Label, PlayerMode Value);
