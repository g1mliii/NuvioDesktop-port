using Nuvio.Core.Validation;

namespace Nuvio.Core.Addons;

public static class AddonEndpoints
{
    private const string ManifestSuffix = "/manifest.json";

    public static string GetBasePath(Uri transportUrl)
    {
        var raw = transportUrl.GetLeftPart(UriPartial.Path);
        if (raw.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^ManifestSuffix.Length];
        }

        return raw.TrimEnd('/');
    }

    public static Uri Build(
        ManagedAddon addon,
        string resource,
        string type,
        string id,
        IReadOnlyList<string>? extras = null)
    {
        if (addon.Manifest is null)
        {
            throw new NuvioValidationException($"Addon '{addon.Id}' has no manifest.");
        }

        var basePath = GetBasePath(addon.Manifest.TransportUrl);
        var extrasSegment = extras is { Count: > 0 } ? "/" + string.Join('&', extras) : string.Empty;
        var path = $"{basePath}/{resource}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}{extrasSegment}.json";

        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            throw new NuvioValidationException($"Failed to build {resource} URL for addon '{addon.Id}'.");
        }

        AddonUrlPolicy.ValidateRemoteUri(uri, $"{char.ToUpperInvariant(resource[0])}{resource[1..]} URL");
        return uri;
    }
}
