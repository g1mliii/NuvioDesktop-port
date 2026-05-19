using System.Net;
using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Services;
using Nuvio.Core.Tests.Net;

namespace Nuvio.Core.Tests.Services;

public sealed class StreamResolverTests
{
    [Fact]
    public async Task ResolveAsync_OneAddonErroring_DoesNotBlockOthers()
    {
        var (resolver, handler, repository) = CreateResolver();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");
        await InstallAddonAsync(repository, "addon.broken", "broken.example.test");

        handler.AddResponse("alpha.example.test", "/stream/movie/tt001.json", """
            { "streams": [
              { "name": "Alpha 1080p", "url": "https://cdn.example.test/alpha.m3u8", "quality": "1080p" }
            ] }
            """);
        handler.AddError("broken.example.test", HttpStatusCode.ServiceUnavailable);

        var groups = await resolver.ResolveAsync("movie", "tt001", CancellationToken.None);

        Assert.Equal(2, groups.Count);
        var success = groups.Single(group => group.AddonId == "addon.alpha");
        var failure = groups.Single(group => group.AddonId == "addon.broken");
        Assert.Single(success.Streams);
        Assert.Empty(failure.Streams);
        Assert.NotNull(failure.Error);
    }

    [Fact]
    public async Task ResolveAsync_FiltersInfoHashAndExternalOnlyStreams()
    {
        var (resolver, handler, repository) = CreateResolver();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");

        handler.AddResponse("alpha.example.test", "/stream/movie/tt001.json", """
            { "streams": [
              { "name": "Direct", "url": "https://cdn.example.test/direct.m3u8" },
              { "name": "Torrent", "infoHash": "abc123" }
            ] }
            """);

        var groups = await resolver.ResolveAsync("movie", "tt001", CancellationToken.None);
        var group = Assert.Single(groups);

        Assert.Single(group.Streams);
        Assert.Equal(1, group.FilteredCount);
        Assert.Equal("Direct", group.Streams[0].Title);
    }

    [Fact]
    public async Task ResolveAsync_PreservesProxyHeadersOnStreamSources()
    {
        var (resolver, handler, repository) = CreateResolver();
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test");

        handler.AddResponse("alpha.example.test", "/stream/movie/tt001.json", """
            { "streams": [
              {
                "name": "With Headers",
                "url": "https://cdn.example.test/video.mp4",
                "behaviorHints": {
                  "proxyHeaders": {
                    "request": { "Authorization": "Bearer secret", "User-Agent": "X" }
                  }
                }
              }
            ] }
            """);

        var groups = await resolver.ResolveAsync("movie", "tt001", CancellationToken.None);
        var stream = groups[0].Streams[0];

        Assert.Equal(2, stream.Headers.Count);
        Assert.Equal("Bearer secret", stream.Headers["Authorization"]);
    }

    [Fact]
    public async Task ResolveAsync_AttachesSharedSubtitlesAcrossGroups()
    {
        var (resolver, handler, repository) = CreateResolver(supportsSubtitles: true);
        await InstallAddonAsync(repository, "addon.alpha", "alpha.example.test", subtitleSupport: true);

        handler.AddResponse("alpha.example.test", "/stream/movie/tt001.json", """
            { "streams": [
              { "name": "Direct", "url": "https://cdn.example.test/direct.mp4" }
            ] }
            """);
        handler.AddResponse("alpha.example.test", "/subtitles/movie/tt001.json", """
            { "subtitles": [
              { "id": "en1", "lang": "en", "url": "https://cdn.example.test/en.vtt" }
            ] }
            """);

        var groups = await resolver.ResolveAsync("movie", "tt001", CancellationToken.None);
        var stream = groups[0].Streams[0];

        Assert.Single(stream.Subtitles);
        Assert.Equal("en", stream.Subtitles[0].Language);
    }

    private static async Task InstallAddonAsync(
        IAddonRepository repository,
        string id,
        string host,
        bool subtitleSupport = false)
    {
        var resources = new List<AddonResource>
        {
            new("stream", ["movie"], Array.Empty<string>())
        };
        if (subtitleSupport)
        {
            resources.Add(new AddonResource("subtitles", ["movie"], Array.Empty<string>()));
        }

        var manifest = new AddonManifest(
            Id: id,
            Name: id,
            Description: string.Empty,
            Version: "1.0.0",
            LogoUrl: null,
            Resources: resources,
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

    private static (StreamResolver Resolver, RoutingStubHandler Handler, IAddonRepository Repository) CreateResolver(bool supportsSubtitles = false)
    {
        var handler = new RoutingStubHandler();
        var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(httpClient, throttler: null, diagnostics: null, timeProvider: null, backoffDelays: [TimeSpan.Zero]);
        var repository = new InMemoryAddonRepository();
        var cache = new MetadataCache();
        var subtitleService = new SubtitleService(repository, nuvio, cache);
        var resolver = new StreamResolver(repository, nuvio, subtitleService);
        return (resolver, handler, repository);
    }
}
