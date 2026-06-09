using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;

namespace Nuvio.Desktop.Controls;

/// <summary>
/// Reusable poster tile used by the Home, Catalog, and Search grids. Extends <see cref="Button"/> so it
/// keeps command binding, keyboard activation, and the shared focus ring, while centralizing poster sizing,
/// the placeholder visual, and the accessibility name in one place. Card dimensions are driven from the
/// active <c>DesktopLayoutMode</c> via <see cref="CardWidth"/>/<see cref="CardHeight"/>.
///
/// Poster art loads on demand: when the card is realized in a virtualized row it asks its
/// <see cref="PosterCardViewModel"/> to decode the poster at a DPI-appropriate width, and cancels that load
/// when recycled out of view. The decoded image flows back through <see cref="PosterImageProperty"/>.
/// </summary>
public sealed class PosterCard : Button
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PosterCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PosterCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<string?> InitialProperty =
        AvaloniaProperty.Register<PosterCard, string?>(nameof(Initial));

    public static readonly StyledProperty<IImage?> PosterImageProperty =
        AvaloniaProperty.Register<PosterCard, IImage?>(nameof(PosterImage));

    public static readonly StyledProperty<bool> ShowInitialProperty =
        AvaloniaProperty.Register<PosterCard, bool>(nameof(ShowInitial), defaultValue: true);

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<PosterCard, double>(nameof(Progress));

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<PosterCard, bool>(nameof(ShowProgress));

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

    public IImage? PosterImage
    {
        get => GetValue(PosterImageProperty);
        set => SetValue(PosterImageProperty, value);
    }

    public bool ShowInitial
    {
        get => GetValue(ShowInitialProperty);
        set => SetValue(ShowInitialProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not PosterCardViewModel card)
        {
            return;
        }

        var renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var decodeWidth = ImageDecodeSizing.PosterDecodeWidth(CardWidth, renderScaling);
        _ = card.EnsureImageAsync(decodeWidth, CancellationToken.None);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is PosterCardViewModel card)
        {
            card.CancelImageLoad();
        }
    }
}
