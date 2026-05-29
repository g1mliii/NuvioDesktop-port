using Nuvio.Core.Models;

namespace Nuvio.Core.Progress;

public interface IWatchProgressRepository
{
    Task<WatchProgress?> GetAsync(string mediaId, string? episodeId, CancellationToken cancellationToken);

    Task UpsertAsync(WatchProgress progress, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string mediaId, string? episodeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WatchProgress>> RecentAsync(int limit, CancellationToken cancellationToken);
}
