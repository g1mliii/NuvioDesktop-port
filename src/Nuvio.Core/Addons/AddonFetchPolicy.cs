namespace Nuvio.Core.Addons;

public sealed record AddonFetchPolicy(
    TimeSpan Timeout,
    long MaxManifestBytes,
    long MaxCatalogBytes,
    long MaxStreamBytes)
{
    public static AddonFetchPolicy Default { get; } = new(
        Timeout: TimeSpan.FromSeconds(15),
        MaxManifestBytes: 512 * 1024,
        MaxCatalogBytes: 2 * 1024 * 1024,
        MaxStreamBytes: 2 * 1024 * 1024);
}
