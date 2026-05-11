using System.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Addons;

public static class AddonUrlPolicy
{
    private static readonly HashSet<string> UnsafeSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "javascript",
        "data",
        "vbscript"
    };

    public static Uri NormalizeManifestUrl(string rawUrl)
    {
        var trimmed = rawUrl.Trim();
        if (trimmed.Length == 0)
        {
            throw new NuvioValidationException("Addon URL is required.");
        }

        var normalizedScheme = trimmed switch
        {
            _ when trimmed.StartsWith("stremio://", StringComparison.OrdinalIgnoreCase) =>
                "https://" + trimmed["stremio://".Length..],
            _ when HasExplicitUriScheme(trimmed) => trimmed,
            _ => "https://" + trimmed
        };

        var withoutFragment = normalizedScheme.Split('#', 2)[0];
        var query = "";
        var path = withoutFragment;
        var queryIndex = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            query = withoutFragment[(queryIndex + 1)..];
            path = withoutFragment[..queryIndex];
        }

        path = path.TrimEnd('/');
        var manifestPath = path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + "/manifest.json";
        var normalized = query.Length == 0 ? manifestPath : manifestPath + "?" + query;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            throw new NuvioValidationException("Addon URL is not a valid absolute URL.");
        }

        ValidateRemoteUri(uri, "Addon manifest URL");
        return uri;
    }

    public static void ValidateRemoteUri(Uri uri, string fieldName)
    {
        if (UnsafeSchemes.Contains(uri.Scheme))
        {
            throw new NuvioValidationException($"{fieldName} uses an unsafe URL scheme.");
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && IsLoopback(uri))
        {
            return;
        }

        throw new NuvioValidationException($"{fieldName} must use HTTPS, except HTTP loopback URLs for tests.");
    }

    public static Uri ResolveManifestAssetUri(Uri manifestUrl, string rawValue, string fieldName)
    {
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            throw new NuvioValidationException($"{fieldName} URL is empty.");
        }

        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(value);
        }

        Uri resolved;
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            resolved = new Uri("https:" + value);
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            resolved = absolute;
        }
        else
        {
            resolved = new Uri(manifestUrl, value);
        }

        ValidateRemoteUri(resolved, fieldName);
        return resolved;
    }

    public static bool TryCreatePlaybackUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!UnsafeSchemes.Contains(parsed.Scheme))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string? ExplicitScheme(string value)
    {
        var colonIndex = value.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= 0)
        {
            return null;
        }

        var candidate = value[..colonIndex];
        if (!char.IsAsciiLetter(candidate[0]))
        {
            return null;
        }

        return candidate.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '+' or '-' or '.')
            ? candidate
            : null;
    }

    private static bool HasExplicitUriScheme(string value)
    {
        var scheme = ExplicitScheme(value);
        if (scheme is null)
        {
            return false;
        }

        var colonIndex = scheme.Length;
        return (value.Length > colonIndex + 2 &&
            value[colonIndex + 1] == '/' &&
            value[colonIndex + 2] == '/') ||
            UnsafeSchemes.Contains(scheme);
    }
}
