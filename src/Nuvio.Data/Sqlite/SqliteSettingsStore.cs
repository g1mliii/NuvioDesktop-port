using System.Text.Json;
using Nuvio.Core.Settings;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private const string DesktopSettingsKey = "desktop";
    private readonly SqliteStorage _storage;

    public SqliteSettingsStore(SqliteStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM settings WHERE key = $key;";
        SqliteConnectionFactory.AddParameter(command, "$key", DesktopSettingsKey);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return DesktopSettings.Default;
        }

        var settings = JsonSerializer.Deserialize<DesktopSettings>(json, SqliteJson.Options)
            ?? DesktopSettings.Default;
        return settings.Normalize();
    }

    public async Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken)
    {
        settings = (settings ?? DesktopSettings.Default).Normalize();

        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value_json, updated_at)
            VALUES ($key, $value_json, $updated_at)
            ON CONFLICT(key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at;
            """;
        SqliteConnectionFactory.AddParameter(command, "$key", DesktopSettingsKey);
        SqliteConnectionFactory.AddParameter(command, "$value_json", JsonSerializer.Serialize(settings, SqliteJson.Options));
        SqliteConnectionFactory.AddParameter(
            command,
            "$updated_at",
            SqliteTimestamp.Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
