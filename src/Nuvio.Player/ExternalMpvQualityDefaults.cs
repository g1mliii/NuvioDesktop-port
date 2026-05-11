using System.Collections.ObjectModel;

namespace Nuvio.Player;

public static class ExternalMpvQualityDefaults
{
    public static IReadOnlyDictionary<string, string> Options { get; } =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["vo"] = "gpu-next",
                ["gpu-api"] = "auto",
                ["hwdec"] = "auto-safe",
                ["cache"] = "yes",
                ["demuxer-max-bytes"] = "150M",
                ["demuxer-max-back-bytes"] = "50M",
                ["save-position-on-quit"] = "no",
                ["config"] = "no"
            });

    public static IReadOnlyList<string> ToCommandLineArguments() =>
        Options.Select(pair => $"--{pair.Key}={pair.Value}").ToArray();

    public static IReadOnlyList<string> ToCommandLineArguments(IReadOnlyDictionary<string, string> options) =>
        options.Select(pair => $"--{pair.Key}={pair.Value}").ToArray();
}
