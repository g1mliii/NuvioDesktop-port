namespace Nuvio.Core.Addons;

public sealed class InMemoryAddonRepository : IAddonRepository
{
    private readonly object _gate = new();
    private readonly List<ManagedAddon> _addons = [];

    public Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<ManagedAddon> snapshot = _addons
                .OrderBy(addon => addon.SortOrder)
                .ToArray();
            return Task.FromResult(snapshot);
        }
    }

    public Task<ManagedAddon?> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_addons.FirstOrDefault(addon =>
                addon.Id.Equals(id, StringComparison.Ordinal)));
        }
    }

    public Task UpsertAsync(ManagedAddon addon, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addon);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _addons.FindIndex(item => item.Id.Equals(addon.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _addons[index] = addon;
            }
            else
            {
                _addons.Add(addon);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _addons.FindIndex(addon => addon.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _addons.RemoveAt(index);
            return Task.FromResult(true);
        }
    }

    public Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            for (var index = 0; index < orderedIds.Count; index++)
            {
                var id = orderedIds[index];
                var position = _addons.FindIndex(addon => addon.Id.Equals(id, StringComparison.Ordinal));
                if (position >= 0)
                {
                    _addons[position] = _addons[position] with { SortOrder = index };
                }
            }
        }

        return Task.CompletedTask;
    }
}
