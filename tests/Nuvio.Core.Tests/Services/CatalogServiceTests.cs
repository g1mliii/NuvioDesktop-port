using System.Net;
using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Services;
using Nuvio.Core.Tests.Net;

namespace Nuvio.Core.Tests.Services;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task SearchAsync_FansOutAcrossEnabledAddonsAndDedupes()
    {
        var (service, handler, repository) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test", searchCatalog: true);
        await InstallAddonAsync(repository, "addon.beta", "beta.example.test", searchCatalog: true);

        handler.AddResponse("alpha.example.test", "/catalog/movie/top/search=batman.json",
            BuildCatalogPayload(("tt001", "movie", "Batman"), ("tt002", "movie", "Batman Returns")));
        handler.AddResponse("beta.example.test", "/catalog/movie/top/search=batman.json",
            BuildCatalogPayload(("tt001", "movie", "Batman"), ("tt003", "movie", "Batman Begins")));

        var results = await service.SearchAsync("batman", types: null, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(new[] { "tt001", "tt002", "tt003" }, results.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task SearchAsync_SkipsDisabledAddons()
    {
        var (service, handler, repository) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test", searchCatalog: true);
        await InstallAddonAsync(repository, "addon.disabled", "disabled.example.test", searchCatalog: true, enabled: false);

        handler.AddResponse("alpha.example.test", "/catalog/movie/top/search=hello.json",
            BuildCatalogPayload(("tt100", "movie", "Hello")));

        var results = await service.SearchAsync("hello", types: null, CancellationToken.None);

        Assert.Single(results);
        Assert.DoesNotContain(handler.Requests, request =>
            request.RequestUri!.Host.Equals("disabled.example.test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_OneAddonFailing_DoesNotBlockOthers()
    {
        var (service, handler, repository) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test", searchCatalog: true);
        await InstallAddonAsync(repository, "addon.broken", "broken.example.test", searchCatalog: true);

        handler.AddResponse("alpha.example.test", "/catalog/movie/top/search=foo.json",
            BuildCatalogPayload(("tt001", "movie", "Foo")));
        handler.AddError("broken.example.test", HttpStatusCode.InternalServerError);

        var results = await service.SearchAsync("foo", types: null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("tt001", results[0].Id);
    }

    [Fact]
    public async Task BrowseAsync_PaginatesWithSkip()
    {
        var (service, handler, repository) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");

        handler.AddResponse("alpha.example.test", "/catalog/movie/top.json",
            BuildCatalogPayload(("tt001", "movie", "One")));
        handler.AddResponse("alpha.example.test", "/catalog/movie/top/skip=100.json",
            BuildCatalogPayload(("tt002", "movie", "Two")));

        var first = await service.BrowseAsync("addon.alpha", "movie", "top", skip: 0, CancellationToken.None);
        var second = await service.BrowseAsync("addon.alpha", "movie", "top", skip: 100, CancellationToken.None);

        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal("tt001", first.Items[0].Id);
        Assert.Equal("tt002", second.Items[0].Id);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_HonorsCancellation()
    {
        var (service, handler, repository) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test", searchCatalog: true);
        handler.AddInfiniteDelay("alpha.example.test");

        using var cts = new CancellationTokenSource();
        var task = service.SearchAsync("foo", types: null, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task SearchAsync_BoundsConcurrentAddonRequestsAcrossHosts()
    {
        var (service, handler, repository) = CreateService();
        const int addonCount = 20;
        var inFlight = 0;
        var maxObserved = 0;

        for (var i = 0; i < addonCount; i++)
        {
            var index = i;
            var host = $"addon-{index}.example.test";
            await InstallAddonAsync(repository, $"addon.{index}", host, searchCatalog: true);
            handler.AddHandlerAsync(host, "/catalog/movie/top/search=load.json", async (_, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                UpdateMax(ref maxObserved, current);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
                    return StubHttpMessageHandler.CreateResponse(
                        HttpStatusCode.OK,
                        BuildCatalogPayload(($"tt{index:000}", "movie", $"Movie {index}")));
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            });
        }

        var results = await service.SearchAsync("load", types: null, CancellationToken.None);

        Assert.Equal(addonCount, results.Count);
        Assert.InRange(maxObserved, 1, 8);
    }

    private static async Task InstallAddonAsync(
        IAddonRepository repository,
        string id,
        string host,
        bool searchCatalog = false,
        bool enabled = true)
    {
        IReadOnlyList<AddonExtraProperty> extras = searchCatalog
            ? [new AddonExtraProperty("search", IsRequired: true, Array.Empty<string>(), null)]
            : Array.Empty<AddonExtraProperty>();

        var manifest = new AddonManifest(
            Id: id,
            Name: id,
            Description: string.Empty,
            Version: "1.0.0",
            LogoUrl: null,
            Resources: [new AddonResource("catalog", ["movie"], Array.Empty<string>())],
            Types: ["movie"],
            IdPrefixes: Array.Empty<string>(),
            Catalogs: [new AddonCatalog("movie", "top", "Top", extras)],
            BehaviorHints: new AddonBehaviorHints(),
            TransportUrl: new Uri($"https://{host}/manifest.json"));

        var addon = new ManagedAddon(
            Id: id,
            ManifestUrl: new Uri($"https://{host}/manifest.json"),
            Manifest: manifest,
            Enabled: enabled,
            SortOrder: 0,
            LastError: null,
            LastRefreshedAt: DateTimeOffset.UtcNow);

        await repository.UpsertAsync(addon, CancellationToken.None);
    }

    private static (CatalogService Service, RoutingStubHandler Handler, IAddonRepository Repository) CreateService()
    {
        var handler = new RoutingStubHandler();
        var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(httpClient, throttler: null, diagnostics: null, timeProvider: null, backoffDelays: [TimeSpan.Zero]);
        var repository = new InMemoryAddonRepository();
        return (new CatalogService(repository, nuvio), handler, repository);
    }

    private static string BuildCatalogPayload(params (string Id, string Type, string Name)[] items)
    {
        var entries = items.Select(item =>
            $"{{\"id\":\"{item.Id}\",\"type\":\"{item.Type}\",\"name\":\"{item.Name}\"}}");
        return "{\"metas\":[" + string.Join(",", entries) + "]}";
    }

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var observed = Volatile.Read(ref target);
            if (value <= observed)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, value, observed) == observed)
            {
                return;
            }
        }
    }
}
