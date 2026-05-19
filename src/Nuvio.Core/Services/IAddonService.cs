using Nuvio.Core.Addons;

namespace Nuvio.Core.Services;

public interface IAddonService
{
    Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken);
    Task<ManagedAddon> InstallAsync(string rawManifestUrl, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
    Task<ManagedAddon> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken);
    Task<ManagedAddon> RefreshAsync(string id, CancellationToken cancellationToken);
    Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken);
}
