using System.Globalization;

namespace Nuvio.Core.Media;

/// <summary>
/// Maps an addon media <c>type</c> token to a human-friendly, pluralised display label.
/// Mirrors the upstream home_catalog_default_title type formatting.
/// </summary>
public static class MediaTypeLabel
{
    public static string ForType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return string.Empty;
        }

        var trimmed = type.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "movie" => "Movies",
            "series" => "Series",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant()),
        };
    }
}
