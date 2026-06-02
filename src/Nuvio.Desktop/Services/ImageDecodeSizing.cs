namespace Nuvio.Desktop.Services;

/// <summary>
/// High-DPI decode sizing (Phase 7.10). Posters and backdrops are decoded to a bounded pixel width keyed
/// off the on-screen layout size multiplied by the display's render scaling, then clamped to a hard maximum.
/// This keeps decoded bitmaps crisp at 2× DPI while guarding against full-resolution decode of very large
/// source images (a 4K poster would otherwise allocate tens of MB per tile).
/// </summary>
public static class ImageDecodeSizing
{
    public const int MinPosterDecodeWidth = 96;
    public const int MaxPosterDecodeWidth = 480;
    public const int MinBackdropDecodeWidth = 320;
    public const int MaxBackdropDecodeWidth = 1280;

    /// <summary>Decode width for a poster shown at <paramref name="layoutWidth"/> DIPs on a display with the
    /// given <paramref name="renderScaling"/>, clamped so the bitmap never over-allocates.</summary>
    public static int PosterDecodeWidth(double layoutWidth, double renderScaling) =>
        Clamp(layoutWidth, renderScaling, MinPosterDecodeWidth, MaxPosterDecodeWidth);

    /// <summary>Decode width for a backdrop shown at <paramref name="layoutWidth"/> DIPs on a display with the
    /// given <paramref name="renderScaling"/>, clamped so the bitmap never over-allocates.</summary>
    public static int BackdropDecodeWidth(double layoutWidth, double renderScaling) =>
        Clamp(layoutWidth, renderScaling, MinBackdropDecodeWidth, MaxBackdropDecodeWidth);

    private static int Clamp(double layoutWidth, double renderScaling, int min, int max)
    {
        var scaling = renderScaling <= 0 ? 1d : renderScaling;
        var target = (int)Math.Ceiling(Math.Max(0d, layoutWidth) * scaling);
        return Math.Clamp(target, min, max);
    }
}
