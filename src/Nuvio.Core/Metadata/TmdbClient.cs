using System.Text.Json;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Core.Net;

namespace Nuvio.Core.Metadata;

public sealed class TmdbClient : ITmdbClient
{
    public const string EnvironmentVariableName = "NUVIO_TMDB_API_KEY";
    private const long MaxResponseBytes = 64 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Uri BaseUri = new("https://api.themoviedb.org/3/");
    private static readonly Uri ImageBaseUri = new("https://image.tmdb.org/t/p/w780/");

    private readonly INuvioHttpClient _httpClient;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly string? _apiKey;

    public TmdbClient(
        INuvioHttpClient httpClient,
        string? apiKey,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public bool IsConfigured => _apiKey is not null;

    public static string? ResolveApiKey(string? settingsFilePath = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        if (string.IsNullOrWhiteSpace(settingsFilePath) || !File.Exists(settingsFilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(settingsFilePath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("Tmdb", out var tmdbElement) ||
                tmdbElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!tmdbElement.TryGetProperty("ApiKey", out var keyElement) ||
                keyElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = keyElement.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<MediaDetails?> TryGetDetailsAsync(string type, string id, CancellationToken cancellationToken)
    {
        if (_apiKey is null)
        {
            return null;
        }

        try
        {
            var tmdbId = id.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                ? await ResolveTmdbIdAsync(type, id, cancellationToken).ConfigureAwait(false)
                : id;

            if (string.IsNullOrEmpty(tmdbId))
            {
                return null;
            }

            var tmdbType = MapType(type);
            var uri = BuildUri($"{tmdbType}/{tmdbId}");
            var response = await SendAsync(uri, "tmdb-details", cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response.Body);
            return MapDetails(type, id, document.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure(ex);
            return null;
        }
    }

    private async Task<string?> ResolveTmdbIdAsync(string type, string imdbId, CancellationToken cancellationToken)
    {
        var uri = BuildUri($"find/{imdbId}", ("external_source", "imdb_id"));
        var response = await SendAsync(uri, "tmdb-find", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response.Body);
        var arrayName = MapType(type) == "tv" ? "tv_results" : "movie_results";
        if (!document.RootElement.TryGetProperty(arrayName, out var resultsElement) ||
            resultsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var result in resultsElement.EnumerateArray())
        {
            if (result.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
            {
                return idElement.GetInt64().ToString();
            }
        }

        return null;
    }

    private async Task<NuvioHttpResponse> SendAsync(Uri uri, string resourceKind, CancellationToken cancellationToken)
    {
        var request = new NuvioHttpRequest(
            Uri: uri,
            MaxBytes: MaxResponseBytes,
            Timeout: Timeout,
            ResourceKind: resourceKind);

        try
        {
            return await _httpClient.GetStringAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"TMDB {resourceKind} failed with {ex.GetType().Name}.", ex);
        }
    }

    private Uri BuildUri(string path, params (string Name, string Value)[] queries)
    {
        var builder = new UriBuilder(new Uri(BaseUri, path));
        var pairs = new List<string> { $"api_key={Uri.EscapeDataString(_apiKey!)}" };
        foreach (var (name, value) in queries)
        {
            pairs.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }

        builder.Query = string.Join('&', pairs);
        return builder.Uri;
    }

    private static string MapType(string type) =>
        type.Equals("series", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";

    private static MediaDetails MapDetails(string originalType, string originalId, JsonElement root)
    {
        var isSeries = MapType(originalType) == "tv";
        var name = String(root, isSeries ? "name" : "title") ?? String(root, "original_title") ?? originalId;
        var releaseDate = String(root, isSeries ? "first_air_date" : "release_date");
        var year = releaseDate?.Length >= 4 ? releaseDate[..4] : null;

        var genres = new List<string>();
        if (root.TryGetProperty("genres", out var genresElement) && genresElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genresElement.EnumerateArray())
            {
                var genreName = String(genre, "name");
                if (!string.IsNullOrWhiteSpace(genreName))
                {
                    genres.Add(genreName);
                }
            }
        }

        var ratings = new List<MediaExternalRating>();
        if (root.TryGetProperty("vote_average", out var voteElement) &&
            voteElement.ValueKind == JsonValueKind.Number &&
            voteElement.TryGetDouble(out var vote))
        {
            ratings.Add(new MediaExternalRating("tmdb", vote));
        }

        return new MediaDetails(
            Id: originalId,
            Type: originalType,
            Name: name,
            PosterUrl: ImagePath(root, "poster_path"),
            BackgroundUrl: ImagePath(root, "backdrop_path"),
            LogoUrl: null,
            Description: String(root, "overview"),
            ReleaseInfo: year,
            Runtime: ResolveRuntime(root, isSeries),
            Genres: genres,
            ExternalRatings: ratings,
            Cast: Array.Empty<MediaPerson>(),
            ProductionCompanies: Array.Empty<MediaCompany>(),
            Trailers: Array.Empty<MediaTrailer>(),
            Links: Array.Empty<MediaLink>(),
            Videos: Array.Empty<MediaVideo>());
    }

    private static string? ResolveRuntime(JsonElement root, bool isSeries)
    {
        if (isSeries)
        {
            if (root.TryGetProperty("episode_run_time", out var runtimes) &&
                runtimes.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in runtimes.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var minutes))
                    {
                        return $"{minutes} min";
                    }
                }
            }

            return null;
        }

        if (root.TryGetProperty("runtime", out var runtime) &&
            runtime.ValueKind == JsonValueKind.Number &&
            runtime.TryGetInt32(out var minutesValue))
        {
            return $"{minutesValue} min";
        }

        return null;
    }

    private static Uri? ImagePath(JsonElement root, string propertyName)
    {
        var value = String(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.TrimStart('/');
        var resolved = new Uri(ImageBaseUri, path);
        AddonUrlPolicy.ValidateRemoteUri(resolved, "TMDB image");
        return resolved;
    }

    private static string? String(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private void RecordFailure(Exception ex)
    {
        _diagnostics?.Record(new NetworkDiagnosticEvent(
            Timestamp: _timeProvider.GetUtcNow(),
            Kind: NetworkEventKind.Failure,
            Host: BaseUri.Host,
            StatusCode: null,
            DurationMs: null,
            AddonId: null,
            ResourceKind: "tmdb",
            Message: $"TMDB lookup failed: {ex.GetType().Name}"));
    }
}
