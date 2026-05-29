using System.Text.Json;
using System.Text.Json.Serialization;
using Nuvio.Core.Settings;

namespace Nuvio.Data.Sqlite;

internal static class SqliteJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };
        options.Converters.Add(new ThemeModeJsonConverter());
        options.Converters.Add(new PlayerModeJsonConverter());
        return options;
    }

    private sealed class ThemeModeJsonConverter : JsonConverter<ThemeMode>
    {
        public override ThemeMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                _ => ThemeMode.System
            };

        public override void Write(Utf8JsonWriter writer, ThemeMode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value switch
            {
                ThemeMode.Light => "light",
                ThemeMode.Dark => "dark",
                _ => "system"
            });
    }

    private sealed class PlayerModeJsonConverter : JsonConverter<PlayerMode>
    {
        public override PlayerMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() switch
            {
                "libmpv" => PlayerMode.LibMpv,
                _ => PlayerMode.ExternalMpv
            };

        public override void Write(Utf8JsonWriter writer, PlayerMode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value == PlayerMode.LibMpv ? "libmpv" : "external-mpv");
    }
}
