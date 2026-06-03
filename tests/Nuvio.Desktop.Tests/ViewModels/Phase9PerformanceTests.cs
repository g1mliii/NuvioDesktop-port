using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Nuvio.Core.Models;
using Nuvio.Data;
using Nuvio.Data.Images;
using Nuvio.Data.Sqlite;
using Nuvio.Desktop;
using Nuvio.Desktop.Models;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;
using Nuvio.Player;

namespace Nuvio.Desktop.Tests.ViewModels;

/// <summary>
/// Phase 9 deterministic performance/stability regressions (9.3, 9.4, 9.6, 9.12). These run in the default
/// Release suite and hard-fail — they assert virtualization caps, cache caps, cancellation correctness, and
/// engine-teardown invariants rather than absolute timing/memory budgets (those are reported, not enforced,
/// by the --perf-probe; see docs/perf/). The opt-in real-mpv start/stop loop lives in the gated player
/// integration tests; the loop here uses a fake engine so it stays deterministic with no native deps.
/// </summary>
public sealed class Phase9PerformanceTests
{
    private static readonly object SetupLock = new();
    private static HeadlessUnitTestSession? s_testSession;

    // A 1x1 PNG so DecodedImageMemoryCache can construct real Bitmaps under the headless Skia backend.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    // ---- 9.3 poster grid virtualization under stress ----

    [Fact]
    public async Task CatalogGrid_With5000Items_StaysVirtualizedDuringScroll()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var viewModel = MainWindowViewModel.CreateFixture(fixtures: new DesktopFixtureService(itemCount: 5_000));
            var window = new MainWindow
            {
                Width = 1180,
                Height = 760,
                DataContext = viewModel
            };

            try
            {
                window.Show();
                await viewModel.NavigateAsync(DesktopRoute.Catalog);
                window.UpdateLayout();

                Assert.NotNull(FindVisualDescendant<ListBox>(window, "CatalogRowsList"));
                Assert.Contains(window.GetVisualDescendants(), control => control is VirtualizingStackPanel);
                Assert.InRange(CountRealizedPosterCards(window), 1, 250);

                // Scroll to the bottom: virtualization must keep the realized-container count bounded rather
                // than realizing all 5,000 cards.
                var scrollViewer = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                Assert.NotNull(scrollViewer);
                scrollViewer!.Offset = new Vector(0, scrollViewer.Extent.Height);
                window.UpdateLayout();

                Assert.InRange(CountRealizedPosterCards(window), 1, 250);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }

    // ---- 9.4 / 9.12 image cache pressure ----

    [Fact]
    public async Task DecodedImageMemoryCache_UnderFlood_StaysAtLimit_AndEvictsLru()
    {
        await RunOnUiThreadAsync(async () =>
        {
            const int limit = 128;
            const int floodCount = 1_000;
            using var cache = new DecodedImageMemoryCache(limit);
            var opens = 0;

            for (var i = 0; i < floodCount; i++)
            {
                await cache.GetOrAddAsync(
                    $"key-{i}",
                    _ => { opens++; return Task.FromResult<Stream>(new MemoryStream(PngBytes)); },
                    CancellationToken.None);

                Assert.True(cache.Count <= limit, $"cache count {cache.Count} exceeded limit after {i + 1} inserts");
            }

            Assert.Equal(limit, cache.Count);
            Assert.Equal(floodCount, opens);

            // The oldest key was evicted long ago, so requesting it re-decodes (opener fires again).
            var opensBefore = opens;
            await cache.GetOrAddAsync(
                "key-0",
                _ => { opens++; return Task.FromResult<Stream>(new MemoryStream(PngBytes)); },
                CancellationToken.None);
            Assert.Equal(opensBefore + 1, opens);

            // A recently used key is still cached, so its opener must NOT fire.
            var opensAfterReinsert = opens;
            await cache.GetOrAddAsync(
                "key-999",
                _ => { opens++; return Task.FromResult<Stream>(new MemoryStream(PngBytes)); },
                CancellationToken.None);
            Assert.Equal(opensAfterReinsert, opens);
        });
    }

    [Fact]
    public async Task DiskImageCache_UnderPressure_StaysWithinByteCap_AndEvictsOldest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nuvio-phase9-{Guid.NewGuid():N}");
        var paths = PlatformPaths.For(
            PlatformInfoProvider.Current().Family,
            root,
            _ => null,
            StoragePlan.Default.DatabaseFileName,
            StoragePlan.Default.ImageCacheDirectoryName);

        try
        {
            var storage = SqliteStorage.Open(paths);
            const long cap = 50_000;
            var options = new DiskImageCacheOptions(MaxCacheBytes: cap, MaxImageBytes: 20L * 1024 * 1024);
            var cache = new DiskImageCache(storage, options);

            const int count = 200;
            var blob = new byte[1024];
            var firstUri = new Uri("https://images.example.test/0.jpg");
            var lastUri = new Uri($"https://images.example.test/{count - 1}.jpg");
            for (var i = 0; i < count; i++)
            {
                await cache.StoreAsync(
                    new Uri($"https://images.example.test/{i}.jpg"),
                    new MemoryStream(blob),
                    "image/jpeg",
                    CancellationToken.None);
            }

            // Each store enforces the limit, so the on-disk total never exceeds the cap.
            var onDiskBytes = Directory.EnumerateFiles(paths.ImageCacheDirectory, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
            Assert.True(onDiskBytes <= cap, $"on-disk {onDiskBytes} exceeded cap {cap}");

            // Oldest evicted, newest retained (LRU by last access).
            Assert.Null(await cache.GetAsync(firstUri, CancellationToken.None));
            Assert.NotNull(await cache.GetAsync(lastUri, CancellationToken.None));
        }
        finally
        {
            // Release the pooled SQLite handle before deleting; otherwise on Windows the open DB file makes
            // the recursive delete fail and leaks the temp dir.
            SqliteStorage.ReleasePooledConnections();
            TryDeleteDirectory(root);
        }
    }

