namespace Nuvio.Data;

public sealed record StoragePlan(
    string DatabaseFileName,
    string ImageCacheDirectoryName,
    long DefaultImageCacheBytes)
{
    public static StoragePlan Default { get; } = new(
        "nuvio-desktop.sqlite3",
        "image-cache",
        500L * 1024L * 1024L);
}
