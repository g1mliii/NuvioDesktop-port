using Nuvio.Data;

namespace Nuvio.Data.Tests;

public sealed class StoragePlanTests
{
    [Fact]
    public void Default_UsesBoundedImageCache()
    {
        Assert.Equal("nuvio-desktop.sqlite3", StoragePlan.Default.DatabaseFileName);
        Assert.Equal("image-cache", StoragePlan.Default.ImageCacheDirectoryName);
        Assert.True(StoragePlan.Default.DefaultImageCacheBytes <= 500L * 1024L * 1024L);
    }
}
