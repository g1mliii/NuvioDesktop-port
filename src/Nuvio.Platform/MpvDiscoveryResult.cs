namespace Nuvio.Platform;

public sealed record MpvDiscoveryResult(
    bool IsAvailable,
    string? ExecutablePath,
    MpvInstallationSource? Source,
    string? Version,
    string? Message)
{
    public string StatusLabel => IsAvailable ? "Found" : "Not found";

    public string SourceLabel => Source switch
    {
        MpvInstallationSource.EnvironmentOverride => "Environment override",
        MpvInstallationSource.AppManaged => "Nuvio-managed",
        MpvInstallationSource.MpvManager => "MPV Manager-installed",
        MpvInstallationSource.Path => "PATH",
        MpvInstallationSource.CommonLocation => "Common install path",
        _ => "Not detected"
    };

    public static MpvDiscoveryResult Found(
        string executablePath,
        MpvInstallationSource source,
        string? version,
        string? message = null) =>
        new(true, executablePath, source, version, message);

    public static MpvDiscoveryResult NotFound(string? message = null) =>
        new(false, null, null, null, message);
}
