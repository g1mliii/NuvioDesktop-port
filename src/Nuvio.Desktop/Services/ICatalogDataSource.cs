using Nuvio.Core.Models;
using Nuvio.Desktop.Models;

namespace Nuvio.Desktop.Services;

public sealed record DesktopHomeRail(string Title, IReadOnlyList<CatalogItem> Items);

public sealed record DesktopCatalogPage(IReadOnlyList<CatalogItem> Items, bool HasMore, int NextSkip);

public interface ICatalogDataSource
{
    string ModeLabel { get; }

    Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken);

    Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken);
}
