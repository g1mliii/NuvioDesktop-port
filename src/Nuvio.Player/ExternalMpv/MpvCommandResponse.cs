using System.Text.Json;

namespace Nuvio.Player.ExternalMpv;

public sealed record MpvCommandResponse(long RequestId, string Error, JsonElement? Data)
{
    public bool IsSuccess => string.Equals(Error, "success", StringComparison.OrdinalIgnoreCase);
}
