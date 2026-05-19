using Nuvio.Core.Models;
using Nuvio.Desktop.Models;

namespace Nuvio.Desktop.Services;

public sealed class FixtureCatalogDataSource : ICatalogDataSource
{
    private readonly IDesktopFixtureService _fixtures;

    public FixtureCatalogDataSource(IDesktopFixtureService fixtures)
    {
        _fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
    }

    public string ModeLabel => "Fixture data";

    public async Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken)
    {
        var sections = await _fixtures.GetHomeSectionsAsync(cancellationToken).ConfigureAwait(false);
        return sections.Select(section => new DesktopHomeRail(section.Title, section.Items)).ToArray();
    }

    public async Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken)
    {
        var all = await _fixtures.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return new DesktopCatalogPage(all, HasMore: false, NextSkip: all.Count);
    }

    public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
        _fixtures.SearchAsync(query, cancellationToken);

    public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
        _fixtures.GetDetailsAsync(mediaId, cancellationToken);
}
