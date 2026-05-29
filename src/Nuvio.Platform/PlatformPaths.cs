namespace Nuvio.Platform;

public sealed class PlatformPaths : IPlatformPaths
{
    public const string WindowsAppDirectoryName = "Nuvio Desktop";
    public const string UnixAppDirectoryName = "nuvio-desktop";
    public const string DefaultDatabaseFileName = "nuvio-desktop.sqlite3";
    public const string DefaultImageCacheDirectoryName = "image-cache";

    private PlatformPaths(
        string appDataDirectory,
        string cacheDirectory,
        string databaseFileName,
        string imageCacheDirectoryName)
    {
        AppDataDirectory = appDataDirectory;
        CacheDirectory = cacheDirectory;
        LogDirectory = Join(appDataDirectory, "logs");
        BackupDirectory = Join(appDataDirectory, "backups");
        DatabasePath = Join(appDataDirectory, databaseFileName);
        ImageCacheDirectory = Join(cacheDirectory, imageCacheDirectoryName);
    }

    public string AppDataDirectory { get; }

    public string CacheDirectory { get; }

    public string LogDirectory { get; }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public string ImageCacheDirectory { get; }

    public static PlatformPaths Current(
        string databaseFileName = DefaultDatabaseFileName,
        string imageCacheDirectoryName = DefaultImageCacheDirectoryName) =>
        For(
            PlatformInfoProvider.Current().Family,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable,
            databaseFileName,
            imageCacheDirectoryName);

    public static PlatformPaths For(
        PlatformFamily family,
        string homeDirectory,
        Func<string, string?> environment,
        string databaseFileName = DefaultDatabaseFileName,
        string imageCacheDirectoryName = DefaultImageCacheDirectoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageCacheDirectoryName);

        return family switch
        {
            PlatformFamily.Windows => BuildWindows(homeDirectory, environment, databaseFileName, imageCacheDirectoryName),
            PlatformFamily.MacOS => new PlatformPaths(
                Join(homeDirectory, "Library", "Application Support", WindowsAppDirectoryName),
                Join(homeDirectory, "Library", "Caches", WindowsAppDirectoryName),
                databaseFileName,
                imageCacheDirectoryName),
            PlatformFamily.Linux => BuildLinux(homeDirectory, environment, databaseFileName, imageCacheDirectoryName),
            _ => BuildLinux(homeDirectory, environment, databaseFileName, imageCacheDirectoryName)
        };
    }

    private static PlatformPaths BuildWindows(
        string homeDirectory,
        Func<string, string?> environment,
        string databaseFileName,
        string imageCacheDirectoryName)
    {
        var localAppData = environment("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Join(homeDirectory, "AppData", "Local");
        }

        var appDirectory = Join(localAppData, WindowsAppDirectoryName);
        return new PlatformPaths(appDirectory, appDirectory, databaseFileName, imageCacheDirectoryName);
    }

    private static PlatformPaths BuildLinux(
        string homeDirectory,
        Func<string, string?> environment,
        string databaseFileName,
        string imageCacheDirectoryName)
    {
        var dataRoot = environment("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            dataRoot = Join(homeDirectory, ".local", "share");
        }

        var cacheRoot = environment("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            cacheRoot = Join(homeDirectory, ".cache");
        }

        return new PlatformPaths(
            Join(dataRoot, UnixAppDirectoryName),
            Join(cacheRoot, UnixAppDirectoryName),
            databaseFileName,
            imageCacheDirectoryName);
    }

    private static string Join(string first, params string[] parts)
    {
        var separator = first.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        var value = first.TrimEnd('\\', '/');
        foreach (var part in parts)
        {
            value += separator + part.Trim('\\', '/');
        }

        return value;
    }
}
