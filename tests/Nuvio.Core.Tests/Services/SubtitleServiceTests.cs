using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;
using Nuvio.Core.Net;
using Nuvio.Core.Services;
using Nuvio.Core.Subtitles;
using Nuvio.Core.Tests.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests.Services;

public sealed class SubtitleServiceTests
{
    [Fact]
    public async Task FetchAsync_MapsLangFieldAndDedupesByUrl()
    {
        var handler = new RoutingStubHandler();
        var (service, repository) = CreateService(handler);

        await InstallSubtitleAddonAsync(repository, "addon.alpha", "alpha.example.test");
        await InstallSubtitleAddonAsync(repository, "addon.beta", "beta.example.test");

        handler.AddResponse("alpha.example.test", "/subtitles/movie/tt001.json", """
            { "subtitles": [
              { "id": "en", "lang": "en", "url": "https://cdn.example.test/en.vtt" }
            ] }
            """);
        handler.AddResponse("beta.example.test", "/subtitles/movie/tt001.json", """
            { "subtitles": [
              { "id": "fr", "lang": "fr", "url": "https://cdn.example.test/fr.vtt" },
              { "id": "en-dup", "lang": "en", "url": "https://cdn.example.test/en.vtt" }
            ] }
            """);

        var subtitles = await service.FetchAsync("movie", "tt001", CancellationToken.None);

        Assert.Equal(2, subtitles.Count);
        Assert.Contains(subtitles, s => s.Language == "en");
        Assert.Contains(subtitles, s => s.Language == "fr");
    }

    [Fact]
    public void Parser_RejectsOversizedPayload()
    {
        var payload = "{\"subtitles\":[" + new string('x', (int)SubtitlePayloadParser.MaxPayloadBytes) + "]}";
        Assert.Throws<NuvioValidationException>(() => SubtitlePayloadParser.Parse(payload));
    }

    [Fact]
    public void Parser_SkipsUnsafeUrls()
    {
        var payload = """
            { "subtitles": [
              { "id": "javascript", "lang": "en", "url": "javascript:alert(1)" },
              { "id": "safe", "lang": "en", "url": "https://cdn.example.test/en.vtt" }
            ] }
            """;
        var subtitles = SubtitlePayloadParser.Parse(payload);

        Assert.Single(subtitles);
        Assert.Equal("safe", subtitles[0].Id);
    }

    private static async Task InstallSubtitleAddonAsync(IAddonRepository repository, string id, string host)
    {
        var manifest = new AddonManifest(
            Id: id,
            Name: id,
            Description: string.Empty,
            Version: "1.0.0",
            LogoUrl: null,
            Resources: [new AddonResource("subtitles", ["movie"], Array.Empty<string>())],
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

    private static (SubtitleService Service, IAddonRepository Repository) CreateService(RoutingStubHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(httpClient, throttler: null, diagnostics: null, timeProvider: null, backoffDelays: [TimeSpan.Zero]);
        var repository = new InMemoryAddonRepository();
        var cache = new MetadataCache();
        return (new SubtitleService(repository, nuvio, cache), repository);
    }
}
