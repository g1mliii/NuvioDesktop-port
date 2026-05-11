using System.Text.Json;
using Nuvio.Core.Addons;
using Nuvio.Core.Models;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Streams;

public static class StreamPayloadParser
{
    public static IReadOnlyList<StreamItem> Parse(string payload, string providerName, string providerAddonId)
    {
        PayloadSizeGuard.RequireWithinUtf8ByteLimit(
            payload,
            AddonFetchPolicy.Default.MaxStreamBytes,
            "Stream payload");

        using var document = ParseDocument(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new NuvioValidationException("Stream payload must be a JSON object.");
        }

        if (!document.RootElement.TryGetProperty("streams", out var streamsElement) ||
            streamsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<StreamItem>();
        }

        var streams = new List<StreamItem>();
        foreach (var streamElement in streamsElement.EnumerateArray())
        {
            if (streamElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AddonUrlPolicy.TryCreatePlaybackUri(String(streamElement, "url"), out var url);
            AddonUrlPolicy.TryCreatePlaybackUri(String(streamElement, "externalUrl"), out var externalUrl);
            var infoHash = String(streamElement, "infoHash");

            if (url is null && externalUrl is null && string.IsNullOrWhiteSpace(infoHash))
            {
                continue;
            }

            var hints = BehaviorHints(streamElement);
            var title = String(streamElement, "title");
            streams.Add(new StreamItem(
                Name: String(streamElement, "name") ?? title,
                Description: String(streamElement, "description") ?? title,
                Url: url,
                InfoHash: infoHash,
                FileIdx: Int(streamElement, "fileIdx"),
                ExternalUrl: externalUrl,
                ProviderName: providerName,
                ProviderAddonId: providerAddonId,
                QualityLabel: String(streamElement, "qualityLabel") ?? String(streamElement, "quality"),
                BehaviorHints: hints,
                Subtitles: Subtitles(streamElement)));
        }

        return streams;
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
            throw new NuvioValidationException($"Stream payload JSON is invalid: {ex.Message}");
        }
    }

    private static StreamBehaviorHints BehaviorHints(JsonElement streamElement)
    {
        if (!streamElement.TryGetProperty("behaviorHints", out var hintsElement) ||
            hintsElement.ValueKind != JsonValueKind.Object)
        {
            return new StreamBehaviorHints();
        }

        var proxyHeaders = ProxyHeaders(hintsElement);
        return new StreamBehaviorHints(
            BingeGroup: String(hintsElement, "bingeGroup"),
            NotWebReady: Boolean(hintsElement, "notWebReady") || proxyHeaders is not null,
            VideoSize: Long(hintsElement, "videoSize"),
            Filename: String(hintsElement, "filename"),
            ProxyHeaders: proxyHeaders);
    }

    private static StreamProxyHeaders? ProxyHeaders(JsonElement hintsElement)
    {
        if (!hintsElement.TryGetProperty("proxyHeaders", out var proxyElement) ||
            proxyElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var request = StringMap(proxyElement, "request");
        var response = StringMap(proxyElement, "response");
        if (request.Count == 0 && response.Count == 0)
        {
            return null;
        }

        return new StreamProxyHeaders(
            request.Count == 0 ? null : request,
            response.Count == 0 ? null : response);
    }

    private static IReadOnlyList<SubtitleTrack> Subtitles(JsonElement streamElement)
    {
        if (!streamElement.TryGetProperty("subtitles", out var subtitlesElement) ||
            subtitlesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SubtitleTrack>();
        }

        var subtitles = new List<SubtitleTrack>();
        foreach (var subtitleElement in subtitlesElement.EnumerateArray())
        {
            if (subtitleElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AddonUrlPolicy.TryCreatePlaybackUri(String(subtitleElement, "url"), out var subtitleUrl);
            var language = String(subtitleElement, "lang") ?? String(subtitleElement, "language");
            var id = String(subtitleElement, "id") ??
                language ??
                subtitleUrl?.ToString() ??
                $"subtitle-{subtitles.Count + 1}";
            var label = String(subtitleElement, "label") ??
                String(subtitleElement, "name") ??
                language ??
                id;

            subtitles.Add(new SubtitleTrack(
                Id: id,
                Label: label,
                Url: subtitleUrl,
                Language: language,
                IsDefault: Boolean(subtitleElement, "default") || Boolean(subtitleElement, "isDefault")));
        }

        return subtitles;
    }

    private static IReadOnlyDictionary<string, string> StringMap(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            var name = property.Name.Trim();
            var headerValue = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()?.Trim()
                : null;

            if (name.Length > 0 && !string.IsNullOrWhiteSpace(headerValue))
            {
                result[name] = headerValue;
            }
        }

        return result;
    }

    private static string? String(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool Boolean(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
    }

    private static long? Long(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }
}
