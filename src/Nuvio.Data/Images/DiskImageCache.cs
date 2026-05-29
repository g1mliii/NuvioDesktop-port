using System.Security.Cryptography;
using System.Text;
using Nuvio.Platform;

namespace Nuvio.Data.Images;

public sealed record DiskImageCacheEntry(
    string CacheKey,
    Uri SourceUrl,
    string FilePath,
    long ByteSize,
    string? ContentType,
    DateTimeOffset StoredAt,
    DateTimeOffset LastAccessedAt);

public sealed record DiskImageCacheOptions(
    long MaxCacheBytes,
    long MaxImageBytes)
{
    public static DiskImageCacheOptions Default { get; } = new(
        StoragePlan.Default.DefaultImageCacheBytes,
        20L * 1024L * 1024L);
}

public sealed class DiskImageCache
{
    private readonly Sqlite.SqliteStorage _storage;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly DiskImageCacheOptions _options;
    private long _maxCacheBytes;

    public DiskImageCache(
        Sqlite.SqliteStorage storage,
        DiskImageCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _paths = storage.Paths;
        _options = options ?? DiskImageCacheOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxCacheBytes = _options.MaxCacheBytes;
    }

    public async Task UpdateLimitAsync(long maxCacheBytes, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _maxCacheBytes, maxCacheBytes);
        await EnforceLimitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiskImageCacheEntry?> GetAsync(Uri sourceUrl, CancellationToken cancellationToken)
    {
        var key = CreateCacheKey(sourceUrl);
        DiskImageCacheEntry? entry;
        await using (var connection = _storage.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT cache_key, source_url, file_path, byte_size, content_type, stored_at, last_accessed_at
                FROM image_cache_entries
                WHERE cache_key = $cache_key;
                """;
            Sqlite.SqliteConnectionFactory.AddParameter(command, "$cache_key", key);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            entry = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadEntry(reader)
                : null;
        }

        if (entry is null)
        {
            return null;
        }

        if (!File.Exists(entry.FilePath))
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await TouchAsync(key, cancellationToken).ConfigureAwait(false);
        return entry with { LastAccessedAt = _timeProvider.GetUtcNow() };
    }

    public async Task<DiskImageCacheEntry> StoreAsync(
        Uri sourceUrl,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(_paths.ImageCacheDirectory);

        var key = CreateCacheKey(sourceUrl);
        var extension = ExtensionFor(contentType, sourceUrl);
        var finalPath = Path.Combine(_paths.ImageCacheDirectory, $"{key}{extension}");
        var tempPath = Path.Combine(_paths.ImageCacheDirectory, $"{key}.{Guid.NewGuid():N}.tmp");
        long byteCount = 0;

        try
        {
            await using (var output = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    byteCount += read;
                    if (byteCount > _options.MaxImageBytes)
                    {
                        throw new InvalidOperationException(
                            $"Image exceeds the configured {_options.MaxImageBytes} byte limit.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO image_cache_entries(
                cache_key,
                source_url,
                file_path,
                byte_size,
                content_type,
                stored_at,
                last_accessed_at)
            VALUES (
                $cache_key,
                $source_url,
                $file_path,
                $byte_size,
                $content_type,
                $stored_at,
                $last_accessed_at)
            ON CONFLICT(cache_key) DO UPDATE SET
                source_url = excluded.source_url,
                file_path = excluded.file_path,
                byte_size = excluded.byte_size,
                content_type = excluded.content_type,
                stored_at = excluded.stored_at,
                last_accessed_at = excluded.last_accessed_at;
            """;
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$cache_key", key);
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$source_url", sourceUrl.ToString());
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$file_path", finalPath);
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$byte_size", byteCount);
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$content_type", contentType);
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$stored_at", Sqlite.SqliteTimestamp.Format(now));
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$last_accessed_at", Sqlite.SqliteTimestamp.Format(now));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await EnforceLimitAsync(cancellationToken).ConfigureAwait(false);

        return new DiskImageCacheEntry(key, sourceUrl, finalPath, byteCount, contentType, now, now);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_paths.ImageCacheDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(_paths.ImageCacheDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDeleteFile(path);
            }
        }

        Directory.CreateDirectory(_paths.ImageCacheDirectory);
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM image_cache_entries;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnforceLimitAsync(CancellationToken cancellationToken)
    {
        var entries = new List<DiskImageCacheEntry>();
        await using (var connection = _storage.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT cache_key, source_url, file_path, byte_size, content_type, stored_at, last_accessed_at
                FROM image_cache_entries
                ORDER BY last_accessed_at ASC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(ReadEntry(reader));
            }
        }

        var total = entries.Sum(entry => entry.ByteSize);
        var limit = Interlocked.Read(ref _maxCacheBytes);
        foreach (var entry in entries)
        {
            if (total <= limit)
            {
                break;
            }

            TryDeleteFile(entry.FilePath);

            await RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
            total -= entry.ByteSize;
        }
    }

    private async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM image_cache_entries WHERE cache_key = $cache_key;";
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$cache_key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task TouchAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE image_cache_entries
            SET last_accessed_at = $last_accessed_at
            WHERE cache_key = $cache_key;
            """;
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$last_accessed_at", Sqlite.SqliteTimestamp.Format(_timeProvider.GetUtcNow()));
        Sqlite.SqliteConnectionFactory.AddParameter(command, "$cache_key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            TryDeleteFile(tempPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string CreateCacheKey(Uri sourceUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DiskImageCacheEntry ReadEntry(System.Data.Common.DbDataReader reader) =>
        new(
            CacheKey: reader.GetString(0),
            SourceUrl: new Uri(reader.GetString(1), UriKind.Absolute),
            FilePath: reader.GetString(2),
            ByteSize: reader.GetInt64(3),
            ContentType: reader.IsDBNull(4) ? null : reader.GetString(4),
            StoredAt: Sqlite.SqliteTimestamp.Parse(reader.GetString(5)),
            LastAccessedAt: Sqlite.SqliteTimestamp.Parse(reader.GetString(6)));

    private static string ExtensionFor(string? contentType, Uri sourceUrl)
    {
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return ".png";
        }

        if (string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase))
        {
            return ".webp";
        }

        if (string.Equals(contentType, "image/gif", StringComparison.OrdinalIgnoreCase))
        {
            return ".gif";
        }

        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase))
        {
            return ".jpg";
        }

        var extension = Path.GetExtension(sourceUrl.LocalPath);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 8
            ? ".img"
            : extension;
    }

}
