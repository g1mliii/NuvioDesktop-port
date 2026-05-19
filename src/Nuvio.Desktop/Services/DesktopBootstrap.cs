using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Metadata;
using Nuvio.Core.Net;
using Nuvio.Core.Services;

namespace Nuvio.Desktop.Services;

public sealed class DesktopServiceHost : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly PerHostThrottler _throttler;
    private readonly MetadataCache _cache;
    private int _disposed;

    internal DesktopServiceHost(
        HttpClient httpClient,
        PerHostThrottler throttler,
        MetadataCache cache,
        ICatalogDataSource dataSource,
        IAddonService addonService,
        INetworkDiagnostics diagnostics)
    {
        _httpClient = httpClient;
        _throttler = throttler;
        _cache = cache;
        DataSource = dataSource;
        AddonService = addonService;
        Diagnostics = diagnostics;
    }

    public ICatalogDataSource DataSource { get; }

    public IAddonService AddonService { get; }

    public INetworkDiagnostics Diagnostics { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        _httpClient.Dispose();
        _throttler.Dispose();
        _cache.Clear();
        return ValueTask.CompletedTask;
    }
}

public static class DesktopBootstrap
{
    public static DesktopServiceHost BuildLive(string settingsFilePath)
    {
        var diagnostics = new NetworkDiagnostics();
        var throttler = new PerHostThrottler();
        var httpClient = new HttpClient(NuvioHttpClient.CreateHandler(), disposeHandler: true);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(NuvioHttpClient.UserAgent);

        var nuvioHttp = new NuvioHttpClient(httpClient, throttler, diagnostics);

        var repository = new InMemoryAddonRepository();
        var addonService = new AddonService(repository, nuvioHttp, diagnostics);
        var catalogService = new CatalogService(repository, nuvioHttp, diagnostics);
        var tmdbKey = TmdbClient.ResolveApiKey(settingsFilePath);
        ITmdbClient? tmdbClient = tmdbKey is null
            ? null
            : new TmdbClient(nuvioHttp, tmdbKey, diagnostics);
        var cache = new MetadataCache();
        var metadataService = new MetadataService(repository, nuvioHttp, cache, tmdbClient, diagnostics);
        var subtitleService = new SubtitleService(repository, nuvioHttp, cache, diagnostics);
        var streamResolver = new StreamResolver(repository, nuvioHttp, subtitleService, diagnostics);

        var dataSource = new LiveCatalogDataSource(repository, catalogService, metadataService, streamResolver);

        return new DesktopServiceHost(httpClient, throttler, cache, dataSource, addonService, diagnostics);
    }
}
