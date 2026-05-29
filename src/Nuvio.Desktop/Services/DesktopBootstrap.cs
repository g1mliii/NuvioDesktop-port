using System.Net.Http;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Metadata;
using Nuvio.Core.Net;
using Nuvio.Core.Progress;
using Nuvio.Core.Services;
using Nuvio.Core.Settings;
using Nuvio.Data;
using Nuvio.Data.Images;
using Nuvio.Data.Sqlite;
using Nuvio.Platform;

namespace Nuvio.Desktop.Services;

public sealed class DesktopServiceHost : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly PerHostThrottler _throttler;
    private int _disposed;

    internal DesktopServiceHost(
        HttpClient httpClient,
        PerHostThrottler throttler,
        IMetadataCache cache,
        ICatalogDataSource dataSource,
        IAddonService addonService,
        INetworkDiagnostics diagnostics,
        ISettingsStore? settingsStore = null,
        ICacheMaintenanceService? cacheMaintenance = null,
        DecodedImageMemoryCache? decodedImageMemoryCache = null,
        IDesktopImageLoader? imageLoader = null,
        IWatchProgressRepository? progressRepository = null)
    {
        _httpClient = httpClient;
        _throttler = throttler;
        DataSource = dataSource;
        AddonService = addonService;
        Diagnostics = diagnostics;
        SettingsStore = settingsStore;
        CacheMaintenance = cacheMaintenance;
        DecodedImageMemoryCache = decodedImageMemoryCache;
        ImageLoader = imageLoader;
        ProgressRepository = progressRepository;
    }

    public ICatalogDataSource DataSource { get; }

    public IAddonService AddonService { get; }

    public INetworkDiagnostics Diagnostics { get; }

    public ISettingsStore? SettingsStore { get; }

    public ICacheMaintenanceService? CacheMaintenance { get; }

    public DecodedImageMemoryCache? DecodedImageMemoryCache { get; }

    public IDesktopImageLoader? ImageLoader { get; }

    public IWatchProgressRepository? ProgressRepository { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        _httpClient.Dispose();
        _throttler.Dispose();
        DecodedImageMemoryCache?.Dispose();
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

        var paths = PlatformPaths.Current(
            StoragePlan.Default.DatabaseFileName,
            StoragePlan.Default.ImageCacheDirectoryName);
        var storage = SqliteStorage.Open(paths);
        var settingsStore = new SqliteSettingsStore(storage);
        var settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        var repository = new SqliteAddonRepository(storage);
        var addonService = new AddonService(repository, nuvioHttp, diagnostics);
        var catalogService = new CatalogService(repository, nuvioHttp, diagnostics);
        var tmdbKey = TmdbClient.ResolveApiKey(settingsFilePath);
        ITmdbClient? tmdbClient = tmdbKey is null
            ? null
            : new TmdbClient(nuvioHttp, tmdbKey, diagnostics);
        var cache = new SqliteMetadataCache(storage, settings.MetadataCacheTtl);
        var metadataService = new MetadataService(repository, nuvioHttp, cache, tmdbClient, diagnostics);
        var subtitleService = new SubtitleService(repository, nuvioHttp, cache, diagnostics);
        var streamResolver = new StreamResolver(repository, nuvioHttp, subtitleService, diagnostics);
        var diskImageCache = new DiskImageCache(
            storage,
            DiskImageCacheOptions.Default with { MaxCacheBytes = settings.ImageDiskCacheLimitBytes });
        var decodedImageCache = new DecodedImageMemoryCache(settings.DecodedImageMemoryItemLimit);
        var imageLoader = new CachedImageLoader(httpClient, diskImageCache, decodedImageCache);
        var cacheMaintenance = new DesktopCacheMaintenanceService(cache, diskImageCache, decodedImageCache);
        var progressRepository = new SqliteWatchProgressRepository(storage);

        var dataSource = new LiveCatalogDataSource(repository, catalogService, metadataService, streamResolver);

        return new DesktopServiceHost(
            httpClient,
            throttler,
            cache,
            dataSource,
            addonService,
            diagnostics,
            settingsStore,
            cacheMaintenance,
            decodedImageCache,
            imageLoader,
            progressRepository);
    }
}
