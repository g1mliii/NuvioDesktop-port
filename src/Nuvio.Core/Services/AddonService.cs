using System.Collections.Concurrent;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Services;

public sealed class AddonService : IAddonService
{
    private readonly IAddonRepository _repository;
    private readonly INuvioHttpClient _httpClient;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Uri, Lazy<Task<AddonManifest>>> _activeRefreshes = new();

    public AddonService(
        IAddonRepository repository,
        INuvioHttpClient httpClient,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken) =>
        _repository.ListAsync(cancellationToken);

    public async Task<ManagedAddon> InstallAsync(string rawManifestUrl, CancellationToken cancellationToken)
    {
        var manifestUrl = AddonUrlPolicy.NormalizeManifestUrl(rawManifestUrl);
        var manifest = await FetchManifestAsync(manifestUrl, addonId: null, cancellationToken).ConfigureAwait(false);

        var existing = await _repository.GetAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
        var existingList = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var sortOrder = existing?.SortOrder ?? existingList.Count;

        var addon = new ManagedAddon(
            Id: manifest.Id,
            ManifestUrl: manifestUrl,
            Manifest: manifest,
            Enabled: existing?.Enabled ?? true,
            SortOrder: sortOrder,
            LastError: null,
            LastRefreshedAt: _timeProvider.GetUtcNow());

        await _repository.UpsertAsync(addon, cancellationToken).ConfigureAwait(false);
        return addon;
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken) =>
        _repository.RemoveAsync(id, cancellationToken);

    public async Task<ManagedAddon> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NuvioValidationException($"Addon '{id}' is not installed.");
        var updated = existing with { Enabled = enabled };
        await _repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<ManagedAddon> RefreshAsync(string id, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NuvioValidationException($"Addon '{id}' is not installed.");

        ManagedAddon updated;
        try
        {
            var manifest = await FetchManifestAsync(existing.ManifestUrl, existing.Id, cancellationToken).ConfigureAwait(false);
            updated = existing with
            {
                Manifest = manifest,
                LastError = null,
                LastRefreshedAt = _timeProvider.GetUtcNow()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            updated = existing with
            {
                LastError = ex.Message,
                LastRefreshedAt = _timeProvider.GetUtcNow()
            };
        }

        await _repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken) =>
        _repository.ReorderAsync(orderedIds, cancellationToken);

    private async Task<AddonManifest> FetchManifestAsync(Uri manifestUrl, string? addonId, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            return await FetchManifestCoreAsync(manifestUrl, addonId, cancellationToken).ConfigureAwait(false);
        }

        Lazy<Task<AddonManifest>>? createdRefresh = null;
        var refresh = _activeRefreshes.GetOrAdd(manifestUrl, url =>
        {
            createdRefresh = new Lazy<Task<AddonManifest>>(
                async () =>
                {
                    try
                    {
                        return await FetchManifestCoreAsync(url, addonId, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        RemoveActiveRefresh(url, createdRefresh);
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication);
            return createdRefresh;
        });

        return await refresh.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RemoveActiveRefresh(Uri manifestUrl, Lazy<Task<AddonManifest>>? refresh)
    {
        if (refresh is null)
        {
            return;
        }

        var pair = new KeyValuePair<Uri, Lazy<Task<AddonManifest>>>(manifestUrl, refresh);
        ((ICollection<KeyValuePair<Uri, Lazy<Task<AddonManifest>>>>)_activeRefreshes).Remove(pair);
    }

    private async Task<AddonManifest> FetchManifestCoreAsync(
        Uri manifestUrl,
        string? addonId,
        CancellationToken cancellationToken)
    {
        var request = new NuvioHttpRequest(
            Uri: manifestUrl,
            MaxBytes: AddonFetchPolicy.Default.MaxManifestBytes,
            Timeout: AddonFetchPolicy.Default.Timeout,
            ResourceKind: "manifest",
            AddonId: addonId);

        var response = await _httpClient.GetStringAsync(request, cancellationToken).ConfigureAwait(false);
        return AddonManifestParser.Parse(response.FinalUri, response.Body);
    }
}
