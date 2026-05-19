using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Core.Services;
using Nuvio.Core.Validation;
using Nuvio.Desktop.ViewModels;

namespace Nuvio.Desktop.Tests.ViewModels;

public sealed class AddonsPageViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesAddonsFromService()
    {
        var service = new FakeAddonService();
        service.Seed(BuildAddon("addon.one", "Alpha", enabled: true));
        service.Seed(BuildAddon("addon.two", "Beta", enabled: false));

        var viewModel = new AddonsPageViewModel(service);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Addons.Count);
        Assert.True(viewModel.HasAddons);
        Assert.True(viewModel.Addons[0].Enabled);
        Assert.False(viewModel.Addons[1].Enabled);
    }

    [Fact]
    public async Task InstallCommand_ReportsErrorWhenServiceFails()
    {
        var service = new FakeAddonService { InstallError = new NuvioValidationException("bad url") };
        var viewModel = new AddonsPageViewModel(service)
        {
            ManifestUrlInput = "https://invalid.example.test"
        };

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Contains("bad url", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleEnabledCommand_FlipsAddonState()
    {
        var service = new FakeAddonService();
        service.Seed(BuildAddon("addon.one", "Alpha", enabled: true));
        var viewModel = new AddonsPageViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ToggleEnabledCommand.ExecuteAsync(viewModel.Addons[0]);

        Assert.False(service.Storage["addon.one"].Enabled);
    }

    [Fact]
    public async Task RefreshDiagnostics_LimitsToTwentyEntries()
    {
        var service = new FakeAddonService();
        var diagnostics = new NetworkDiagnostics(capacity: 100);
        for (var i = 0; i < 40; i++)
        {
            diagnostics.Record(new NetworkDiagnosticEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Kind: NetworkEventKind.Success,
                Host: "host.test",
                StatusCode: 200,
                DurationMs: 1,
                AddonId: null,
                ResourceKind: "manifest",
                Message: $"event {i}"));
        }

        var viewModel = new AddonsPageViewModel(service, diagnostics);
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(20, viewModel.RecentEvents.Count);
    }

    [Fact]
    public async Task CancelPendingOperations_CancelsInFlightInstall()
    {
        var service = new BlockingInstallAddonService();
        var viewModel = new AddonsPageViewModel(service)
        {
            ManifestUrlInput = "https://addons.example.test/manifest.json"
        };

        var install = viewModel.InstallCommand.ExecuteAsync(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CancelPendingOperations();
        await install.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(service.InstallToken.IsCancellationRequested);
        Assert.False(viewModel.IsBusy);
    }

    private static ManagedAddon BuildAddon(string id, string name, bool enabled)
    {
        var manifest = new AddonManifest(
            Id: id,
            Name: name,
            Description: string.Empty,
            Version: "1.0.0",
            LogoUrl: null,
            Resources: [new AddonResource("catalog", ["movie"], Array.Empty<string>())],
            Types: ["movie"],
            IdPrefixes: Array.Empty<string>(),
            Catalogs: Array.Empty<AddonCatalog>(),
            BehaviorHints: new AddonBehaviorHints(),
            TransportUrl: new Uri($"https://{id}.example.test/manifest.json"));

        return new ManagedAddon(
            Id: id,
            ManifestUrl: new Uri($"https://{id}.example.test/manifest.json"),
            Manifest: manifest,
            Enabled: enabled,
            SortOrder: 0,
            LastError: null,
            LastRefreshedAt: DateTimeOffset.UtcNow);
    }

    private sealed class FakeAddonService : IAddonService
    {
        public Dictionary<string, ManagedAddon> Storage { get; } = new();
        public Exception? InstallError { get; set; }

        public void Seed(ManagedAddon addon) => Storage[addon.Id] = addon;

        public Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedAddon>>(Storage.Values.ToArray());

        public Task<ManagedAddon> InstallAsync(string rawManifestUrl, CancellationToken cancellationToken)
        {
            if (InstallError is not null)
            {
                throw InstallError;
            }

            var addon = BuildAddon("installed", "Installed", true);
            Storage[addon.Id] = addon;
            return Task.FromResult(addon);
        }

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Storage.Remove(id));

        public Task<ManagedAddon> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
        {
            var updated = Storage[id] with { Enabled = enabled };
            Storage[id] = updated;
            return Task.FromResult(updated);
        }

        public Task<ManagedAddon> RefreshAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Storage[id]);

        public Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class BlockingInstallAddonService : IAddonService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken InstallToken { get; private set; }

        public Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedAddon>>(Array.Empty<ManagedAddon>());

        public async Task<ManagedAddon> InstallAsync(string rawManifestUrl, CancellationToken cancellationToken)
        {
            InstallToken = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<ManagedAddon> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");

        public Task<ManagedAddon> RefreshAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");

        public Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
