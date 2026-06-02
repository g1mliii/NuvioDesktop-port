using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Nuvio.Core.Settings;

namespace Nuvio.Desktop.Services;

/// <summary>
/// Default <see cref="IThemeController"/> that drives <see cref="Application.RequestedThemeVariant"/>.
/// </summary>
public sealed class ThemeController : IThemeController
{
    public void Apply(ThemeMode mode)
    {
        var variant = ToVariant(mode);

        if (Dispatcher.UIThread.CheckAccess())
        {
            SetVariant(variant);
        }
        else
        {
            Dispatcher.UIThread.Post(() => SetVariant(variant));
        }
    }

    private static void SetVariant(ThemeVariant variant)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
        }
    }

    internal static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        // System -> Default, which follows the OS theme and keeps responding to OS theme changes.
        _ => ThemeVariant.Default,
    };
}
