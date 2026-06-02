using Avalonia.Media;

namespace Nuvio.Desktop.Services;

public interface IDesktopImageLoader
{
    /// <summary>
    /// Loads (and caches) the image at <paramref name="sourceUrl"/>, decoded to a bounded pixel width
    /// (<paramref name="decodePixelWidth"/>) so high-DPI displays stay crisp without over-allocating bitmaps.
    /// A value &lt;= 0 decodes at the source resolution.
    /// </summary>
    Task<IImage?> LoadAsync(Uri sourceUrl, int decodePixelWidth, CancellationToken cancellationToken);
}
