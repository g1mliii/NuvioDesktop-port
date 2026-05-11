using System.Text;

namespace Nuvio.Core.Validation;

public static class PayloadSizeGuard
{
    public static void RequireWithinUtf8ByteLimit(string payload, long maxBytes, string label)
    {
        if (Encoding.UTF8.GetByteCount(payload) > maxBytes)
        {
            throw new NuvioValidationException($"{label} exceeds the configured {maxBytes} byte limit.");
        }
    }
}
