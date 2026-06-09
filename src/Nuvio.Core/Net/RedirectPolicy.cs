using Nuvio.Core.Validation;

namespace Nuvio.Core.Net;

public static class RedirectPolicy
{
    public const int MaxRedirects = 3;

    public static void Validate(Uri original, Uri target, string resourceLabel)
    {
        // Cross-host redirects are permitted: real addons (Cinemeta and friends) redirect to
        // CDN/Cloudflare hosts. The caller validates every redirect target with
        // AddonUrlPolicy.ValidateRemoteUri (HTTPS-only, public host) before reaching here, and the
        // redirect count is capped, so following a validated cross-host HTTPS hop is safe.
        if (original.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new NuvioValidationException($"{resourceLabel} redirect downgrades HTTPS to HTTP; rejecting.");
        }
    }

    public static bool IsRedirect(int statusCode) =>
        statusCode is 301 or 302 or 303 or 307 or 308;
}
