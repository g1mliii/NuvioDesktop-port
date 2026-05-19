namespace Nuvio.Desktop.Models;

public enum DesktopRouteKind
{
    Home,
    Search,
    Catalog,
    Details,
    Addons,
    Settings,
    Player
}

public sealed record DesktopRoute(DesktopRouteKind Kind, string? MediaId = null, string? MediaType = null)
{
    public static DesktopRoute Home { get; } = new(DesktopRouteKind.Home);
    public static DesktopRoute Search { get; } = new(DesktopRouteKind.Search);
    public static DesktopRoute Catalog { get; } = new(DesktopRouteKind.Catalog);
    public static DesktopRoute Addons { get; } = new(DesktopRouteKind.Addons);
    public static DesktopRoute Settings { get; } = new(DesktopRouteKind.Settings);
    public static DesktopRoute Player { get; } = new(DesktopRouteKind.Player);

    public static DesktopRoute Details(string mediaId, string? mediaType = null) =>
        new(DesktopRouteKind.Details, mediaId, mediaType);

    public string Label => Kind switch
    {
        DesktopRouteKind.Home => "Home",
        DesktopRouteKind.Search => "Search",
        DesktopRouteKind.Catalog => "Catalog",
        DesktopRouteKind.Details => "Details",
        DesktopRouteKind.Addons => "Addons",
        DesktopRouteKind.Settings => "Settings",
        DesktopRouteKind.Player => "Player",
        _ => Kind.ToString()
    };

    public bool IsPrimaryNavigation => Kind is DesktopRouteKind.Home
        or DesktopRouteKind.Search
        or DesktopRouteKind.Catalog
        or DesktopRouteKind.Addons
        or DesktopRouteKind.Settings;
}
