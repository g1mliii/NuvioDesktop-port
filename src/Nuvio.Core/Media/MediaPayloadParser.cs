using System.Text.Json;
using Nuvio.Core.Addons;
using Nuvio.Core.Models;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Media;

public static class MediaPayloadParser
{
    private static readonly Uri MetadataBaseUri = new("https://metadata.invalid/");

    public static IReadOnlyList<CatalogItem> ParseCatalogItems(string payload)
    {
        PayloadSizeGuard.RequireWithinUtf8ByteLimit(
            payload,
            AddonFetchPolicy.Default.MaxCatalogBytes,
            "Catalog payload");

        using var document = ParseDocument(payload, "Catalog payload");
        var root = RequireObject(document.RootElement, "Catalog payload");
        if (!root.TryGetProperty("metas", out var metasElement) ||
            metasElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CatalogItem>();
        }

        var items = new List<CatalogItem>();
        foreach (var meta in metasElement.EnumerateArray())
        {
            if (meta.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new CatalogItem(
                Id: RequiredString(meta, "id", "Catalog item"),
                Type: RequiredString(meta, "type", "Catalog item"),
                Name: RequiredString(meta, "name", "Catalog item"),
                PosterUrl: OptionalAssetUri(meta, "poster"),
                BackgroundUrl: OptionalAssetUri(meta, "background"),
                ReleaseInfo: OptionalString(meta, "releaseInfo")));
        }

        return items;
    }

    public static MediaDetails ParseMediaDetails(string payload)
    {
        PayloadSizeGuard.RequireWithinUtf8ByteLimit(
            payload,
            AddonFetchPolicy.Default.MaxCatalogBytes,
            "Metadata payload");

        using var document = ParseDocument(payload, "Metadata payload");
        var root = RequireObject(document.RootElement, "Metadata payload");
        var meta = root.TryGetProperty("meta", out var metaElement)
            ? RequireObject(metaElement, "Metadata payload meta")
            : root;

        return new MediaDetails(
            Id: RequiredString(meta, "id", "Metadata item"),
            Type: RequiredString(meta, "type", "Metadata item"),
            Name: RequiredString(meta, "name", "Metadata item"),
            PosterUrl: OptionalAssetUri(meta, "poster"),
            BackgroundUrl: OptionalAssetUri(meta, "background"),
            LogoUrl: OptionalAssetUri(meta, "logo"),
            Description: OptionalString(meta, "description"),
            ReleaseInfo: OptionalString(meta, "releaseInfo"),
            Runtime: OptionalString(meta, "runtime"),
            Genres: StringList(meta, "genres"),
            ExternalRatings: ExternalRatings(meta),
            Cast: People(meta, "cast"),
            ProductionCompanies: Companies(meta, "productionCompanies"),
            Trailers: Trailers(meta),
            Links: Links(meta),
            Videos: Videos(meta));
    }

    private static JsonDocument ParseDocument(string payload, string label)
    {
        try
        {
            return JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            throw new NuvioValidationException($"{label} JSON is invalid: {ex.Message}");
        }
    }

    private static JsonElement RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new NuvioValidationException($"{label} must be a JSON object.");
        }

        return element;
    }

    private static IReadOnlyList<MediaExternalRating> ExternalRatings(JsonElement meta)
    {
        if (!meta.TryGetProperty("externalRatings", out var ratingsElement) ||
            ratingsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaExternalRating>();
        }

        return ratingsElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new MediaExternalRating(
                Source: OptionalString(item, "source") ?? string.Empty,
                Value: Double(item, "value") ?? 0d))
            .Where(item => item.Source.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<MediaPerson> People(JsonElement meta, string propertyName)
    {
        if (!meta.TryGetProperty(propertyName, out var peopleElement) ||
            peopleElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaPerson>();
        }

        return peopleElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new MediaPerson(
                Name: OptionalString(item, "name") ?? string.Empty,
                Role: OptionalString(item, "role"),
                PhotoUrl: OptionalAssetUri(item, "photo")))
            .Where(item => item.Name.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<MediaCompany> Companies(JsonElement meta, string propertyName)
    {
        if (!meta.TryGetProperty(propertyName, out var companiesElement) ||
            companiesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaCompany>();
        }

        return companiesElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new MediaCompany(
                Name: OptionalString(item, "name") ?? string.Empty,
                LogoUrl: OptionalAssetUri(item, "logo")))
            .Where(item => item.Name.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<MediaTrailer> Trailers(JsonElement meta)
    {
        if (!meta.TryGetProperty("trailers", out var trailersElement) ||
            trailersElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaTrailer>();
        }

        return trailersElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new MediaTrailer(
                Id: OptionalString(item, "id") ?? OptionalString(item, "key") ?? string.Empty,
                Key: OptionalString(item, "key") ?? string.Empty,
                Name: OptionalString(item, "name") ?? string.Empty,
                Site: OptionalString(item, "site") ?? string.Empty,
                Type: OptionalString(item, "type") ?? "Trailer",
                Official: Boolean(item, "official")))
            .Where(item => item.Id.Length > 0 && item.Key.Length > 0)
            .ToArray();
    }

    private static IReadOnlyList<MediaLink> Links(JsonElement meta)
    {
        if (!meta.TryGetProperty("links", out var linksElement) ||
            linksElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaLink>();
        }

        var links = new List<MediaLink>();
        foreach (var item in linksElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = OptionalString(item, "name");
            var category = OptionalString(item, "category");
            var url = OptionalAssetUri(item, "url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category) || url is null)
            {
                continue;
            }

            links.Add(new MediaLink(name, category, url));
        }

        return links;
    }

    private static IReadOnlyList<MediaVideo> Videos(JsonElement meta)
    {
        if (!meta.TryGetProperty("videos", out var videosElement) ||
            videosElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MediaVideo>();
        }

        return videosElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new MediaVideo(
                Id: RequiredString(item, "id", "Metadata video"),
                Title: OptionalString(item, "title") ?? string.Empty,
                Released: OptionalString(item, "released"),
                ThumbnailUrl: OptionalAssetUri(item, "thumbnail"),
                Season: Int(item, "season"),
                Episode: Int(item, "episode"),
                Overview: OptionalString(item, "overview"),
                RuntimeMinutes: Int(item, "runtime")))
            .ToArray();
    }

    private static Uri? OptionalAssetUri(JsonElement obj, string propertyName)
    {
        var value = OptionalString(obj, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : AddonUrlPolicy.ResolveManifestAssetUri(MetadataBaseUri, value, propertyName);
    }

    private static string RequiredString(JsonElement obj, string propertyName, string label)
    {
        var value = OptionalString(obj, propertyName)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new NuvioValidationException($"{label} missing \"{propertyName}\".");
        }

        return value;
    }

    private static string? OptionalString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static IReadOnlyList<string> StringList(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static bool Boolean(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }

    private static double? Double(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            ? parsed
            : null;
    }
}
