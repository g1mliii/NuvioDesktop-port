using System.Text.Json;
using Nuvio.Core.Addons;
using Nuvio.Core.Models;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteAddonRepository : IAddonRepository
{
    private readonly SqliteStorage _storage;

    public SqliteAddonRepository(SqliteStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<IReadOnlyList<ManagedAddon>> ListAsync(CancellationToken cancellationToken)
    {
        var addons = new List<ManagedAddon>();
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, manifest_url, manifest_json, enabled, sort_order, last_error, last_refreshed_at
            FROM addons
            ORDER BY sort_order ASC, id ASC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            addons.Add(ReadAddon(reader));
        }

        return addons;
    }

    public async Task<ManagedAddon?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, manifest_url, manifest_json, enabled, sort_order, last_error, last_refreshed_at
            FROM addons
            WHERE id = $id;
            """;
        SqliteConnectionFactory.AddParameter(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadAddon(reader)
            : null;
    }

    public async Task UpsertAsync(ManagedAddon addon, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addon);

        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO addons (
                id,
                manifest_url,
                manifest_json,
                enabled,
                sort_order,
                last_error,
                last_refreshed_at,
                updated_at)
            VALUES (
                $id,
                $manifest_url,
                $manifest_json,
                $enabled,
                $sort_order,
                $last_error,
                $last_refreshed_at,
                $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                manifest_url = excluded.manifest_url,
                manifest_json = excluded.manifest_json,
                enabled = excluded.enabled,
                sort_order = excluded.sort_order,
                last_error = excluded.last_error,
                last_refreshed_at = excluded.last_refreshed_at,
                updated_at = excluded.updated_at;
            """;
        SqliteConnectionFactory.AddParameter(command, "$id", addon.Id);
        SqliteConnectionFactory.AddParameter(command, "$manifest_url", addon.ManifestUrl.ToString());
        SqliteConnectionFactory.AddParameter(
            command,
            "$manifest_json",
            addon.Manifest is null ? null : JsonSerializer.Serialize(addon.Manifest, SqliteJson.Options));
        SqliteConnectionFactory.AddParameter(command, "$enabled", addon.Enabled ? 1 : 0);
        SqliteConnectionFactory.AddParameter(command, "$sort_order", addon.SortOrder);
        SqliteConnectionFactory.AddParameter(command, "$last_error", addon.LastError);
        SqliteConnectionFactory.AddParameter(command, "$last_refreshed_at", SqliteTimestamp.Format(addon.LastRefreshedAt));
        SqliteConnectionFactory.AddParameter(command, "$updated_at", SqliteTimestamp.Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = _storage.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM addons WHERE id = $id;";
        SqliteConnectionFactory.AddParameter(command, "$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);

        await using var connection = _storage.OpenConnection();
        using var transaction = connection.BeginTransaction();
        for (var index = 0; index < orderedIds.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE addons SET sort_order = $sort_order, updated_at = $updated_at WHERE id = $id;";
            SqliteConnectionFactory.AddParameter(command, "$sort_order", index);
            SqliteConnectionFactory.AddParameter(command, "$updated_at", SqliteTimestamp.Format(DateTimeOffset.UtcNow));
            SqliteConnectionFactory.AddParameter(command, "$id", orderedIds[index]);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    private static ManagedAddon ReadAddon(System.Data.Common.DbDataReader reader)
    {
        var manifestJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        var manifest = string.IsNullOrWhiteSpace(manifestJson)
            ? null
            : JsonSerializer.Deserialize<AddonManifest>(manifestJson, SqliteJson.Options);

        return new ManagedAddon(
            Id: reader.GetString(0),
            ManifestUrl: new Uri(reader.GetString(1), UriKind.Absolute),
            Manifest: manifest,
            Enabled: reader.GetInt32(3) == 1,
            SortOrder: reader.GetInt32(4),
            LastError: reader.IsDBNull(5) ? null : reader.GetString(5),
            LastRefreshedAt: reader.IsDBNull(6) ? null : SqliteTimestamp.Parse(reader.GetString(6)));
    }
}
