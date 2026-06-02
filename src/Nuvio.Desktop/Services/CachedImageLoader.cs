using System.Net.Http;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nuvio.Core.Net;
using Nuvio.Data.Images;

namespace Nuvio.Desktop.Services;

public sealed class CachedImageLoader : IDesktopImageLoader
{
    private readonly HttpClient _httpClient;
    private readonly DiskImageCache _diskCache;
    private readonly DecodedImageMemoryCache _decodedCache;

    public CachedImageLoader(
        HttpClient httpClient,
        DiskImageCache diskCache,
        DecodedImageMemoryCache decodedCache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        _decodedCache = decodedCache ?? throw new ArgumentNullException(nameof(decodedCache));
    }

    public async Task<IImage?> LoadAsync(Uri sourceUrl, int decodePixelWidth, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        var entry = await _diskCache.GetAsync(sourceUrl, cancellationToken).ConfigureAwait(false)
            ?? await DownloadAsync(sourceUrl, cancellationToken).ConfigureAwait(false);

        // Decode width is part of the cache key so the poster (small) and backdrop (large) decodes of the
        // same source URL don't collide, and a full-resolution decode is never silently reused for a tile.
        var cacheKey = decodePixelWidth > 0 ? $"{entry.CacheKey}@w{decodePixelWidth}" : entry.CacheKey;
        return await _decodedCache.GetOrAddAsync(
            cacheKey,
            token => Task.FromResult<Stream>(File.OpenRead(entry.FilePath)),
            cancellationToken,
            decode: decodePixelWidth > 0
                ? stream => Bitmap.DecodeToWidth(stream, decodePixelWidth)
                : null).ConfigureAwait(false);
    }

    private async Task<DiskImageCacheEntry> DownloadAsync(Uri sourceUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.UserAgent.ParseAdd(NuvioHttpClient.UserAgent);
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/png,image/jpeg,image/*;q=0.8");

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await _diskCache
            .StoreAsync(sourceUrl, stream, response.Content.Headers.ContentType?.MediaType, cancellationToken)
            .ConfigureAwait(false);
    }
}
