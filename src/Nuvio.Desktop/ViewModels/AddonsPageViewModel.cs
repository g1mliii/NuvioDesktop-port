using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class AddonsPageViewModel : ViewModelBase, IDisposable
{
    private readonly IAddonService _addonService;
    private readonly INetworkDiagnostics? _diagnostics;
    private CancellationTokenSource? _operationCancellation;
    private string _manifestUrlInput = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private int _isDisposed;

    public AddonsPageViewModel(IAddonService addonService, INetworkDiagnostics? diagnostics = null)
    {
        _addonService = addonService ?? throw new ArgumentNullException(nameof(addonService));
        _diagnostics = diagnostics;
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ManifestUrlInput));
        RefreshCommand = new AsyncRelayCommand<AddonListItemViewModel>(RefreshAddonAsync, _ => !IsBusy);
        RemoveCommand = new AsyncRelayCommand<AddonListItemViewModel>(RemoveAddonAsync, _ => !IsBusy);
        ToggleEnabledCommand = new AsyncRelayCommand<AddonListItemViewModel>(ToggleEnabledAsync, _ => !IsBusy);
        RefreshDiagnosticsCommand = new RelayCommand(RefreshDiagnostics);
    }

    public ObservableCollection<AddonListItemViewModel> Addons { get; } = [];

    public ObservableCollection<NetworkDiagnosticRowViewModel> RecentEvents { get; } = [];

    public string ManifestUrlInput
    {
        get => _manifestUrlInput;
        set
        {
            if (SetProperty(ref _manifestUrlInput, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                RemoveCommand.NotifyCanExecuteChanged();
                ToggleEnabledCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasAddons => Addons.Count > 0;

    public IAsyncRelayCommand InstallCommand { get; }

    public IAsyncRelayCommand<AddonListItemViewModel> RefreshCommand { get; }

    public IAsyncRelayCommand<AddonListItemViewModel> RemoveCommand { get; }

    public IAsyncRelayCommand<AddonListItemViewModel> ToggleEnabledCommand { get; }

    public IRelayCommand RefreshDiagnosticsCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var addons = await _addonService.ListAsync(cancellationToken);
        UpdateAddons(addons);
        RefreshDiagnostics();
    }

    private async Task InstallAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(ManifestUrlInput) || Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        var cancellationToken = BeginOperation();
        IsBusy = true;
        try
        {
            var addon = await _addonService.InstallAsync(ManifestUrlInput, cancellationToken);
            StatusMessage = $"Installed {addon.DisplayName}";
            ManifestUrlInput = string.Empty;
            UpdateAddons(await _addonService.ListAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            FinishOperation();
        }
    }

    private async Task RefreshAddonAsync(AddonListItemViewModel? item)
    {
        if (item is null || Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        var cancellationToken = BeginOperation();
        IsBusy = true;
        try
        {
            await _addonService.RefreshAsync(item.Id, cancellationToken);
            UpdateAddons(await _addonService.ListAsync(cancellationToken));
            StatusMessage = $"Refreshed {item.Name}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            FinishOperation();
        }
    }

    private async Task RemoveAddonAsync(AddonListItemViewModel? item)
    {
        if (item is null || Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        var cancellationToken = BeginOperation();
        IsBusy = true;
        try
        {
            await _addonService.RemoveAsync(item.Id, cancellationToken);
            UpdateAddons(await _addonService.ListAsync(cancellationToken));
            StatusMessage = $"Removed {item.Name}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
        finally
        {
            FinishOperation();
        }
    }

    private async Task ToggleEnabledAsync(AddonListItemViewModel? item)
    {
        if (item is null || Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        var cancellationToken = BeginOperation();
        IsBusy = true;
        try
        {
            var updated = await _addonService.SetEnabledAsync(item.Id, !item.Enabled, cancellationToken);
            UpdateAddons(await _addonService.ListAsync(cancellationToken));
            StatusMessage = updated.Enabled ? $"Enabled {item.Name}" : $"Disabled {item.Name}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Toggle failed: {ex.Message}";
        }
        finally
        {
            FinishOperation();
        }
    }

    public void CancelPendingOperations()
    {
        _operationCancellation?.Cancel();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        CancelPendingOperations();
    }

    private CancellationToken BeginOperation()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        return _operationCancellation.Token;
    }

    private void FinishOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = null;

        if (Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        IsBusy = false;
        RefreshDiagnostics();
    }

    private void UpdateAddons(IReadOnlyList<ManagedAddon> addons)
    {
        Addons.Clear();
        foreach (var addon in addons)
        {
            Addons.Add(new AddonListItemViewModel(addon));
        }

        OnPropertyChanged(nameof(HasAddons));
    }

    private void RefreshDiagnostics()
    {
        RecentEvents.Clear();
        if (_diagnostics is null)
        {
            return;
        }

        foreach (var diagnosticEvent in _diagnostics.Snapshot().Take(20))
        {
            RecentEvents.Add(new NetworkDiagnosticRowViewModel(diagnosticEvent));
        }
    }
}

public sealed class AddonListItemViewModel : ViewModelBase
{
    public AddonListItemViewModel(ManagedAddon addon)
    {
        Id = addon.Id;
        Name = addon.DisplayName;
        Version = addon.Manifest?.Version ?? "n/a";
        Enabled = addon.Enabled;
        LastError = addon.LastError ?? string.Empty;
        ManifestUrlDisplay = $"{addon.ManifestUrl.Scheme}://{addon.ManifestUrl.Host}{addon.ManifestUrl.AbsolutePath}";
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public bool Enabled { get; }
    public string LastError { get; }
    public string ManifestUrlDisplay { get; }
    public bool HasError => !string.IsNullOrWhiteSpace(LastError);
    public string EnabledLabel => Enabled ? "Enabled" : "Disabled";
    public string ToggleLabel => Enabled ? "Disable" : "Enable";
}

public sealed class NetworkDiagnosticRowViewModel
{
    public NetworkDiagnosticRowViewModel(NetworkDiagnosticEvent diagnosticEvent)
    {
        Kind = diagnosticEvent.Kind.ToString();
        Host = diagnosticEvent.Host;
        ResourceKind = diagnosticEvent.ResourceKind;
        StatusCode = diagnosticEvent.StatusCode?.ToString() ?? "-";
        DurationMs = diagnosticEvent.DurationMs?.ToString() ?? "-";
        Timestamp = diagnosticEvent.Timestamp.ToString("HH:mm:ss");
        Message = diagnosticEvent.Message;
        AddonId = diagnosticEvent.AddonId ?? string.Empty;
    }

    public string Kind { get; }
    public string Host { get; }
    public string ResourceKind { get; }
    public string StatusCode { get; }
    public string DurationMs { get; }
    public string Timestamp { get; }
    public string Message { get; }
    public string AddonId { get; }
}
