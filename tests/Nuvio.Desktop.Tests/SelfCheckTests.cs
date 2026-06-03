using Nuvio.Platform;

namespace Nuvio.Desktop.Tests;

public sealed class SelfCheckTests
{
    [Theory]
    [InlineData(PlatformFamily.Windows)]
    [InlineData(PlatformFamily.MacOS)]
    [InlineData(PlatformFamily.Linux)]
    public void SelfCheckPlatformPaths_StayUnderTemporaryRoot(PlatformFamily family)
    {
        var root = Path.Combine(Path.GetTempPath(), $"nuvio-self-check-test-{Guid.NewGuid():N}");

        var paths = SelfCheck.CreateSelfCheckPlatformPaths(family, root);

        AssertUnderRoot(root, paths.AppDataDirectory);
        AssertUnderRoot(root, paths.CacheDirectory);
        AssertUnderRoot(root, paths.DatabasePath);
        AssertUnderRoot(root, paths.ImageCacheDirectory);
    }

    private static void AssertUnderRoot(string root, string path)
    {
        var normalizedRoot = Normalize(root);
        var normalizedPath = Normalize(path);
        Assert.StartsWith(
            normalizedRoot + "/",
            normalizedPath + "/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd('\\', '/').Replace('\\', '/');
}
