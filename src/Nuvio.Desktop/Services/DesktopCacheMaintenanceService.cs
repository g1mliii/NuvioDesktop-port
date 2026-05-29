using Nuvio.Core.Metadata;
using Nuvio.Data.Images;

namespace Nuvio.Desktop.Services;

public sealed class DesktopCacheMaintenanceService : ICacheMaintenanceService
{
    private readonly IMetadataCache _metadataCache;
    private readonly DiskImageCache _diskImageCache;
    private readonly DecodedImageMemoryCache _decodedImageCache;

    public DesktopCacheMaintenanceService(
        IMetadataCache metadataCache,
        DiskImageCache diskImageCache,
        DecodedImageMemoryCache decodedImageCache)
    {
        _metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        _diskImageCache = diskImageCache ?? throw new ArgumentNullException(nameof(diskImageCache));
        _decodedImageCache = decodedImageCache ?? throw new ArgumentNullException(nameof(decodedImageCache));
    }

    public async Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        _metadataCache.Clear();
        _decodedImageCache.TrimAll();
        await _diskImageCache.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateDiskCacheLimitAsync(long maxCacheBytes, CancellationToken cancellationToken)
        => _diskImageCache.UpdateLimitAsync(maxCacheBytes, cancellationToken);
}
