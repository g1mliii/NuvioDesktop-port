using Microsoft.Data.Sqlite;
using Nuvio.Core.Addons;
using Nuvio.Core.Models;
using Nuvio.Core.Settings;
using Nuvio.Data.Images;
using Nuvio.Data.Sqlite;
using Nuvio.Platform;

namespace Nuvio.Data.Tests;

public sealed class SqliteStorageTests
{
    [Fact]
    public void Open_AppliesMigrationsIdempotently()
    {
        using var paths = TempPlatformPaths.Create();

        SqliteStorage.Open(paths);
        var storage = SqliteStorage.Open(paths);

        using var connection = storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('schema_migrations', 'settings', 'addons', 'watch_progress', 'metadata_cache', 'image_cache_entries');
            """;
        Assert.Equal(6L, (long)command.ExecuteScalar()!);

        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task Open_RestoresPreMigrationBackupWhenMigrationFails()
    {
        using var paths = TempPlatformPaths.Create();
        var storage = SqliteStorage.Open(paths);
        var settingsStore = new SqliteSettingsStore(storage);
        await settingsStore.SaveAsync(DesktopSettings.Default with { InitialVolume = 33 }, CancellationToken.None);

        var brokenRunner = new SqliteMigrationRunner(
        [
            new SqliteMigration(2, "002_broken", "CREATE TABLE broken_table (")
        ]);

        Assert.Throws<SqliteException>(() => SqliteStorage.Open(paths, brokenRunner));

        var restored = SqliteStorage.Open(paths);
        var loaded = await new SqliteSettingsStore(restored).LoadAsync(CancellationToken.None);
        Assert.Equal(33, loaded.InitialVolume);
        Assert.NotEmpty(Directory.GetFiles(paths.BackupDirectory, "pre-migration-*.sqlite3"));
    }

    [Fact]
    public void Open_MovesCorruptDatabaseAsideAndCreatesCleanDatabase()
    {
        using var paths = TempPlatformPaths.Create();
        Directory.CreateDirectory(paths.AppDataDirectory);
        File.WriteAllText(paths.DatabasePath, "not a sqlite database");

        var storage = SqliteStorage.Open(paths);

        Assert.NotEmpty(Directory.GetFiles(paths.AppDataDirectory, "*.corrupt-*"));
        using var connection = storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task SettingsStore_ReturnsDefaultsAndPersistsRoundTrip()
    {
        using var paths = TempPlatformPaths.Create();
        var storage = SqliteStorage.Open(paths);
        var store = new SqliteSettingsStore(storage);

        Assert.Equal(DesktopSettings.Default, await store.LoadAsync(CancellationToken.None));

        var settings = DesktopSettings.Default with
        {
            Theme = ThemeMode.Dark,
            InitialVolume = 42,
            ImageDiskCacheLimitBytes = 128L * 1024L * 1024L,
            DecodedImageMemoryItemLimit = 64
        };
        await store.SaveAsync(settings, CancellationToken.None);

        Assert.Equal(settings.Normalize(), await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddonRepository_PersistsAndReordersAddons()
    {
        using var paths = TempPlatformPaths.Create();
        var repository = new SqliteAddonRepository(SqliteStorage.Open(paths));

        await repository.UpsertAsync(BuildAddon("addon.one", "One", 0), CancellationToken.None);
        await repository.UpsertAsync(BuildAddon("addon.two", "Two", 1), CancellationToken.None);
        await repository.ReorderAsync(["addon.two", "addon.one"], CancellationToken.None);

        var addons = await repository.ListAsync(CancellationToken.None);
        Assert.Equal(["addon.two", "addon.one"], addons.Select(addon => addon.Id).ToArray());
        Assert.Equal("Two", addons[0].Manifest?.Name);
    }

    [Fact]
    public async Task WatchProgressRepository_SavesLoadsAndNormalizes()
    {
        using var paths = TempPlatformPaths.Create();
        var repository = new SqliteWatchProgressRepository(SqliteStorage.Open(paths));

        await repository.UpsertAsync(
            new WatchProgress(
                "movie-1",
                null,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMinutes(10),
                0,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.Null(await repository.GetAsync("movie-1", null, CancellationToken.None));

        await repository.UpsertAsync(
            new WatchProgress(
                "movie-1",
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(10),
                0,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var loaded = await repository.GetAsync("movie-1", null, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(50d, loaded.Percent, precision: 3);
        Assert.Single(await repository.RecentAsync(10, CancellationToken.None));
    }

    [Fact]
    public void MetadataCache_ExpiresByTtl()
    {
        using var paths = TempPlatformPaths.Create();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-29T00:00:00Z"));
        var cache = new SqliteMetadataCache(SqliteStorage.Open(paths), TimeSpan.FromMinutes(5), time);
        var details = BuildDetails("movie-1");

        cache.SetDetails("movie", "movie-1", details);
        Assert.True(cache.TryGetDetails("movie", "movie-1", out _));

        time.Advance(TimeSpan.FromMinutes(6));

        Assert.False(cache.TryGetDetails("movie", "movie-1", out _));
    }

    [Fact]
    public async Task DiskImageCache_EvictsLeastRecentlyUsedEntries()
    {
        using var paths = TempPlatformPaths.Create();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-29T00:00:00Z"));
        var cache = new DiskImageCache(
            SqliteStorage.Open(paths),
            new DiskImageCacheOptions(MaxCacheBytes: 10, MaxImageBytes: 10),
            time);

        await cache.StoreAsync(new Uri("https://images.example.test/one.png"), Bytes(5), "image/png", CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await cache.StoreAsync(new Uri("https://images.example.test/two.png"), Bytes(5), "image/png", CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        var third = await cache.StoreAsync(new Uri("https://images.example.test/three.png"), Bytes(5), "image/png", CancellationToken.None);

        Assert.Null(await cache.GetAsync(new Uri("https://images.example.test/one.png"), CancellationToken.None));
        Assert.NotNull(await cache.GetAsync(new Uri("https://images.example.test/two.png"), CancellationToken.None));
        Assert.True(File.Exists(third.FilePath));
    }

    [Fact]
    public async Task DiskImageCache_RemovesTempFileWhenStoreFails()
    {
        using var paths = TempPlatformPaths.Create();
        var cache = new DiskImageCache(SqliteStorage.Open(paths));

        await Assert.ThrowsAsync<IOException>(() => cache.StoreAsync(
            new Uri("https://images.example.test/failing.png"),
            new FailingReadStream(),
            "image/png",
            CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(paths.ImageCacheDirectory, "*.tmp"));
    }

    private static Stream Bytes(int count) => new MemoryStream(Enumerable.Repeat((byte)'x', count).ToArray());

    private sealed class FailingReadStream : Stream
    {
        private bool _returnedFirstByte;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_returnedFirstByte)
            {
                _returnedFirstByte = true;
                buffer[offset] = 1;
                return 1;
            }

            throw new IOException("Simulated read failure.");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_returnedFirstByte)
            {
                _returnedFirstByte = true;
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }

            throw new IOException("Simulated read failure.");
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private static ManagedAddon BuildAddon(string id, string name, int sortOrder)
    {
        var manifestUrl = new Uri($"https://{id}.example.test/manifest.json");
        var manifest = new AddonManifest(
            id,
            name,
            string.Empty,
            "1.0.0",
            null,
            [new AddonResource("catalog", ["movie"], [])],
            ["movie"],
            [],
            [],
            new AddonBehaviorHints(),
            manifestUrl);

        return new ManagedAddon(id, manifestUrl, manifest, Enabled: true, sortOrder, null, DateTimeOffset.UtcNow);
    }

    private static MediaDetails BuildDetails(string id) =>
        new(
            id,
            "movie",
            "Movie",
            null,
            null,
            null,
            "Description",
            "2026",
            "100 min",
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private sealed class TempPlatformPaths : IPlatformPaths, IDisposable
    {
        private TempPlatformPaths(string root)
        {
            Root = root;
            AppDataDirectory = Path.Combine(root, "data");
            CacheDirectory = Path.Combine(root, "cache");
            LogDirectory = Path.Combine(root, "data", "logs");
            DatabasePath = Path.Combine(root, "data", StoragePlan.Default.DatabaseFileName);
            BackupDirectory = Path.Combine(root, "data", "backups");
            ImageCacheDirectory = Path.Combine(root, "cache", StoragePlan.Default.ImageCacheDirectoryName);
        }

        private string Root { get; }

        public string AppDataDirectory { get; }

        public string CacheDirectory { get; }

        public string LogDirectory { get; }

        public string DatabasePath { get; }

        public string BackupDirectory { get; }

        public string ImageCacheDirectory { get; }

        public static TempPlatformPaths Create() =>
            new(Path.Combine(Path.GetTempPath(), "nuvio-data-tests", Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
