using Nuvio.Core.Security;

namespace Nuvio.Player.ExternalMpv;

public static class MpvCommandLogRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return LogRedactor.RedactUrl(value);
    }
}
