namespace Nuvio.Platform;

public sealed record PlatformInfo(
    PlatformFamily Family,
    string RuntimeIdentifier,
    string Description)
{
    public bool IsSupportedDesktop =>
        Family is PlatformFamily.Windows or PlatformFamily.MacOS or PlatformFamily.Linux;
}
