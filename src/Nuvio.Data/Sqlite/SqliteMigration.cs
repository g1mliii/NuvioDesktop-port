namespace Nuvio.Data.Sqlite;

public sealed record SqliteMigration(int Version, string Name, string Sql);
