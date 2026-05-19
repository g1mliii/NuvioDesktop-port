using System.Text.Json;
using Nuvio.Core.Addons;
using Nuvio.Core.Models;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Subtitles;

public static class SubtitlePayloadParser
{
    public const long MaxPayloadBytes = 256 * 1024;

    public static IReadOnlyList<SubtitleTrack> Parse(string payload)
    {
        PayloadSizeGuard.RequireWithinUtf8ByteLimit(payload, MaxPayloadBytes, "Subtitle payload");

        using var document = ParseDocument(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new NuvioValidationException("Subtitle payload must be a JSON object.");
        }

        if (!document.RootElement.TryGetProperty("subtitles", out var subtitlesElement) ||
            subtitlesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SubtitleTrack>();
        }

        var subtitles = new List<SubtitleTrack>();
        foreach (var element in subtitlesElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!AddonUrlPolicy.TryCreatePlaybackUri(String(element, "url"), out var url) || url is null)
            {
                continue;
            }

            var language = String(element, "lang") ?? String(element, "language");
            var id = String(element, "id") ?? language ?? url.ToString();
            var label = String(element, "label") ?? String(element, "name") ?? language ?? id;
            subtitles.Add(new SubtitleTrack(
                Id: id,
                Label: label,
                Url: url,
                Language: language,
                IsDefault: Boolean(element, "default") || Boolean(element, "isDefault")));
        }

        return subtitles;
    }

    private static JsonDocument ParseDocument(string payload)
    {
        try
        {
            return JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            throw new NuvioValidationException($"Subtitle payload JSON is invalid: {ex.Message}");
        }
    }

    private static string? String(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool Boolean(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
}
