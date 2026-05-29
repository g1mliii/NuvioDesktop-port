using Nuvio.Platform;

namespace Nuvio.Platform.Tests;

public sealed class PlatformPathsTests
{
    [Fact]
    public void Windows_UsesLocalAppDataNuvioDesktop()
    {
        var paths = PlatformPaths.For(
            PlatformFamily.Windows,
            @"C:\Users\test",
            key => key == "LOCALAPPDATA" ? @"C:\Users\test\AppData\Local" : null);

        Assert.Equal(@"C:\Users\test\AppData\Local\Nuvio Desktop", paths.AppDataDirectory);
        Assert.Equal(paths.AppDataDirectory, paths.CacheDirectory);
        Assert.EndsWith(@"\nuvio-desktop.sqlite3", paths.DatabasePath, StringComparison.Ordinal);
        Assert.EndsWith(@"\image-cache", paths.ImageCacheDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void MacOS_UsesApplicationSupportAndCaches()
    {
        var paths = PlatformPaths.For(PlatformFamily.MacOS, "/Users/test", _ => null);

        Assert.Equal("/Users/test/Library/Application Support/Nuvio Desktop", paths.AppDataDirectory);
        Assert.Equal("/Users/test/Library/Caches/Nuvio Desktop", paths.CacheDirectory);
        Assert.Equal("/Users/test/Library/Application Support/Nuvio Desktop/nuvio-desktop.sqlite3", paths.DatabasePath);
        Assert.Equal("/Users/test/Library/Caches/Nuvio Desktop/image-cache", paths.ImageCacheDirectory);
    }

    [Fact]
    public void Linux_UsesXdgRootsAndFallbacks()
    {
        var xdg = PlatformPaths.For(
            PlatformFamily.Linux,
            "/home/test",
            key => key switch
            {
                "XDG_DATA_HOME" => "/data-home",
                "XDG_CACHE_HOME" => "/cache-home",
                _ => null
            });

        Assert.Equal("/data-home/nuvio-desktop", xdg.AppDataDirectory);
        Assert.Equal("/cache-home/nuvio-desktop", xdg.CacheDirectory);

        var fallback = PlatformPaths.For(PlatformFamily.Linux, "/home/test", _ => null);
        Assert.Equal("/home/test/.local/share/nuvio-desktop", fallback.AppDataDirectory);
        Assert.Equal("/home/test/.cache/nuvio-desktop", fallback.CacheDirectory);
    }
}
