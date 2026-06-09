namespace Nuvio.Core.Settings;

/// <summary>
/// Per-rail Home customization, keyed by the rail's stable "{manifestId}:{type}:{catalogId}" key.
/// Mirrors upstream HomeCatalogSettingsSnapshot's per-key entries.
/// </summary>
public sealed record HomeCatalogPreference(
    string Key,
    int Order,
    bool Enabled = true,
    string? CustomTitle = null,
    bool HeroSourceEnabled = true);

/// <summary>
/// Persisted Home customization: a global hero toggle plus per-rail order/enable/rename/hero-source
/// preferences. Mirrors upstream HomeCatalogSettingsRepository/HomeCatalogSettingsSnapshot.
/// </summary>
public sealed record HomeCatalogSettings
{
    public bool HeroEnabled { get; init; } = true;

    public IReadOnlyList<HomeCatalogPreference> Preferences { get; init; } = Array.Empty<HomeCatalogPreference>();

    public static HomeCatalogSettings Default { get; } = new();

    /// <summary>Drops malformed/duplicate entries (first key wins) so downstream lookups are unambiguous.</summary>
    public HomeCatalogSettings Normalize()
    {
        var source = Preferences ?? Array.Empty<HomeCatalogPreference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<HomeCatalogPreference>(source.Count);
        foreach (var preference in source)
        {
            if (preference is null || string.IsNullOrWhiteSpace(preference.Key))
            {
                continue;
            }

            if (seen.Add(preference.Key))
            {
                normalized.Add(preference);
            }
        }

        return this with { Preferences = normalized };
    }

    public HomeCatalogPreference? Find(string key) =>
        Preferences.FirstOrDefault(preference =>
            string.Equals(preference.Key, key, StringComparison.OrdinalIgnoreCase));
}
