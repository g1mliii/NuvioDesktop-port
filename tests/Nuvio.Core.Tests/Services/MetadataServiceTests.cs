using System.Net;
using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Services;
using Nuvio.Core.Tests.Net;

namespace Nuvio.Core.Tests.Services;

public sealed class MetadataServiceTests
{
    [Fact]
    public async Task GetDetailsAsync_ReturnsAddonMetadata()
    {
        var (service, handler, repository, _) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");

        handler.AddResponse("alpha.example.test", "/meta/movie/tt001.json", """
            { "meta": {
              "id": "tt001", "type": "movie", "name": "Alpha", "poster": "https://images.example.test/p.jpg",
              "releaseInfo": "2024", "description": "hello"
            } }
            """);

        var details = await service.GetDetailsAsync("movie", "tt001", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Alpha", details!.Name);
        Assert.Equal("2024", details.ReleaseInfo);
    }

    [Fact]
    public async Task GetDetailsAsync_CachesSubsequentLookups()
    {
        var (service, handler, repository, _) = CreateService();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");

        handler.AddResponse("alpha.example.test", "/meta/movie/tt001.json", """
            { "meta": {
              "id": "tt001", "type": "movie", "name": "Cached", "poster": "https://images.example.test/p.jpg",
              "releaseInfo": "2024"
            } }
            """);

        var first = await service.GetDetailsAsync("movie", "tt001", CancellationToken.None);
        var second = await service.GetDetailsAsync("movie", "tt001", CancellationToken.None);

        Assert.Equal(first!.Name, second!.Name);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetDetailsAsync_FallsBackToTmdbWhenAddonMissing()
    {
        var (service, handler, repository, _) = CreateServiceWithTmdb();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");

        // Addon meta lacks releaseInfo (incomplete)
        handler.AddResponse("alpha.example.test", "/meta/movie/tt001.json", """
            { "meta": { "id": "tt001", "type": "movie", "name": "Partial" } }
            """);

        handler.AddHandler("api.themoviedb.org", "/3/find/tt001", _ =>
            StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, """
                { "movie_results": [{ "id": 99 }] }
                """));
        handler.AddHandler("api.themoviedb.org", "/3/movie/99", _ =>
            StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, """
                { "title": "TMDB Full", "release_date": "2024-05-01", "poster_path": "/x.jpg", "overview": "from tmdb" }
                """));

        var details = await service.GetDetailsAsync("movie", "tt001", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Partial", details!.Name);
        Assert.Equal("2024", details.ReleaseInfo);
        Assert.NotNull(details.PosterUrl);
    }

    private static async Task InstallAddonAsync(IAddonRepository repository, string id, string host)
    {
        var manifest = new AddonManifest(
            Id: id,
            Name: id,
            Description: string.Empty,
            Version: "1.0.0",
            LogoUrl: null,
            Resources: [new AddonResource("meta", ["movie"], Array.Empty<string>())],
            Types: ["movie"],
            IdPrefixes: Array.Empty<string>(),
            Catalogs: Array.Empty<AddonCatalog>(),
            BehaviorHints: new AddonBehaviorHints(),
            TransportUrl: new Uri($"https://{host}/manifest.json"));

        await repository.UpsertAsync(new ManagedAddon(
            Id: id,
            ManifestUrl: new Uri($"https://{host}/manifest.json"),
            Manifest: manifest,
            Enabled: true,
            SortOrder: 0,
            LastError: null,
            LastRefreshedAt: DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private static (MetadataService Service, RoutingStubHandler Handler, IAddonRepository Repository, MetadataCache Cache) CreateService()
    {
        var handler = new RoutingStubHandler();
        var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(httpClient, throttler: null, diagnostics: null, timeProvider: null, backoffDelays: [TimeSpan.Zero]);
        var repository = new InMemoryAddonRepository();
        var cache = new MetadataCache();
        return (new MetadataService(repository, nuvio, cache), handler, repository, cache);
    }

    private static (MetadataService Service, RoutingStubHandler Handler, IAddonRepository Repository, MetadataCache Cache) CreateServiceWithTmdb()
    {
        var handler = new RoutingStubHandler();
        var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(httpClient, throttler: null, diagnostics: null, timeProvider: null, backoffDelays: [TimeSpan.Zero]);
        var repository = new InMemoryAddonRepository();
        var cache = new MetadataCache();
        var tmdb = new TmdbClient(nuvio, apiKey: "test-key");
        return (new MetadataService(repository, nuvio, cache, tmdb), handler, repository, cache);
    }
}
