using System.Net;
using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Net;
using Nuvio.Core.Services;
using Nuvio.Core.Tests.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests.Services;

public sealed class AddonServiceTests
{
    private const string ValidManifest = """
        {
          "id": "org.nuvio.fixture",
          "name": "Fixture Addon",
          "version": "1.0.0",
          "resources": ["catalog", "meta", "stream"],
          "types": ["movie"],
          "catalogs": [
            { "type": "movie", "id": "top", "name": "Top" }
          ]
        }
        """;

    [Fact]
    public async Task InstallAsync_RegistersAddonFromValidManifest()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidManifest);
        var service = CreateService(handler);

        var addon = await service.InstallAsync("https://addons.example.test/", CancellationToken.None);

        Assert.Equal("org.nuvio.fixture", addon.Id);
        Assert.True(addon.Enabled);
        Assert.NotNull(addon.Manifest);
        Assert.Equal("Fixture Addon", addon.Manifest!.Name);
        Assert.Equal("https://addons.example.test/manifest.json", addon.ManifestUrl.ToString());
    }

    [Fact]
    public async Task InstallAsync_UsesRedirectedManifestUrlForTransportBase()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/manifest.json")
            {
                return Task.FromResult(StubHttpMessageHandler.CreateRedirect(
                    HttpStatusCode.Redirect,
                    new Uri("https://addons.example.test/v2/manifest.json")));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, ValidManifest));
        });
        var service = CreateService(handler);

        var addon = await service.InstallAsync("https://addons.example.test/", CancellationToken.None);

        Assert.Equal("https://addons.example.test/manifest.json", addon.ManifestUrl.ToString());
        Assert.Equal("https://addons.example.test/v2/manifest.json", addon.Manifest!.TransportUrl.ToString());
    }

    [Fact]
    public async Task InstallAsync_RejectsUnsafeScheme()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidManifest);
        var service = CreateService(handler);

        await Assert.ThrowsAsync<NuvioValidationException>(() =>
            service.InstallAsync("javascript:alert(1)", CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesEnabledFlag()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidManifest);
        var service = CreateService(handler);
        await service.InstallAsync("https://addons.example.test/", CancellationToken.None);

        var disabled = await service.SetEnabledAsync("org.nuvio.fixture", enabled: false, CancellationToken.None);
        Assert.False(disabled.Enabled);

        var enabled = await service.SetEnabledAsync("org.nuvio.fixture", enabled: true, CancellationToken.None);
        Assert.True(enabled.Enabled);
    }

    [Fact]
    public async Task RemoveAsync_RemovesInstalledAddon()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidManifest);
        var service = CreateService(handler);
        await service.InstallAsync("https://addons.example.test/", CancellationToken.None);

        var removed = await service.RemoveAsync("org.nuvio.fixture", CancellationToken.None);
        Assert.True(removed);

        var addons = await service.ListAsync(CancellationToken.None);
        Assert.Empty(addons);
    }

    [Fact]
    public async Task RefreshAsync_RecordsErrorOnFailure()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, ValidManifest)
                : StubHttpMessageHandler.CreateResponse(HttpStatusCode.NotFound, "missing"));
        });

        var service = CreateService(handler);
        var installed = await service.InstallAsync("https://addons.example.test/", CancellationToken.None);
        Assert.Null(installed.LastError);

        var refreshed = await service.RefreshAsync(installed.Id, CancellationToken.None);
        Assert.NotNull(refreshed.LastError);
        Assert.Contains("404", refreshed.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallAsync_CancelledManifestFetchDoesNotPinRefreshTask()
    {
        var attempts = 0;
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstRequestStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }

            return StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, ValidManifest);
        });

        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        var cancelledInstall = service.InstallAsync("https://addons.example.test/", cts.Token);

        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledInstall);

        var addon = await service.InstallAsync("https://addons.example.test/", CancellationToken.None);

        Assert.Equal("org.nuvio.fixture", addon.Id);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task InstallAsync_DedupesConcurrentFetchesForSameManifestUrl()
    {
        var attempts = 0;
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref attempts);
            firstRequestStarted.SetResult();
            await releaseResponse.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, ValidManifest);
        });

        var service = CreateService(handler);
        var first = service.InstallAsync("https://addons.example.test/", CancellationToken.None);
        var second = service.InstallAsync("https://addons.example.test/manifest.json", CancellationToken.None);

        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseResponse.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, attempts);
    }

    private static AddonService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(
            httpClient,
            throttler: null,
            diagnostics: null,
            timeProvider: null,
            backoffDelays: [TimeSpan.Zero]);
        return new AddonService(new InMemoryAddonRepository(), nuvio);
    }
}
