namespace Nuvio.Platform;

/// <summary>
/// Ambient inputs for <see cref="LibMpvLibraryLocator"/>. Kept injectable so per-OS filename selection and
/// discovery order can be unit tested on any host without a real libmpv present.
/// </summary>
public sealed record LibMpvLocatorEnvironment(
    PlatformFamily PlatformFamily,
    string AppBaseDirectory,
    string? RuntimeIdentifier,
    string? EnvironmentOverridePath,
    string? UserProfileDirectory,
    string? LocalApplicationDataDirectory,
    Func<string, bool> FileExists)
{
    public static LibMpvLocatorEnvironment Current()
    {
        var platform = PlatformInfoProvider.Current();
        return new LibMpvLocatorEnvironment(
            platform.Family,
            AppContext.BaseDirectory,
            platform.RuntimeIdentifier,
            Environment.GetEnvironmentVariable("NUVIO_LIBMPV_PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            File.Exists);
    }
}
