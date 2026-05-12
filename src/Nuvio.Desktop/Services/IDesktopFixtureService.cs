using Nuvio.Core.Models;
using Nuvio.Desktop.Models;

namespace Nuvio.Desktop.Services;

public interface IDesktopFixtureService
{
    Task<IReadOnlyList<FixtureHomeSection>> GetHomeSectionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<FixtureDetailState> GetDetailsAsync(string mediaId, CancellationToken cancellationToken);
}
