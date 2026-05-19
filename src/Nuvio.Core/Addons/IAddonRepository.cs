namespace Nuvio.Core.Addons;

public interface IAddonRepository
{
    Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken);
    Task<ManagedAddon?> GetAsync(string id, CancellationToken cancellationToken);
    Task UpsertAsync(ManagedAddon addon, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
    Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken);
}
