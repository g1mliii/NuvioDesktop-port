using System.Text.Json;

namespace Nuvio.Player.ExternalMpv;

public static class MpvCommandSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string Serialize(IReadOnlyList<object?> command, long requestId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["command"] = command,
            ["request_id"] = requestId
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string SerializeLine(IReadOnlyList<object?> command, long requestId) =>
        Serialize(command, requestId) + "\n";

    public static MpvCommandResponse ParseResponse(JsonElement root)
    {
        var requestId = root.TryGetProperty("request_id", out var requestIdElement)
            ? requestIdElement.GetInt64()
            : 0;

        var error = root.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString() ?? "unknown"
            : "unknown";

        JsonElement? data = root.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : null;

        return new MpvCommandResponse(requestId, error, data);
    }
}
