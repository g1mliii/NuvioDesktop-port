using Avalonia;
using Avalonia.Controls;

namespace Nuvio.Desktop.Controls;

/// <summary>
/// Reusable poster tile used by the Home, Catalog, and Search grids. Extends <see cref="Button"/> so it
/// keeps command binding, keyboard activation, and the shared focus ring, while centralizing poster sizing,
/// the placeholder visual, and the accessibility name in one place. Card dimensions are driven from the
/// active <c>DesktopLayoutMode</c> via <see cref="CardWidth"/>/<see cref="CardHeight"/>.
/// </summary>
public sealed class PosterCard : Button
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PosterCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PosterCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<string?> InitialProperty =
        AvaloniaProperty.Register<PosterCard, string?>(nameof(Initial));

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<PosterCard, double>(nameof(CardWidth), 150d);

    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<PosterCard, double>(nameof(CardHeight), 252d);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? Initial
    {
        get => GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }
}
