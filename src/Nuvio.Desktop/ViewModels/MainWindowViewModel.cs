using Nuvio.Platform;

namespace Nuvio.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
        : this(PlatformInfoProvider.Current())
    {
    }

    public MainWindowViewModel(PlatformInfo platformInfo)
    {
        PlatformName = platformInfo.Family.ToString();
        RuntimeIdentifier = platformInfo.RuntimeIdentifier;
        PlatformDescription = platformInfo.Description;
    }

    public string AppName { get; } = "Nuvio Desktop";

    public string PhaseStatus { get; } = "Phase 0 architecture skeleton";

    public string PlatformName { get; }

    public string RuntimeIdentifier { get; }

    public string PlatformDescription { get; }
}
