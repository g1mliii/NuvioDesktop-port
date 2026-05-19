using Nuvio.Core.Models;

namespace Nuvio.Core.Addons;

public sealed record ManagedAddon(
    string Id,
    Uri ManifestUrl,
    AddonManifest? Manifest,
    bool Enabled,
    int SortOrder,
    string? LastError,
    DateTimeOffset? LastRefreshedAt)
{
    public string DisplayName => Manifest?.Name ?? Id;

    public bool HasResource(string resourceName) =>
        Manifest?.Resources.Any(resource =>
            resource.Name.Equals(resourceName, StringComparison.OrdinalIgnoreCase)) ?? false;

    public bool Supports(string resourceName, string type, string? idPrefix = null)
    {
        if (Manifest is null)
        {
            return false;
        }

        foreach (var resource in Manifest.Resources)
        {
            if (!resource.Name.Equals(resourceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (resource.Types.Count > 0 &&
                !resource.Types.Any(item => item.Equals(type, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (idPrefix is not null && resource.IdPrefixes.Count > 0 &&
                !resource.IdPrefixes.Any(prefix =>
                    idPrefix.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
