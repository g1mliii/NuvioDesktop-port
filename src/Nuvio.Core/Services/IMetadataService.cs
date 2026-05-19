using Nuvio.Core.Models;

namespace Nuvio.Core.Services;

public interface IMetadataService
{
    Task<MediaDetails?> GetDetailsAsync(string type, string id, CancellationToken cancellationToken);
}
