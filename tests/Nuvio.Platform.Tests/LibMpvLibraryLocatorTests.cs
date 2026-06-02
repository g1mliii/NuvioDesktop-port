namespace Nuvio.Platform.Tests;

public sealed class LibMpvLibraryLocatorTests
{
    [Theory]
    [InlineData(PlatformFamily.Windows, "libmpv-2.dll")]
    [InlineData(PlatformFamily.MacOS, "libmpv.2.dylib")]
    [InlineData(PlatformFamily.Linux, "libmpv.so.2")]
    public void CandidateLibraryNames_SelectsPreferredNamePerOs(PlatformFamily family, string expectedFirst)
    {
        var locator = new LibMpvLibraryLocator(Environment(family, files: []));

        var names = locator.CandidateLibraryNames();

        Assert.NotEmpty(names);
        Assert.Equal(expectedFirst, names[0]);
    }

    [Fact]
    public void CandidateLibraryNames_IsEmptyForUnknownPlatform()
    {
        var locator = new LibMpvLibraryLocator(Environment(PlatformFamily.Unknown, files: []));

        Assert.Empty(locator.CandidateLibraryNames());
    }

    [Fact]
    public void Locate_UsesEnvironmentOverrideFirst()
    {
        var overridePath = Path.Combine("C:", "tools", "mpv", "libmpv-2.dll");

        var result = new LibMpvLibraryLocator(
                Environment(PlatformFamily.Windows, files: [overridePath], environmentOverridePath: overridePath))
            .Locate();

        Assert.True(result.FoundOnDisk);
        Assert.Equal(overridePath, result.LibraryPath);
        Assert.Equal(LibMpvLibrarySource.EnvironmentOverride, result.Source);
    }

    [Fact]
    public void Locate_PrefersAppManagedOverSystemLocations()
    {
        var appBase = Path.Combine("C:", "Nuvio");
        var appManaged = Path.Combine(appBase, "libmpv-2.dll");
        var systemLib = Path.Combine("C:", "Program Files", "mpv", "libmpv-2.dll");

        var result = new LibMpvLibraryLocator(
                Environment(PlatformFamily.Windows, files: [appManaged, systemLib], appBaseDirectory: appBase))
            .Locate();

        Assert.True(result.FoundOnDisk);
        Assert.Equal(appManaged, result.LibraryPath);
        Assert.Equal(LibMpvLibrarySource.AppManaged, result.Source);
    }

    [Fact]
    public void Locate_DetectsLinuxSystemLibrary()
    {
        var systemLib = "/usr/lib/x86_64-linux-gnu/libmpv.so.2";

        var result = new LibMpvLibraryLocator(
                Environment(PlatformFamily.Linux, files: [systemLib]))
            .Locate();

        Assert.True(result.FoundOnDisk);
        Assert.Equal(systemLib, result.LibraryPath);
        Assert.Equal(LibMpvLibrarySource.SystemLocation, result.Source);
    }

    [Fact]
    public void Locate_FallsBackToLoaderResolvedCandidatesWhenNothingOnDisk()
    {
        var result = new LibMpvLibraryLocator(Environment(PlatformFamily.Linux, files: [])).Locate();

        Assert.False(result.FoundOnDisk);
        Assert.NotNull(result.Message);
        // Bare library names are still offered so the OS loader can resolve through its own search path.
        Assert.Contains(result.Candidates, candidate => candidate.Source == LibMpvLibrarySource.LoaderResolved);
        Assert.Contains(result.Candidates, candidate => candidate.NameOrPath == "libmpv.so.2");
    }

    [Fact]
    public void Locate_ReportsClearMessage_WhenEnvironmentOverrideMissing()
    {
        var result = new LibMpvLibraryLocator(
                Environment(PlatformFamily.Windows, files: [], environmentOverridePath: @"C:\nope\libmpv-2.dll"))
            .Locate();

        Assert.False(result.FoundOnDisk);
        Assert.Contains("NUVIO_LIBMPV_PATH", result.Message);
    }

    private static LibMpvLocatorEnvironment Environment(
        PlatformFamily family,
        IReadOnlyCollection<string> files,
        string? environmentOverridePath = null,
        string? appBaseDirectory = null)
    {
        var comparer = family == PlatformFamily.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var fileSet = new HashSet<string>(files, comparer);

        return new LibMpvLocatorEnvironment(
            family,
            appBaseDirectory ?? Path.Combine("C:", "Nuvio"),
            "win-x64",
            environmentOverridePath,
            Path.Combine("C:", "Users", "Subai"),
            Path.Combine("C:", "Users", "Subai", "AppData", "Local"),
            fileSet.Contains);
    }
}