    // ---- 9.6 / 9.12 cancellation storm ----

    [Fact]
    public async Task Search_CancellationStorm_NewestWins_NoStaleOverwrite()
    {
        var service = new StormSearchService();
        using var search = new SearchPageViewModel(new FixtureCatalogDataSource(service), _ => Task.CompletedTask);

        const int storm = 200;
        var tasks = new List<Task>(storm);
        for (var i = 0; i < storm; i++)
        {
            tasks.Add(search.SearchAsync($"query-{i:000}"));
        }

        // No task faults: cancelled searches complete via the swallowed OperationCanceledException.
        await Task.WhenAll(tasks);

        Assert.False(search.HasError);
        Assert.Equal(1, search.ResultCount);
        Assert.Contains("query-199", search.Rows.Single().Items.Single().Name, StringComparison.OrdinalIgnoreCase);
        // The storm cancelled the overwhelming majority of in-flight searches rather than letting them pile up.
        Assert.True(service.CancelledCount > 0, "expected stale searches to be cancelled");
    }

    // ---- 9.5 / 9.12 playback start/stop loop (deterministic, fake engine) ----

    [Fact]
    public async Task Player_StartStopLoop_DisposesEveryEngine_AndDoesNotLeak()
    {
        var factory = new LoopPlayerEngineFactory();
        var viewModel = new PlayerViewModel(factory, () => Task.CompletedTask, action => action());
        var source = CreateStreamSource();
        var details = CreateMediaDetails();

        const int loops = 50;
        GcSettle();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        for (var i = 0; i < loops; i++)
        {
            await viewModel.LoadAndPlayAsync(source, details, CancellationToken.None);
            await viewModel.StopCommand.ExecuteAsync(null);
        }

        await viewModel.DisposeAsync();

        Assert.True(factory.Created.Count >= loops, $"expected >= {loops} engines, saw {factory.Created.Count}");
        Assert.All(factory.Created, engine => Assert.True(engine.IsDisposed, "engine was not disposed across start/stop cycle"));

        GcSettle();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        Assert.True(after - before < 4_000_000, $"managed heap grew {after - before} bytes across {loops} playback cycles");
    }

    // ---- helpers ----

    private static int CountRealizedPosterCards(Visual root) =>
        root.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "PosterCard");

    private static void GcSettle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static StreamSource CreateStreamSource() =>
        new(
            "fixture-stream",
            new Uri("https://cdn.example.test/video.m3u8"),
            "Fixture 1080p",
            "1080p",
            new Dictionary<string, string>(),
            Array.Empty<SubtitleTrack>(),
            IsUserProvided: false);

    private static MediaDetails CreateMediaDetails() =>
        new(
            "fixture-0001",
            "movie",
            "Fixture Movie",
            null,
            null,
            null,
            "Fixture metadata",
            "2026",
            "112 min",
            Array.Empty<string>(),
            Array.Empty<MediaExternalRating>(),
            Array.Empty<MediaPerson>(),
            Array.Empty<MediaCompany>(),
            Array.Empty<MediaTrailer>(),
            Array.Empty<MediaLink>(),
            Array.Empty<MediaVideo>());

    private static void TryDeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        HeadlessUnitTestSession session;
        lock (SetupLock)
        {
            session = s_testSession ??= HeadlessUnitTestSession.StartNew(typeof(App));
        }

        return session.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
    }

    private static T? FindVisualDescendant<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    // Search service that delays (honoring cancellation) so a tight burst of queries cancels predecessors,
    // letting the test prove newest-wins under a cancellation storm.
    private sealed class StormSearchService : IDesktopFixtureService
    {
        private readonly DesktopFixtureService _inner = new(itemCount: 4);
        private int _cancelledCount;

        public int CancelledCount => Volatile.Read(ref _cancelledCount);

        public Task<IReadOnlyList<FixtureHomeSection>> GetHomeSectionsAsync(CancellationToken cancellationToken) =>
            _inner.GetHomeSectionsAsync(cancellationToken);

        public Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(CancellationToken cancellationToken) =>
            _inner.GetCatalogAsync(cancellationToken);

        public async Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelledCount);
                throw;
            }

            return
            [
                new CatalogItem(
                    $"search-{query}",
                    "movie",
                    $"Result for {query}",
                    new Uri("https://images.example.test/search.jpg"),
                    null,
                    "2026")
            ];
        }

        public Task<FixtureDetailState> GetDetailsAsync(string mediaId, CancellationToken cancellationToken) =>
            _inner.GetDetailsAsync(mediaId, cancellationToken);
    }

    private sealed class LoopPlayerEngineFactory : IPlayerEngineFactory
    {
        public List<LoopPlayerEngine> Created { get; } = [];

        public IPlayerEngine Create(PlayerOptions options)
        {
            var engine = new LoopPlayerEngine();
            Created.Add(engine);
            return engine;
        }
    }

    private sealed class LoopPlayerEngine : IPlayerEngine
    {
        private readonly Channel<PlayerEvent> _events = Channel.CreateUnbounded<PlayerEvent>();

        public bool IsDisposed { get; private set; }

        public Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken)
        {
            _events.Writer.TryWrite(new PlayerEvent.AvailabilityChanged(true, "fake", DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task LoadAsync(StreamSource source, CancellationToken cancellationToken)
        {
            _events.Writer.TryWrite(new PlayerEvent.FileLoaded(DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SelectAudioTrackAsync(string trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SelectSubtitleTrackAsync(string? trackId, CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<PlayerEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var playerEvent in _events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return playerEvent;
            }
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
