using Nuvio.Core.Validation;

namespace Nuvio.Core.Net;

public static class RedirectPolicy
{
    public const int MaxRedirects = 3;

    public static void Validate(Uri original, Uri target, string resourceLabel)
    {
        if (!string.Equals(original.Host, target.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new NuvioValidationException($"{resourceLabel} redirect changed host; rejecting.");
        }

        if (original.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new NuvioValidationException($"{resourceLabel} redirect downgrades HTTPS to HTTP; rejecting.");
        }
    }

    public static bool IsRedirect(int statusCode) =>
        statusCode is 301 or 302 or 303 or 307 or 308;
}
