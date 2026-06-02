namespace Nuvio.Desktop.Models;

/// <summary>
/// Responsive layout buckets driven by window width (and the TV focus-mode override). Views size chrome,
/// sidebar, and poster cards from the active mode instead of fixed literals.
/// </summary>
public enum DesktopLayoutMode
{
    Compact,
    Normal,
    Wide,
    Tv
}
