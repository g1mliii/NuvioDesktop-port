using System.Text.Json;
using Nuvio.Core.Settings;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private const string DesktopSettingsKey = "desktop";
    private const string HomeCatalogSettingsKey = "home_catalog";
    private readonly SqliteStorage _storage;

    public SqliteSettingsStore(SqliteStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await ReadValueAsync(DesktopSettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return DesktopSettings.Default;
        }

        var settings = JsonSerializer.Deserialize<DesktopSettings>(json, SqliteJson.Options)
            ?? DesktopSettings.Default;
        return settings.Normalize();
    }

    public Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken)
    {
        var normalized = (settings ?? DesktopSettings.Default).Normalize();
        return WriteValueAsync(
            DesktopSettingsKey,
            JsonSerializer.Serialize(normalized, SqliteJson.Options),
            cancellationToken);
    }

    public async Task<HomeCatalogSettings> LoadHomeCatalogSettingsAsync(CancellationToken cancellationToken)
    {
        var json = await ReadValueAsync(HomeCatalogSettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return HomeCatalogSettings.Default;
        }

        var settings = JsonSerializer.Deserialize<HomeCatalogSettings>(json, SqliteJson.Options)
            ?? HomeCatalogSettings.Default;
        return settings.Normalize();
    }

    public Task SaveHomeCatalogSettingsAsync(HomeCatalogSettings settings, CancellationToken cancellationToken)
    {
        var normalized = (settings ?? HomeCatalogSettings.Default).Normalize();
        return WriteValueAsync(
            HomeCatalogSettingsKey,
            JsonSerializer.Serialize(normalized, SqliteJson.Options),
            cancellationToken);
    }

    private async Task<string?> ReadValueAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM settings WHERE key = $key;";
        SqliteConnectionFactory.AddParameter(command, "$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value as string;
    }

    private async Task WriteValueAsync(string key, string valueJson, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value_json, updated_at)
            VALUES ($key, $value_json, $updated_at)
            ON CONFLICT(key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at;
            """;
        SqliteConnectionFactory.AddParameter(command, "$key", key);
        SqliteConnectionFactory.AddParameter(command, "$value_json", valueJson);
        SqliteConnectionFactory.AddParameter(
            command,
            "$updated_at",
            SqliteTimestamp.Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
