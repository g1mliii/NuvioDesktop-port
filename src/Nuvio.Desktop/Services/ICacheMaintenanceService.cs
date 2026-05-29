namespace Nuvio.Desktop.Services;

public interface ICacheMaintenanceService
{
    Task ClearCacheAsync(CancellationToken cancellationToken);
    Task UpdateDiskCacheLimitAsync(long maxCacheBytes, CancellationToken cancellationToken);
}
