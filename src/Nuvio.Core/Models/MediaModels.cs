namespace Nuvio.Core.Models;

public sealed record CatalogItem(
    string Id,
    string Type,
    string Name,
    Uri? PosterUrl,
    Uri? BackgroundUrl,
    string? ReleaseInfo);

public sealed record MediaItem(
    string Id,
    string Type,
    string Name,
    Uri? PosterUrl,
    Uri? BackgroundUrl,
    string? Description,
    string? ReleaseInfo);

public sealed record MediaDetails(
    string Id,
    string Type,
    string Name,
    Uri? PosterUrl,
    Uri? BackgroundUrl,
    Uri? LogoUrl,
    string? Description,
    string? ReleaseInfo,
    string? Runtime,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MediaExternalRating> ExternalRatings,
    IReadOnlyList<MediaPerson> Cast,
    IReadOnlyList<MediaCompany> ProductionCompanies,
    IReadOnlyList<MediaTrailer> Trailers,
    IReadOnlyList<MediaLink> Links,
    IReadOnlyList<MediaVideo> Videos);

public sealed record MediaExternalRating(string Source, double Value);

public sealed record MediaTrailer(
    string Id,
    string Key,
    string Name,
    string Site,
    string Type,
    bool Official);

public sealed record MediaPerson(
    string Name,
    string? Role,
    Uri? PhotoUrl);

public sealed record MediaCompany(
    string Name,
    Uri? LogoUrl);

public sealed record MediaLink(
    string Name,
    string Category,
    Uri Url);

public sealed record MediaVideo(
    string Id,
    string Title,
    string? Released,
    Uri? ThumbnailUrl,
    int? Season,
    int? Episode,
    string? Overview,
    int? RuntimeMinutes);
