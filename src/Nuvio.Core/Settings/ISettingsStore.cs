namespace Nuvio.Core.Settings;

public interface ISettingsStore
{
    Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the Home customization snapshot (hero toggle + per-rail order/enable/rename/hero-source).
    /// Default implementation returns <see cref="HomeCatalogSettings.Default"/> so in-memory/test stores
    /// that don't persist Home settings keep working.
    /// </summary>
    Task<HomeCatalogSettings> LoadHomeCatalogSettingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(HomeCatalogSettings.Default);

    /// <summary>Persists the Home customization snapshot. Default implementation is a no-op.</summary>
    Task SaveHomeCatalogSettingsAsync(HomeCatalogSettings settings, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
