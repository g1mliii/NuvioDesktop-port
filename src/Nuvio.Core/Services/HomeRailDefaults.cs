namespace Nuvio.Core.Services;

/// <summary>
/// Constants governing how the Home screen fetches and publishes catalog rails.
/// Ported from upstream NuvioMobile <c>HomeRepository.kt</c> so desktop Home batching
/// matches mobile/TV behaviour.
/// </summary>
public static class HomeRailDefaults
{
    /// <summary>Number of items surfaced in the hero/featured carousel.</summary>
    public const int HeroItemLimit = 8;

    /// <summary>How many catalog rails are fetched in parallel per batch.</summary>
    public const int CatalogFetchBatchSize = 4;

    /// <summary>Maximum items retained per rail for the Home preview.</summary>
    public const int CatalogPreviewFetchLimit = 18;

    /// <summary>Number of completed batches between progressive UI publishes.</summary>
    public const int CatalogPublishInterval = 2;
}
