using Nuvio.Core.Models;

namespace Nuvio.Core.Services;

public interface ISubtitleService
{
    Task<IReadOnlyList<SubtitleTrack>> FetchAsync(string type, string id, CancellationToken cancellationToken);
}
