using System.Text.RegularExpressions;

namespace Nuvio.Core.Security;

public static partial class LogRedactor
{
    public static string RedactUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var withoutQuery = QueryStringRegex().Replace(value, "?<redacted>");
        return SensitiveHeaderRegex().Replace(withoutQuery, "$1: <redacted>");
    }

    [GeneratedRegex(@"\?.*$", RegexOptions.Compiled)]
    private static partial Regex QueryStringRegex();

    [GeneratedRegex(@"(?im)^(authorization|cookie|x-api-key|x-auth-token)\s*:\s*.+$", RegexOptions.Compiled)]
    private static partial Regex SensitiveHeaderRegex();
}
