using System.Runtime.CompilerServices;
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
        return sections.Select(ToRail).ToArray();
    }

    public async IAsyncEnumerable<DesktopHomeRail> StreamHomeRailsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sections = await _fixtures.GetHomeSectionsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ToRail(section);
        }
    }

    public Task<bool> HasCatalogCapableAddonsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public async Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken)
    {
        var all = await _fixtures.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return new DesktopCatalogPage(all, HasMore: false, NextSkip: all.Count);
    }

    public Task<IReadOnlyList<DesktopCatalogChoice>> GetCatalogChoicesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DesktopCatalogChoice> choices =
        [
            new("addon:fixture", "Fixture Addon", "movie", "fixture-movies", "Fixture Movies — Movies",
                ["Action", "Drama", "Sci-Fi"]),
            new("addon:fixture", "Fixture Addon", "series", "fixture-series", "Fixture Series — Series",
                ["Drama", "Mystery"]),
        ];
        return Task.FromResult(choices);
    }

    public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
        _fixtures.SearchAsync(query, cancellationToken);

    public Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken) =>
        _fixtures.GetDetailsAsync(mediaId, cancellationToken);

    private static DesktopHomeRail ToRail(FixtureHomeSection section) =>
        new(
            Title: section.Title,
            Items: section.Items,
            Key: $"fixture:{section.Title}",
            AddonId: "addon:fixture",
            AddonName: "Fixture Addon",
            Type: section.Items.Count > 0 ? section.Items[0].Type : "movie",
            CatalogId: section.Title);
}
