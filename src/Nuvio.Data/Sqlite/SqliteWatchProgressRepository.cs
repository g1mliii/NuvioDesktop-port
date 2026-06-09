using Nuvio.Core.Models;
using Nuvio.Core.Progress;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteWatchProgressRepository : IWatchProgressRepository
{
    private readonly SqliteStorage _storage;

    public SqliteWatchProgressRepository(SqliteStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<WatchProgress?> GetAsync(string mediaId, string? episodeId, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT media_id, episode_id, position_ms, duration_ms, percent, updated_at,
                   media_type, title, poster_url, background_url
            FROM watch_progress
            WHERE media_id = $media_id AND episode_id = $episode_id;
            """;
        SqliteConnectionFactory.AddParameter(command, "$media_id", mediaId);
        SqliteConnectionFactory.AddParameter(command, "$episode_id", NormalizeEpisodeId(episodeId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadProgress(reader)
            : null;
    }

    public async Task UpsertAsync(WatchProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var normalized = WatchProgressRules.Normalize(progress);
        if (!WatchProgressRules.ShouldStoreProgress(normalized.Position, normalized.Duration))
        {
            return;
        }

        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO watch_progress(
                media_id,
                episode_id,
                position_ms,
                duration_ms,
                percent,
                updated_at,
                media_type,
                title,
                poster_url,
                background_url)
            VALUES (
                $media_id,
                $episode_id,
                $position_ms,
                $duration_ms,
                $percent,
                $updated_at,
                $media_type,
                $title,
                $poster_url,
                $background_url)
            ON CONFLICT(media_id, episode_id) DO UPDATE SET
                position_ms = excluded.position_ms,
                duration_ms = excluded.duration_ms,
                percent = excluded.percent,
                updated_at = excluded.updated_at,
                media_type = excluded.media_type,
                title = excluded.title,
                poster_url = excluded.poster_url,
                background_url = excluded.background_url;
            """;
        SqliteConnectionFactory.AddParameter(command, "$media_id", normalized.MediaId);
        SqliteConnectionFactory.AddParameter(command, "$episode_id", NormalizeEpisodeId(normalized.EpisodeId));
        SqliteConnectionFactory.AddParameter(command, "$position_ms", ToMilliseconds(normalized.Position));
        SqliteConnectionFactory.AddParameter(command, "$duration_ms", ToMilliseconds(normalized.Duration));
        SqliteConnectionFactory.AddParameter(command, "$percent", normalized.Percent);
        SqliteConnectionFactory.AddParameter(command, "$updated_at", SqliteTimestamp.Format(normalized.UpdatedAt));
        SqliteConnectionFactory.AddParameter(command, "$media_type", NullIfEmpty(normalized.MediaType));
        SqliteConnectionFactory.AddParameter(command, "$title", NullIfEmpty(normalized.Title));
        SqliteConnectionFactory.AddParameter(command, "$poster_url", UriToString(normalized.PosterUrl));
        SqliteConnectionFactory.AddParameter(command, "$background_url", UriToString(normalized.BackgroundUrl));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string mediaId, string? episodeId, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM watch_progress WHERE media_id = $media_id AND episode_id = $episode_id;";
        SqliteConnectionFactory.AddParameter(command, "$media_id", mediaId);
        SqliteConnectionFactory.AddParameter(command, "$episode_id", NormalizeEpisodeId(episodeId));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<IReadOnlyList<WatchProgress>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 500);
        var rows = new List<WatchProgress>();
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT media_id, episode_id, position_ms, duration_ms, percent, updated_at,
                   media_type, title, poster_url, background_url
            FROM watch_progress
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        SqliteConnectionFactory.AddParameter(command, "$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(ReadProgress(reader));
        }

        return rows;
    }

    private static WatchProgress ReadProgress(System.Data.Common.DbDataReader reader)
    {
        var episodeId = reader.GetString(1);
        return new WatchProgress(
            MediaId: reader.GetString(0),
            EpisodeId: string.IsNullOrEmpty(episodeId) ? null : episodeId,
            Position: TimeSpan.FromMilliseconds(reader.GetInt64(2)),
            Duration: TimeSpan.FromMilliseconds(reader.GetInt64(3)),
            Percent: reader.GetDouble(4),
            UpdatedAt: SqliteTimestamp.Parse(reader.GetString(5)),
            MediaType: ReadNullableString(reader, 6),
            Title: ReadNullableString(reader, 7),
            PosterUrl: ReadNullableUri(reader, 8),
            BackgroundUrl: ReadNullableUri(reader, 9));
    }

    private static string? ReadNullableString(System.Data.Common.DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static Uri? ReadNullableUri(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = ReadNullableString(reader, ordinal);
        return value is not null && Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static object? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static object? UriToString(Uri? value) =>
        value is null ? null : value.ToString();

    private static string NormalizeEpisodeId(string? episodeId) => episodeId ?? string.Empty;

    private static long ToMilliseconds(TimeSpan value) =>
        Math.Max(0, Convert.ToInt64(Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero)));
}
