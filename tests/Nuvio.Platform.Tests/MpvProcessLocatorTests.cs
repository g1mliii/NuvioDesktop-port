using Nuvio.Platform;

namespace Nuvio.Platform.Tests;

public sealed class MpvProcessLocatorTests
{
    [Fact]
    public void Locate_UsesEnvironmentOverrideFirst()
    {
        var mpvPath = Path.Combine("C:", "tools", "mpv", "mpv.exe");
        var pathMpv = Path.Combine("C:", "path-mpv", "mpv.exe");

        var result = Locate(
            files: [mpvPath, pathMpv],
            environmentOverridePath: mpvPath,
            pathVariable: Path.GetDirectoryName(pathMpv));

        Assert.True(result.IsAvailable);
        Assert.Equal(mpvPath, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.EnvironmentOverride, result.Source);
        Assert.Equal("mpv 0.41.0", result.Version);
    }

    [Fact]
    public void Locate_RejectsMpvManagerExecutableAsPlayer()
    {
        var managerPath = Path.Combine("C:", "Downloads", "mpv-manager-win-x86_64.exe");

        var result = Locate(
            files: [managerPath],
            environmentOverridePath: managerPath);

        Assert.False(result.IsAvailable);
        Assert.Contains("NUVIO_MPV_PATH", result.Message);
    }

    [Fact]
    public void Locate_PrefersAppManagedMpvOverManagerAndPath()
    {
        var appBase = Path.Combine("C:", "Nuvio");
        var appManaged = Path.Combine(appBase, "tools", "mpv", "mpv.exe");
        var managerMpv = Path.Combine("C:", "Users", "Subai", "AppData", "Local", "mpv-manager", "mpv", "mpv.exe");
        var pathMpv = Path.Combine("C:", "path-mpv", "mpv.exe");

        var result = Locate(
            files: [appManaged, managerMpv, pathMpv],
            appBaseDirectory: appBase,
            localApplicationDataDirectory: Path.Combine("C:", "Users", "Subai", "AppData", "Local"),
            pathVariable: Path.GetDirectoryName(pathMpv));

        Assert.True(result.IsAvailable);
        Assert.Equal(appManaged, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.AppManaged, result.Source);
    }

    [Fact]
    public void Locate_DetectsMpvManagerInstalledMpvBeforePath()
    {
        var localAppData = Path.Combine("C:", "Users", "Subai", "AppData", "Local");
        var managerMpv = Path.Combine(localAppData, "mpv-manager", "mpv", "mpv.exe");
        var pathMpv = Path.Combine("C:", "path-mpv", "mpv.exe");

        var result = Locate(
            files: [managerMpv, pathMpv],
            localApplicationDataDirectory: localAppData,
            pathVariable: Path.GetDirectoryName(pathMpv));

        Assert.True(result.IsAvailable);
        Assert.Equal(managerMpv, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.MpvManager, result.Source);
    }

    [Fact]
    public void Locate_DetectsPathMpv()
    {
        var pathMpv = Path.Combine("C:", "path-mpv", "mpv.exe");

        var result = Locate(
            files: [pathMpv],
            pathVariable: Path.GetDirectoryName(pathMpv));

        Assert.True(result.IsAvailable);
        Assert.Equal(pathMpv, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.Path, result.Source);
    }

    [Fact]
    public void Locate_DetectsChocolateyMpvWhenPathHasNotRefreshed()
    {
        var programData = Path.Combine("C:", "ProgramData");
        var chocolateyMpv = Path.Combine(programData, "chocolatey", "lib", "mpvio.install", "tools", "mpv-0.41.0-x86_64_x64", "mpv.exe");

        var result = Locate(
            files: [chocolateyMpv],
            programDataDirectory: programData);

        Assert.True(result.IsAvailable);
        Assert.Equal(chocolateyMpv, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.CommonLocation, result.Source);
    }

    [Fact]
    public void Locate_DetectsMacOsCommonMpv()
    {
        var mpvPath = "/opt/homebrew/bin/mpv";

        var result = Locate(
            platformFamily: PlatformFamily.MacOS,
            files: [mpvPath]);

        Assert.True(result.IsAvailable);
        Assert.Equal(mpvPath, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.CommonLocation, result.Source);
    }

    [Fact]
    public void Locate_DetectsLinuxCommonMpv()
    {
        var mpvPath = "/usr/bin/mpv";

        var result = Locate(
            platformFamily: PlatformFamily.Linux,
            files: [mpvPath]);

        Assert.True(result.IsAvailable);
        Assert.Equal(mpvPath, result.ExecutablePath);
        Assert.Equal(MpvInstallationSource.CommonLocation, result.Source);
    }

    private static MpvDiscoveryResult Locate(
        IReadOnlyCollection<string> files,
        PlatformFamily platformFamily = PlatformFamily.Windows,
        string? environmentOverridePath = null,
        string? appBaseDirectory = null,
        string? localApplicationDataDirectory = null,
        string? programDataDirectory = null,
        string? pathVariable = null)
    {
        var comparison = platformFamily == PlatformFamily.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = platformFamily == PlatformFamily.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var fileSet = new HashSet<string>(files, comparer);
        var environment = new MpvProcessLocatorEnvironment(
            platformFamily,
            appBaseDirectory ?? Path.Combine("C:", "Nuvio"),
            environmentOverridePath,
            pathVariable,
            Path.Combine("C:", "Users", "Subai"),
            localApplicationDataDirectory ?? Path.Combine("C:", "Users", "Subai", "AppData", "Local"),
            Path.Combine("C:", "Users", "Subai", "AppData", "Roaming"),
            programDataDirectory ?? Path.Combine("C:", "ProgramData"),
            path => fileSet.Contains(path),
            (directory, pattern) => fileSet.Where(path => path.StartsWith(directory, comparison) && Path.GetFileName(path).Equals(pattern, comparison)),
            _ => "mpv 0.41.0");

        return new MpvProcessLocator(environment).Locate();
    }
}
