using Nuvio.Core.Models;

namespace Nuvio.Core.Metadata;

public interface ITmdbClient
{
    bool IsConfigured { get; }
    Task<MediaDetails?> TryGetDetailsAsync(string type, string id, CancellationToken cancellationToken);
}
