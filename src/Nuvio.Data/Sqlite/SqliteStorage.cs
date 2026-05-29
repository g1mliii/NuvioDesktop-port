using Microsoft.Data.Sqlite;
using Nuvio.Platform;

namespace Nuvio.Data.Sqlite;

public sealed class SqliteStorage
{
    private readonly SqliteMigrationRunner _migrationRunner;

    private SqliteStorage(IPlatformPaths paths, SqliteMigrationRunner migrationRunner)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _migrationRunner = migrationRunner ?? throw new ArgumentNullException(nameof(migrationRunner));
    }

    public IPlatformPaths Paths { get; }

    public static SqliteStorage Open(
        IPlatformPaths paths,
        SqliteMigrationRunner? migrationRunner = null,
        CancellationToken cancellationToken = default)
    {
        var storage = new SqliteStorage(paths, migrationRunner ?? new SqliteMigrationRunner());
        storage.InitializeAsync(cancellationToken).GetAwaiter().GetResult();
        return storage;
    }

    public SqliteConnection OpenConnection() => SqliteConnectionFactory.Open(Paths.DatabasePath);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        CreateDirectories();

        string? backupPath = null;
        if (File.Exists(Paths.DatabasePath))
        {
            if (!IsDatabaseReadable())
            {
                MoveCorruptDatabaseAside();
            }
            else
            {
                backupPath = CreatePreMigrationBackup();
            }
        }

        try
        {
            await using var connection = OpenConnection();
            await _migrationRunner.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Copy(backupPath, Paths.DatabasePath, overwrite: true);
                TryDeleteSidecar("-wal");
                TryDeleteSidecar("-shm");
            }

            throw;
        }
    }

    private void CreateDirectories()
    {
        Directory.CreateDirectory(Paths.AppDataDirectory);
        Directory.CreateDirectory(Paths.CacheDirectory);
        Directory.CreateDirectory(Paths.LogDirectory);
        Directory.CreateDirectory(Paths.BackupDirectory);
        Directory.CreateDirectory(Paths.ImageCacheDirectory);
    }

    private bool IsDatabaseReadable()
    {
        if (!HasSqliteHeader(Paths.DatabasePath))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = command.ExecuteScalar()?.ToString();
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            ReleaseFailedOpenHandles();
            return false;
        }
        catch (InvalidOperationException)
        {
            ReleaseFailedOpenHandles();
            return false;
        }
    }

    private string CreatePreMigrationBackup()
    {
        using (var connection = OpenConnection())
        using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(FULL);";
            checkpoint.ExecuteNonQuery();
        }

        var backupPath = Path.Combine(
            Paths.BackupDirectory,
            $"pre-migration-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.sqlite3");
        File.Copy(Paths.DatabasePath, backupPath, overwrite: false);
        return backupPath;
    }

    private void MoveCorruptDatabaseAside()
    {
        var corruptPath = $"{Paths.DatabasePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        MoveFileWithRetry(Paths.DatabasePath, corruptPath);
        MoveSidecarAside("-wal", corruptPath);
        MoveSidecarAside("-shm", corruptPath);
    }

    private void TryDeleteSidecar(string suffix)
    {
        var sidecar = Paths.DatabasePath + suffix;
        try
        {
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void MoveSidecarAside(string suffix, string corruptPath)
    {
        var sidecar = Paths.DatabasePath + suffix;
        if (File.Exists(sidecar))
        {
            MoveFileWithRetry(sidecar, corruptPath + suffix);
        }
    }

    private static void MoveFileWithRetry(string sourcePath, string destinationPath)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: false);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                ReleaseFailedOpenHandles();
                Thread.Sleep(50);
            }
        }
    }

    private static void ReleaseFailedOpenHandles()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static bool HasSqliteHeader(string databasePath)
    {
        const string Header = "SQLite format 3";
        var info = new FileInfo(databasePath);
        if (!info.Exists || info.Length < Header.Length)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Header.Length];
        using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var read = stream.Read(buffer);
        return read == Header.Length &&
               Header.AsSpan().SequenceEqual(System.Text.Encoding.ASCII.GetString(buffer));
    }
}
