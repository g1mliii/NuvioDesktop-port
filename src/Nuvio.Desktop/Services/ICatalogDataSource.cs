using System.Runtime.CompilerServices;
using Nuvio.Core.Models;
using Nuvio.Desktop.Models;

namespace Nuvio.Desktop.Services;

public sealed record DesktopHomeRail(
    string Title,
    IReadOnlyList<CatalogItem> Items,
    string Key = "",
    string AddonId = "",
    string AddonName = "",
    string Type = "",
    string CatalogId = "");

public sealed record DesktopCatalogPage(IReadOnlyList<CatalogItem> Items, bool HasMore, int NextSkip);

/// <summary>A selectable catalog for the Catalog page filter, with the genre options the addon advertises.</summary>
public sealed record DesktopCatalogChoice(
    string AddonId,
    string AddonName,
    string Type,
    string CatalogId,
    string DisplayName,
    IReadOnlyList<string> Genres)
{
    public string Key => $"{AddonId}:{Type}:{CatalogId}";
}

public interface ICatalogDataSource
{
    string ModeLabel { get; }

    Task<IReadOnlyList<DesktopHomeRail>> GetHomeRailsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Streams Home rails as they arrive so the page can paint incrementally (progressive publish).
    /// Default implementation falls back to the non-streaming <see cref="GetHomeRailsAsync"/>.
    /// </summary>
    async IAsyncEnumerable<DesktopHomeRail> StreamHomeRailsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var rail in await GetHomeRailsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return rail;
        }
    }

    /// <summary>
    /// True when at least one enabled addon advertises a Home-eligible catalog. Lets Home distinguish
    /// "no addons installed" from "addons installed but all rails failed/empty". Default: true.
    /// </summary>
    Task<bool> HasCatalogCapableAddonsAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    Task<DesktopCatalogPage> GetCatalogPageAsync(int skip, CancellationToken cancellationToken);

    /// <summary>Catalog choices for the Catalog page filter. Default: none.</summary>
    Task<IReadOnlyList<DesktopCatalogChoice>> GetCatalogChoicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DesktopCatalogChoice>>(Array.Empty<DesktopCatalogChoice>());

    /// <summary>Selects which catalog (and optional genre) subsequent <see cref="GetCatalogPageAsync"/>
    /// calls browse. Default: no-op.</summary>
    void SelectCatalog(DesktopCatalogChoice choice, string? genre)
    {
    }

    Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<FixtureDetailState> GetDetailsAsync(string mediaId, string? mediaType, CancellationToken cancellationToken);
}
