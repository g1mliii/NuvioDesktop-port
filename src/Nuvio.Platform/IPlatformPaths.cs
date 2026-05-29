namespace Nuvio.Platform;

public interface IPlatformPaths
{
    string AppDataDirectory { get; }

    string CacheDirectory { get; }

    string LogDirectory { get; }

    string DatabasePath { get; }

    string BackupDirectory { get; }

    string ImageCacheDirectory { get; }
}
