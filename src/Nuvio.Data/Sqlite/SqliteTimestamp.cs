using System.Globalization;

namespace Nuvio.Data.Sqlite;

/// <summary>
/// Single source of truth for serializing timestamps to and from SQLite text columns,
/// using the round-trip ("O") format with invariant culture.
/// </summary>
internal static class SqliteTimestamp
{
    public static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    public static string? Format(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
