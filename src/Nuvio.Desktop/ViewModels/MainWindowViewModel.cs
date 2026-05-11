using Nuvio.Platform;

namespace Nuvio.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private string _mpvStatus = string.Empty;
    private string _mpvPath = string.Empty;
    private string _mpvVersion = string.Empty;
    private string _mpvSource = string.Empty;
    private string _mpvMessage = string.Empty;

    public MainWindowViewModel()
        : this(PlatformInfoProvider.Current(), MpvDiscoveryResult.NotFound("Checking for mpv without blocking app startup."))
    {
    }

    public MainWindowViewModel(PlatformInfo platformInfo)
        : this(platformInfo, MpvDiscoveryResult.NotFound("mpv discovery was not supplied."))
    {
    }

    public MainWindowViewModel(PlatformInfo platformInfo, MpvDiscoveryResult mpvDiscovery)
    {
        PlatformName = platformInfo.Family.ToString();
        RuntimeIdentifier = platformInfo.RuntimeIdentifier;
        PlatformDescription = platformInfo.Description;
        ApplyMpvDiscovery(mpvDiscovery);
    }

    public void ApplyMpvDiscovery(MpvDiscoveryResult mpvDiscovery)
    {
        MpvStatus = mpvDiscovery.StatusLabel;
        MpvPath = mpvDiscovery.ExecutablePath ?? "Not detected";
        MpvVersion = mpvDiscovery.Version ?? "Unavailable";
        MpvSource = mpvDiscovery.SourceLabel;
        MpvMessage = mpvDiscovery.Message ?? "Nuvio will launch the detected mpv binary directly through JSON IPC.";
    }

    public string AppName { get; } = "Nuvio Desktop";

    public string PhaseStatus { get; } = "Phase 2 mpv setup diagnostics";

    public string PlatformName { get; }

    public string RuntimeIdentifier { get; }

    public string PlatformDescription { get; }

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
}
