using Nuvio.Core.Settings;

namespace Nuvio.Desktop.Services;

/// <summary>
/// Applies the persisted <see cref="ThemeMode"/> to the running application so the whole chrome re-themes
/// live. <see cref="ThemeMode.System"/> maps to Avalonia's default variant, which follows (and keeps
/// following) the OS theme; Light/Dark are explicit overrides.
/// </summary>
public interface IThemeController
{
    void Apply(ThemeMode mode);
}
