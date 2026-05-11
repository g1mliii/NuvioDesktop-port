using System.Text.Json;
using Nuvio.Core.Models;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Addons;

public static class AddonManifestParser
{
    public static AddonManifest Parse(Uri manifestUrl, string payload)
    {
        PayloadSizeGuard.RequireWithinUtf8ByteLimit(
            payload,
            AddonFetchPolicy.Default.MaxManifestBytes,
            "Addon manifest");

        using var document = ParseDocument(payload, "Addon manifest");
        var root = RequireObject(document.RootElement, "Addon manifest");
        var defaultTypes = StringList(root, "types");
        var defaultPrefixes = StringList(root, "idPrefixes");

        return new AddonManifest(
            Id: RequiredString(root, "id", "Manifest"),
            Name: RequiredString(root, "name", "Manifest"),
            Description: OptionalString(root, "description") ?? string.Empty,
            Version: RequiredString(root, "version", "Manifest"),
            LogoUrl: OptionalString(root, "logo") is { } logo
                ? AddonUrlPolicy.ResolveManifestAssetUri(manifestUrl, logo, "Manifest logo")
                : null,
            Resources: Resources(root, defaultTypes, defaultPrefixes),
            Types: defaultTypes,
            IdPrefixes: defaultPrefixes,
            Catalogs: Catalogs(root),
            BehaviorHints: BehaviorHints(root),
            TransportUrl: manifestUrl);
    }

    public static AddonManifest Parse(string manifestUrl, string payload) =>
        Parse(AddonUrlPolicy.NormalizeManifestUrl(manifestUrl), payload);

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

    private static IReadOnlyList<AddonResource> Resources(
        JsonElement root,
        IReadOnlyList<string> defaultTypes,
        IReadOnlyList<string> defaultPrefixes)
    {
        if (!root.TryGetProperty("resources", out var resourcesElement) ||
            resourcesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AddonResource>();
        }

        var resources = new List<AddonResource>();
        foreach (var resource in resourcesElement.EnumerateArray())
        {
            if (resource.ValueKind == JsonValueKind.String)
            {
                var name = resource.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    resources.Add(new AddonResource(name, defaultTypes, defaultPrefixes));
                }

                continue;
            }

            if (resource.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var resourceName = RequiredString(resource, "name", "Manifest resource");
            resources.Add(new AddonResource(
                resourceName,
                StringList(resource, "types").DefaultIfEmpty(defaultTypes),
                StringList(resource, "idPrefixes").DefaultIfEmpty(defaultPrefixes)));
        }

        return resources;
    }

    private static IReadOnlyList<AddonCatalog> Catalogs(JsonElement root)
    {
        if (!root.TryGetProperty("catalogs", out var catalogsElement) ||
            catalogsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AddonCatalog>();
        }

        var catalogs = new List<AddonCatalog>();
        foreach (var catalog in catalogsElement.EnumerateArray())
        {
            if (catalog.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = RequiredString(catalog, "id", "Manifest catalog");
            catalogs.Add(new AddonCatalog(
                Type: RequiredString(catalog, "type", "Manifest catalog"),
                Id: id,
                Name: OptionalString(catalog, "name")?.Trim().Length > 0 ? OptionalString(catalog, "name")! : id,
                Extra: CatalogExtra(catalog)));
        }

        return catalogs;
    }

    private static IReadOnlyList<AddonExtraProperty> CatalogExtra(JsonElement catalog)
    {
        if (!catalog.TryGetProperty("extra", out var extraElement) ||
            extraElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AddonExtraProperty>();
        }

        var extra = new List<AddonExtraProperty>();
        foreach (var item in extraElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = OptionalString(item, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            extra.Add(new AddonExtraProperty(
                Name: name,
                IsRequired: Boolean(item, "isRequired"),
                Options: StringList(item, "options"),
                OptionsLimit: Int(item, "optionsLimit")));
        }

        return extra;
    }

    private static AddonBehaviorHints BehaviorHints(JsonElement root)
    {
        if (!root.TryGetProperty("behaviorHints", out var hints) ||
            hints.ValueKind != JsonValueKind.Object)
        {
            return new AddonBehaviorHints();
        }

        return new AddonBehaviorHints(
            Configurable: Boolean(hints, "configurable"),
            ConfigurationRequired: Boolean(hints, "configurationRequired"),
            Adult: Boolean(hints, "adult"),
            P2p: Boolean(hints, "p2p"));
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

    private static IReadOnlyList<string> DefaultIfEmpty(this IReadOnlyList<string> value, IReadOnlyList<string> fallback) =>
        value.Count == 0 ? fallback : value;
}
