using System.Text.Json;
using Nuvio.Core.Media;
using Nuvio.Core.Models;
using Nuvio.Core.Streams;
using Nuvio.Desktop.Models;

namespace Nuvio.Desktop.Services;

public sealed class DesktopFixtureService : IDesktopFixtureService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IReadOnlyList<CatalogItem> _catalog;
    private readonly IReadOnlyDictionary<string, MediaDetails> _details;
    private readonly IReadOnlyList<StreamItem> _streams;

    public DesktopFixtureService()
        : this(itemCount: 48)
    {
    }

    public DesktopFixtureService(int itemCount)
    {
        _catalog = MediaPayloadParser.ParseCatalogItems(BuildCatalogPayload(Math.Max(1, itemCount)));
        _details = _catalog.ToDictionary(item => item.Id, BuildDetails, StringComparer.Ordinal);
        _streams = StreamPayloadParser.Parse(StreamPayload, "Fixture Addon", "addon:fixture")
            .Where(stream => stream.HasPlayableSource)
            .ToArray();
    }

    public Task<IReadOnlyList<FixtureHomeSection>> GetHomeSectionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FixtureHomeSection> sections =
        [
            new("Featured", _catalog.Take(10).ToArray()),
            new("Popular Movies", _catalog.Where(item => item.Type == "movie").Take(10).ToArray()),
            new("Series Picks", _catalog.Where(item => item.Type == "series").Take(10).ToArray())
        ];

        return Task.FromResult(sections);
    }

    public Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_catalog);
    }

    public Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = query.Trim();
        if (normalized.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<CatalogItem>>(Array.Empty<CatalogItem>());
        }

        var results = _catalog
            .Where(item =>
                item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.Type.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                (item.ReleaseInfo?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(40)
            .ToArray();

        return Task.FromResult<IReadOnlyList<CatalogItem>>(results);
    }

    public Task<FixtureDetailState> GetDetailsAsync(string mediaId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_details.TryGetValue(mediaId, out var details))
        {
            throw new InvalidOperationException($"Fixture media item '{mediaId}' was not found.");
        }

        return Task.FromResult(new FixtureDetailState(details, _streams));
    }

    private static MediaDetails BuildDetails(CatalogItem item)
    {
        var payload = JsonSerializer.Serialize(new
        {
            meta = new
            {
                id = item.Id,
                type = item.Type,
                name = item.Name,
                poster = item.PosterUrl?.ToString(),
                background = item.BackgroundUrl?.ToString(),
                logo = $"https://images.example.test/{item.Id}/logo.png",
                description = $"{item.Name} is deterministic Phase 3 fixture metadata adapted from the mobile details flow.",
                releaseInfo = item.ReleaseInfo,
                runtime = item.Type == "series" ? "45 min" : "112 min",
                genres = item.Type == "series"
                    ? new[] { "Drama", "Mystery" }
                    : new[] { "Adventure", "Sci-Fi" },
                externalRatings = new[]
                {
                    new { source = "imdb", value = 7.4 + (Math.Abs(item.Id.GetHashCode()) % 16) / 10d }
                },
                cast = new[]
                {
                    new { name = "Ada Example", role = "Lead", photo = $"https://images.example.test/{item.Id}/ada.jpg" },
                    new { name = "Lin Example", role = "Director", photo = $"https://images.example.test/{item.Id}/lin.jpg" }
                },
                productionCompanies = new[]
                {
                    new { name = "Fixture Studio", logo = $"https://images.example.test/{item.Id}/studio.png" }
                },
                trailers = new[]
                {
                    new { id = $"{item.Id}-trailer", key = "fixture", name = "Trailer", site = "YouTube", type = "Trailer", official = true }
                },
                links = new[]
                {
                    new { name = "Fixture homepage", category = "official", url = $"https://example.test/title/{item.Id}" }
                },
                videos = new[]
                {
                    new
                    {
                        id = $"{item.Id}:1:1",
                        title = "Pilot",
                        released = "2026-01-01",
                        thumbnail = $"https://images.example.test/{item.Id}/episode.jpg",
                        season = item.Type == "series" ? 1 : (int?)null,
                        episode = item.Type == "series" ? 1 : (int?)null,
                        overview = "First fixture video.",
                        runtime = item.Type == "series" ? 45 : 112
                    }
                }
            }
        }, JsonOptions);

        return MediaPayloadParser.ParseMediaDetails(payload);
    }

    private static string BuildCatalogPayload(int itemCount)
    {
        var metas = Enumerable.Range(1, itemCount)
            .Select(index =>
            {
                var type = index % 3 == 0 ? "series" : "movie";
                return new
                {
                    id = $"fixture-{index:0000}",
                    type,
                    name = type == "series" ? $"Fixture Series {index:0000}" : $"Fixture Movie {index:0000}",
                    poster = $"https://images.example.test/posters/{index:0000}.jpg",
                    background = $"https://images.example.test/backdrops/{index:0000}.jpg",
                    releaseInfo = (2026 - index % 10).ToString()
                };
            });

        return JsonSerializer.Serialize(new { metas }, JsonOptions);
    }

    private const string StreamPayload = """
        {
          "streams": [
            {
              "name": "Fixture 1080p",
              "description": "1080p | 2.0 GB | English",
              "quality": "1080p",
              "url": "https://cdn.example.test/video.m3u8?token=secret",
              "behaviorHints": {
                "bingeGroup": "fixture-group",
                "notWebReady": false,
                "videoSize": 2147483648,
                "filename": "fixture.mkv",
                "proxyHeaders": {
                  "request": {
                    "Authorization": "Bearer secret",
                    "User-Agent": "NuvioFixture"
                  },
                  "response": {
                    "X-Trace": "fixture"
                  }
                }
              },
              "subtitles": [
                {
                  "id": "sub-en",
                  "label": "English",
                  "lang": "en",
                  "url": "https://cdn.example.test/subtitles/en.vtt",
                  "default": true
                }
              ]
            },
            {
              "name": "External 720p",
              "externalUrl": "http://cdn.example.test/external.mp4",
              "qualityLabel": "720p"
            }
          ]
        }
        """;
}
