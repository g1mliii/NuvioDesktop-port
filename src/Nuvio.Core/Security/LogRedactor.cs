using System.Text.RegularExpressions;

namespace Nuvio.Core.Security;

public static partial class LogRedactor
{
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "cookie",
        "proxy-authorization",
        "x-api-key",
        "x-auth-token"
    };

    public static string RedactUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var withoutQuery = QueryStringRegex().Replace(value, "?<redacted>");
        return SensitiveHeaderRegex().Replace(withoutQuery, "$1: <redacted>");
    }

    public static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers) =>
        headers.ToDictionary(
            pair => pair.Key,
            pair => SensitiveHeaderNames.Contains(pair.Key) ? "<redacted>" : pair.Value,
            StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\?.*$", RegexOptions.Compiled)]
    private static partial Regex QueryStringRegex();

    [GeneratedRegex(@"(?im)^(authorization|cookie|proxy-authorization|x-api-key|x-auth-token)\s*:\s*.+$", RegexOptions.Compiled)]
    private static partial Regex SensitiveHeaderRegex();
}
