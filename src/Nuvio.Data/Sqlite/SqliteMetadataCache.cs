using System.Text.Json;
using Nuvio.Core.Metadata;
using Nuvio.Core.Models;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteMetadataCache : IMetadataCache
{
    private const string DetailsKind = "details";
    private const string SubtitlesKind = "subtitles";
    private readonly SqliteStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();

    public SqliteMetadataCache(
        SqliteStorage storage,
        TimeSpan? ttl = null,
        TimeProvider? timeProvider = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _ttl = ttl ?? MetadataCache.DefaultTtl;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryGetDetails(string type, string id, out MediaDetails details)
    {
        if (TryGet(DetailsKind, type, id, out var json))
        {
            var parsed = JsonSerializer.Deserialize<MediaDetails>(json, SqliteJson.Options);
            if (parsed is not null)
            {
                details = parsed;
                return true;
            }
        }

        details = default!;
        return false;
    }

    public void SetDetails(string type, string id, MediaDetails details) =>
        Set(DetailsKind, type, id, JsonSerializer.Serialize(details, SqliteJson.Options));

    public bool TryGetSubtitles(string type, string id, out IReadOnlyList<SubtitleTrack> subtitles)
    {
        if (TryGet(SubtitlesKind, type, id, out var json))
        {
            var parsed = JsonSerializer.Deserialize<IReadOnlyList<SubtitleTrack>>(json, SqliteJson.Options);
            if (parsed is not null)
            {
                subtitles = parsed;
                return true;
            }
        }

        subtitles = Array.Empty<SubtitleTrack>();
        return false;
    }

    public void SetSubtitles(string type, string id, IReadOnlyList<SubtitleTrack> subtitles) =>
        Set(SubtitlesKind, type, id, JsonSerializer.Serialize(subtitles, SqliteJson.Options));

    public void Clear()
    {
        lock (_gate)
        {
            using var connection = _storage.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM metadata_cache;";
            command.ExecuteNonQuery();
        }
    }

    private bool TryGet(string kind, string type, string id, out string json)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            DateTimeOffset expiresAt;
            using var connection = _storage.OpenConnection();
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT payload_json, expires_at
                    FROM metadata_cache
                    WHERE cache_kind = $kind AND media_type = $type AND media_id = $id;
                    """;
                SqliteConnectionFactory.AddParameter(command, "$kind", kind);
                SqliteConnectionFactory.AddParameter(command, "$type", type);
                SqliteConnectionFactory.AddParameter(command, "$id", id);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    json = string.Empty;
                    return false;
                }

                json = reader.GetString(0);
                expiresAt = SqliteTimestamp.Parse(reader.GetString(1));
            }

            if (expiresAt <= now)
            {
                DeleteExpired(kind, type, id);
                json = string.Empty;
                return false;
            }

            Touch(kind, type, id, now);
            return true;
        }
    }

    private void Set(string kind, string type, string id, string json)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            using var connection = _storage.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO metadata_cache(
                    cache_kind,
                    media_type,
                    media_id,
                    payload_json,
                    stored_at,
                    expires_at,
                    last_accessed_at)
                VALUES (
                    $kind,
                    $type,
                    $id,
                    $payload_json,
                    $stored_at,
                    $expires_at,
                    $last_accessed_at)
                ON CONFLICT(cache_kind, media_type, media_id) DO UPDATE SET
                    payload_json = excluded.payload_json,
                    stored_at = excluded.stored_at,
                    expires_at = excluded.expires_at,
                    last_accessed_at = excluded.last_accessed_at;
                """;
            SqliteConnectionFactory.AddParameter(command, "$kind", kind);
            SqliteConnectionFactory.AddParameter(command, "$type", type);
            SqliteConnectionFactory.AddParameter(command, "$id", id);
            SqliteConnectionFactory.AddParameter(command, "$payload_json", json);
            SqliteConnectionFactory.AddParameter(command, "$stored_at", SqliteTimestamp.Format(now));
            SqliteConnectionFactory.AddParameter(command, "$expires_at", SqliteTimestamp.Format(now.Add(_ttl)));
            SqliteConnectionFactory.AddParameter(command, "$last_accessed_at", SqliteTimestamp.Format(now));
            command.ExecuteNonQuery();
        }
    }

    private void DeleteExpired(string kind, string type, string id)
    {
        using var connection = _storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM metadata_cache
            WHERE cache_kind = $kind AND media_type = $type AND media_id = $id;
            """;
        SqliteConnectionFactory.AddParameter(command, "$kind", kind);
        SqliteConnectionFactory.AddParameter(command, "$type", type);
        SqliteConnectionFactory.AddParameter(command, "$id", id);
        command.ExecuteNonQuery();
    }

    private void Touch(string kind, string type, string id, DateTimeOffset now)
    {
        using var connection = _storage.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE metadata_cache
            SET last_accessed_at = $last_accessed_at
            WHERE cache_kind = $kind AND media_type = $type AND media_id = $id;
            """;
        SqliteConnectionFactory.AddParameter(command, "$last_accessed_at", SqliteTimestamp.Format(now));
        SqliteConnectionFactory.AddParameter(command, "$kind", kind);
        SqliteConnectionFactory.AddParameter(command, "$type", type);
        SqliteConnectionFactory.AddParameter(command, "$id", id);
        command.ExecuteNonQuery();
    }
}
