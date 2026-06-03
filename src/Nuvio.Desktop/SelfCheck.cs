using System.IO;
using System.Reflection;
using Nuvio.Desktop.Services;
using Nuvio.Platform;

namespace Nuvio.Desktop;

/// <summary>
/// Headless startup smoke used by packaging scripts and CI. It boots the live service
/// graph without opening a window, reports platform, app version, and mpv/libmpv
/// discovery to stdout, and returns process exit code 0 when the app constructs cleanly.
/// Modeled on the <c>--fixture-data</c> arg plumbing in <see cref="App"/>.
/// </summary>
internal static class SelfCheck
{
    internal const string FlagArg = "--self-check";
    internal const string FlagEnv = "NUVIO_SELF_CHECK";

    public static bool IsRequested(string[]? args)
    {
        if (args is not null && args.Any(arg =>
            string.Equals(arg, FlagArg, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fromEnv = Environment.GetEnvironmentVariable(FlagEnv);
        return string.Equals(fromEnv, "1", StringComparison.Ordinal) ||
               string.Equals(fromEnv, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs the self-check, writing a human-readable report to <paramref name="output"/>.
    /// Returns 0 on success. A missing external mpv/libmpv is reported but does not fail
    /// the check, because mpv is an optional, externally-installed dependency.
    /// </summary>
    public static int Run(TextWriter output)
    {
        try
        {
            var platform = PlatformInfoProvider.Current();

            output.WriteLine("Nuvio Desktop self-check");
            output.WriteLine($"  Version        : {ResolveVersion()}");
            output.WriteLine($"  Platform       : {platform.Family} ({platform.RuntimeIdentifier})");
            output.WriteLine($"  OS             : {platform.Description}");
            output.WriteLine($"  Supported      : {platform.IsSupportedDesktop}");

            BootServiceGraph(output);
            ReportMpv(output);
            ReportLibMpv(output);

            output.WriteLine("Self-check: OK");
            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine($"Self-check: FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void BootServiceGraph(TextWriter output)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        DesktopServiceHost? host = null;
        try
        {
            host = DesktopBootstrap.BuildLive(settingsPath);
            output.WriteLine("  Service graph  : constructed (storage, addons, catalog, cache)");
        }
        finally
        {
            host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void ReportMpv(TextWriter output)
    {
        try
        {
            var result = new MpvProcessLocator().Locate();
            if (result.IsAvailable)
            {
                output.WriteLine($"  mpv (external) : {result.StatusLabel} — {result.SourceLabel}, {result.ExecutablePath}, {result.Version ?? "version unknown"}");
            }
            else
            {
                output.WriteLine($"  mpv (external) : {result.StatusLabel} — {result.Message}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"  mpv (external) : discovery error — {ex.Message}");
        }
    }

    private static void ReportLibMpv(TextWriter output)
    {
        try
        {
            var result = new LibMpvLibraryLocator().Locate();
            if (result.FoundOnDisk)
            {
                output.WriteLine($"  libmpv         : {result.StatusLabel} — {result.SourceLabel}, {result.LibraryPath}");
            }
            else
            {
                output.WriteLine($"  libmpv         : {result.StatusLabel} — {result.Message}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"  libmpv         : discovery error — {ex.Message}");
        }
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(SelfCheck).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any "+<sourcelink-commit>" build metadata for a clean display version.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
