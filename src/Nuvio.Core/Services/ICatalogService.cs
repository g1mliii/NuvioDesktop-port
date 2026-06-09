using Nuvio.Core.Models;

namespace Nuvio.Core.Services;

public sealed record CatalogPage(
    string AddonId,
    string AddonName,
    string Type,
    string CatalogId,
    int Skip,
    IReadOnlyList<CatalogItem> Items);

public sealed record CatalogRail(
    string AddonId,
    string AddonName,
    string Title,
    string Type,
    string CatalogId,
    IReadOnlyList<CatalogItem> Items);

public interface ICatalogService
{
    const int PageSize = 100;

    Task<CatalogPage> BrowseAsync(
        string addonId,
        string type,
        string catalogId,
        int skip,
        CancellationToken cancellationToken,
        string? genre = null);

    Task<IReadOnlyList<CatalogItem>> SearchAsync(
        string query,
        IReadOnlyList<string>? types,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogRail>> HomeRailsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams Home rails in bounded fetch batches so the UI can paint each rail as it arrives
    /// (progressive publish) rather than awaiting the whole catalog fan-out.
    /// </summary>
    IAsyncEnumerable<CatalogRail> StreamHomeRailsAsync(CancellationToken cancellationToken);
}
