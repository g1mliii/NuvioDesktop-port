using Microsoft.Data.Sqlite;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteMigrationRunner
{
    public SqliteMigrationRunner(IEnumerable<SqliteMigration>? migrations = null)
    {
        Migrations = (migrations ?? DefaultMigrations()).OrderBy(migration => migration.Version).ToArray();
    }

    public IReadOnlyList<SqliteMigration> Migrations { get; }

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await EnsureSchemaMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
        var applied = await GetAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var migration in Migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO schema_migrations(version, name, applied_at)
                    VALUES ($version, $name, $applied_at);
                    """;
                SqliteConnectionFactory.AddParameter(insert, "$version", migration.Version);
                SqliteConnectionFactory.AddParameter(insert, "$name", migration.Name);
                SqliteConnectionFactory.AddParameter(insert, "$applied_at", DateTimeOffset.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            applied.Add(migration.Version);
        }
    }

    private static async Task EnsureSchemaMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<int>> GetAppliedVersionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var applied = new HashSet<int>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied.Add(reader.GetInt32(0));
        }

        return applied;
    }

    private static IReadOnlyList<SqliteMigration> DefaultMigrations() =>
    [
        new SqliteMigration(
            1,
            "001_initial_storage",
            """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS addons (
                id TEXT PRIMARY KEY,
                manifest_url TEXT NOT NULL,
                manifest_json TEXT NULL,
                enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
                sort_order INTEGER NOT NULL,
                last_error TEXT NULL,
                last_refreshed_at TEXT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_addons_sort_order ON addons(sort_order, id);

            CREATE TABLE IF NOT EXISTS watch_progress (
                media_id TEXT NOT NULL,
                episode_id TEXT NOT NULL DEFAULT '',
                position_ms INTEGER NOT NULL,
                duration_ms INTEGER NOT NULL,
                percent REAL NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (media_id, episode_id)
            );

            CREATE INDEX IF NOT EXISTS idx_watch_progress_recent
                ON watch_progress(updated_at DESC);

            CREATE TABLE IF NOT EXISTS metadata_cache (
                cache_kind TEXT NOT NULL,
                media_type TEXT NOT NULL,
                media_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                stored_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                last_accessed_at TEXT NOT NULL,
                PRIMARY KEY (cache_kind, media_type, media_id)
            );

            CREATE INDEX IF NOT EXISTS idx_metadata_cache_expiry
                ON metadata_cache(expires_at);

            CREATE TABLE IF NOT EXISTS image_cache_entries (
                cache_key TEXT PRIMARY KEY,
                source_url TEXT NOT NULL,
                file_path TEXT NOT NULL,
                byte_size INTEGER NOT NULL,
                content_type TEXT NULL,
                stored_at TEXT NOT NULL,
                last_accessed_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_image_cache_lru
                ON image_cache_entries(last_accessed_at ASC);
            """),
        new SqliteMigration(
            2,
            "002_watch_progress_display_metadata",
            """
            ALTER TABLE watch_progress ADD COLUMN media_type TEXT NULL;
            ALTER TABLE watch_progress ADD COLUMN title TEXT NULL;
            ALTER TABLE watch_progress ADD COLUMN poster_url TEXT NULL;
            ALTER TABLE watch_progress ADD COLUMN background_url TEXT NULL;
            """)
    ];
}
