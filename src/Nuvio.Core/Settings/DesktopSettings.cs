namespace Nuvio.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum PlayerMode
{
    ExternalMpv,
    LibMpv
}

public sealed record DesktopSettings(
    ThemeMode Theme,
    PlayerMode PlayerMode,
    int InitialVolume,
    bool HardwareDecodingEnabled,
    long ImageDiskCacheLimitBytes,
    int DecodedImageMemoryItemLimit,
    TimeSpan MetadataCacheTtl)
{
    public const int DefaultInitialVolume = 80;
    public const bool DefaultHardwareDecodingEnabled = true;
    public const long DefaultImageDiskCacheLimitBytes = 500L * 1024L * 1024L;
    public const int DefaultDecodedImageMemoryItemLimit = 128;
    public static readonly TimeSpan DefaultMetadataCacheTtl = TimeSpan.FromMinutes(30);

    public static DesktopSettings Default { get; } = new(
        ThemeMode.System,
        PlayerMode.ExternalMpv,
        DefaultInitialVolume,
        DefaultHardwareDecodingEnabled,
        DefaultImageDiskCacheLimitBytes,
        DefaultDecodedImageMemoryItemLimit,
        DefaultMetadataCacheTtl);

    public DesktopSettings Normalize() => this with
    {
        InitialVolume = Math.Clamp(InitialVolume, 0, 100),
        ImageDiskCacheLimitBytes = Math.Clamp(
            ImageDiskCacheLimitBytes,
            64L * 1024L * 1024L,
            DefaultImageDiskCacheLimitBytes),
        DecodedImageMemoryItemLimit = Math.Clamp(DecodedImageMemoryItemLimit, 16, 512),
        MetadataCacheTtl = MetadataCacheTtl < TimeSpan.FromMinutes(5)
            ? TimeSpan.FromMinutes(5)
            : MetadataCacheTtl
    };
}
