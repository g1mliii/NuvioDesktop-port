using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Nuvio.Core.Models;
using Nuvio.Data;
using Nuvio.Data.Sqlite;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;
using Nuvio.Platform;
using Nuvio.Player;
using Nuvio.Player.ExternalMpv;

namespace Nuvio.Desktop;

/// <summary>
/// Phase 9 performance/memory probe (9.1, 9.2). A headless, cross-platform CLI mode (modeled on
/// <see cref="SelfCheck"/>) that boots the real service graph and runs scripted scenarios, reporting
/// elapsed time and managed-heap deltas as JSON. The same binary runs identically on Windows, macOS,
/// and Linux, so the CI matrix gives a cross-platform memory baseline without OS-specific scripts.
///
/// Division of labour: the probe owns non-render scenarios measured by managed heap + timing. Deterministic
/// UI-bound assertions (poster-grid virtualization caps, decoded/disk image-cache caps, cancellation
/// no-stale, fake-engine no-leak) live in the hard-failing xunit Phase 9 tests, which is the right place
/// for assertions. Absolute budgets here are reported, not enforced (report-only gate, see docs/perf/).
/// </summary>
internal static class PerfProbe
{
    internal const string FlagArg = "--perf-probe";
    internal const string FlagEnv = "NUVIO_PERF_PROBE";
    internal const string OutArg = "--perf-out";
    internal const string OutEnv = "NUVIO_PERF_OUT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
    /// Runs the probe, always writing the JSON report to <paramref name="output"/> and, when an output path
    /// is supplied (<c>--perf-out &lt;file&gt;</c> or <c>NUVIO_PERF_OUT</c>), to that file. Returns 0 on
    /// success. Individual scenario failures are captured per-scenario and return non-zero; skipped optional
    /// scenarios do not fail the run.
    /// </summary>
    public static int Run(string[]? args, TextWriter output)
    {
        try
        {
            var report = BuildReport();
            var json = JsonSerializer.Serialize(report, JsonOptions);
            output.WriteLine(json);
            WriteToFileIfRequested(args, json, output);
            return report.Scenarios.Any(s => string.Equals(s.Status, "error", StringComparison.Ordinal))
                ? 1
                : 0;
        }
        catch (Exception ex)
        {
            output.WriteLine($"Perf probe FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static ProbeReport BuildReport()
    {
        var platform = PlatformInfoProvider.Current();
        var scenarios = new List<ScenarioResult>
        {
            ColdStart(platform),
            CatalogScroll(),
            DetailsOpenCloseLoop(),
            PlaybackStopLoop()
        };

        return new ProbeReport(
            Tool: "nuvio-perf-probe",
            Version: ResolveVersion(),
            Platform: platform.Family.ToString(),
            Rid: platform.RuntimeIdentifier,
            TimestampUtc: DateTimeOffset.UtcNow.ToString("O"),
            GcMode: System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation",
            ProcessorCount: Environment.ProcessorCount,
            Scenarios: scenarios);
    }

    // 9.1 / 9.2: cost of constructing the live SQLite-backed service graph into isolated storage. This is
    // the dominant managed startup cost paid before the first window paints; window paint itself is a manual
    // measurement (see docs/perf/README.md).
    private static ScenarioResult ColdStart(PlatformInfo platform)
    {
        return RunScenario("cold-start", () =>
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var root = CreateTempRoot();
            try
            {
                var paths = PlatformPaths.For(
                    platform.Family,
                    root,
                    _ => null,
                    StoragePlan.Default.DatabaseFileName,
                    StoragePlan.Default.ImageCacheDirectoryName);
                var host = DesktopBootstrap.BuildLive(settingsPath, paths);
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                // Drop pooled SQLite connections so the temp DB handle is released; otherwise on Windows the
                // open handle makes the recursive delete below silently fail and leak the temp root (mirrors
                // SelfCheck.BootServiceGraph).
                SqliteStorage.ReleasePooledConnections();
                TryDeleteTempRoot(root);
            }

            return null;
        });
    }

    // 9.2 / 9.3: retained memory after building and fully realizing a large catalog's poster grid, then
    // releasing it. A near-zero delta proves nothing static holds the grid/cards alive after scroll-away.
    private static ScenarioResult CatalogScroll()
    {
        var items = ReadIntEnv("NUVIO_PERF_CATALOG_ITEMS", 5000);
        long peak = 0;
        var result = RunScenario("catalog-scroll", () =>
        {
            var fixtures = new DesktopFixtureService(itemCount: items);
            var catalog = fixtures.GetCatalogAsync(CancellationToken.None).GetAwaiter().GetResult();
            var openCommand = PosterGridBuilder.CreateOpenCommand(_ => Task.CompletedTask);
            var rows = PosterGridBuilder.BuildRows(catalog, openCommand);

            // Realize every card the way scrolling through the whole catalog would.
            long realizedCards = 0;
            foreach (var row in rows)
            {
                realizedCards += row.Items.Count;
            }

            peak = GC.GetTotalMemory(forceFullCollection: false);
            GC.KeepAlive(rows);
            return $"items={items}, realizedCards={realizedCards}, peakManagedBytes={peak}";
        });

        return result with { PeakManagedBytes = peak };
    }

    // 9.2: retained memory after repeatedly building and discarding media detail state (parser + DTO churn).
    private static ScenarioResult DetailsOpenCloseLoop()
    {
        var loops = ReadIntEnv("NUVIO_PERF_DETAILS_LOOPS", 200);
        return RunScenario("details-loop", () =>
        {
            for (var i = 0; i < loops; i++)
            {
                // Rebuild the fixture every iteration so the media-detail parser runs and the DTO graph is
                // genuinely re-created and discarded per loop. A single shared fixture pre-parses and caches
                // its details, which would make GetDetailsAsync a dictionary lookup and the loop count moot.
                var fixtures = new DesktopFixtureService(itemCount: 1);
                var catalog = fixtures.GetCatalogAsync(CancellationToken.None).GetAwaiter().GetResult();
                var details = fixtures.GetDetailsAsync(catalog[0].Id, CancellationToken.None).GetAwaiter().GetResult();
                GC.KeepAlive(details.Details.Name);
            }

            return $"loops={loops}";
        });
    }

    // 9.2 / 9.5 / 9.9: repeated external-mpv load/play/stop/dispose. Gated on real mpv: when
    // NUVIO_RUN_MPV_INTEGRATION is unset (or mpv is missing) the scenario reports "skipped". With
    // NUVIO_PERF_MEDIA_URL set, a real network stream exercises the demuxer/cache so buffering behavior can
    // inform mpv cache tuning. The deterministic, no-native fake-engine start/stop loop lives in the xunit
    // Phase 9 suite.
    private static ScenarioResult PlaybackStopLoop()
    {
        const string name = "playback-stop";
        if (!string.Equals(Environment.GetEnvironmentVariable("NUVIO_RUN_MPV_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return ScenarioResult.Skipped(name, "set NUVIO_RUN_MPV_INTEGRATION=1 (mpv installed) to run; optional NUVIO_PERF_MEDIA_URL for cache evidence");
        }

        var discovery = new MpvProcessLocator().Locate();
        if (!discovery.IsAvailable)
        {
            return ScenarioResult.Skipped(name, $"external mpv not found: {discovery.Message}");
        }

        try
        {
            return PlaybackStopLoopCoreAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return ScenarioResult.Errored(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<ScenarioResult> PlaybackStopLoopCoreAsync()
    {
        var loops = ReadIntEnv("NUVIO_PERF_PLAYBACK_LOOPS", 5);
        var mediaUrl = Environment.GetEnvironmentVariable("NUVIO_PERF_MEDIA_URL");
        string? fixturePath = null;
        StreamSource source;
        if (!string.IsNullOrWhiteSpace(mediaUrl) && Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri))
        {
            source = new StreamSource("perf-media", uri, "Perf media", null, new Dictionary<string, string>(), [], true);
        }
        else
        {
            fixturePath = CreateGeneratedWavFixture();
            source = new StreamSource("perf-wav", new Uri(fixturePath), "Generated WAV", "audio", new Dictionary<string, string>(), [], true);
        }

        // Keep playback headless so the probe runs on CI runners without a display.
        var options = PlayerOptions.ExternalMpvDefault with
        {
            MpvOptions = new Dictionary<string, string>(ExternalMpvQualityDefaults.Options)
            {
                ["vo"] = "null",
                ["ao"] = "null"
            }
        };

        long maxBufferingObserved = 0;
        GcSettle();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < loops; i++)
            {
                var engine = new ExternalMpvEngine();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var eventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                var fileLoaded = new TaskCompletionSource();
                var pump = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var playerEvent in engine.Events(eventCancellation.Token))
                        {
                            if (playerEvent is PlayerEvent.FileLoaded)
                            {
                                fileLoaded.TrySetResult();
                            }
                            else if (playerEvent is PlayerEvent.BufferingStateChanged { IsBuffering: true })
                            {
                                Interlocked.Increment(ref maxBufferingObserved);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }, eventCancellation.Token);

                try
                {
                    await engine.InitializeAsync(options, cancellation.Token);
                    await engine.LoadAsync(source, cancellation.Token);
                    await Task.WhenAny(fileLoaded.Task, Task.Delay(TimeSpan.FromSeconds(10), cancellation.Token));
                    await engine.PlayAsync(cancellation.Token);
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation.Token);
                    await engine.StopAsync(cancellation.Token);
                }
                finally
                {
                    await engine.DisposeAsync();
                    await eventCancellation.CancelAsync();
                    try
                    {
                        await pump.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (TimeoutException)
                    {
                    }
                }
            }
        }
        finally
        {
            if (fixturePath is not null)
            {
                TryDeleteFile(fixturePath);
            }
        }

        stopwatch.Stop();
        GcSettle();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        var notes = $"loops={loops}, source={(fixturePath is null ? "network" : "generated-wav")}, bufferingEvents={Interlocked.Read(ref maxBufferingObserved)}";
        return new ScenarioResult("playback-stop", "ok", stopwatch.Elapsed.TotalMilliseconds, before, after, after - before, null, CurrentWorkingSet(), notes);
    }

    private static ScenarioResult RunScenario(string name, Func<string?> body)
    {
        GcSettle();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        string? notes;
        try
        {
            notes = body();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ScenarioResult.Errored(name, $"{ex.GetType().Name}: {ex.Message}");
        }

        stopwatch.Stop();
        GcSettle();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        return new ScenarioResult(name, "ok", stopwatch.Elapsed.TotalMilliseconds, before, after, after - before, null, CurrentWorkingSet(), notes);
    }

    private static void WriteToFileIfRequested(string[]? args, string json, TextWriter output)
    {
        var path = ResolveOutPath(args);
        if (path is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
            output.WriteLine($"Perf probe report written to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"Perf probe could not write report file: {ex.Message}");
        }
    }

    private static string? ResolveOutPath(string[]? args)
    {
        if (args is not null)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], OutArg, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
        }

        var fromEnv = Environment.GetEnvironmentVariable(OutEnv);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    private static int ReadIntEnv(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static long CurrentWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }

    private static void GcSettle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"nuvio-perf-{Guid.NewGuid():N}");

    private static void TryDeleteTempRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Mirrors the generated WAV fixture used by the gated mpv integration tests so the probe needs no
    // committed media and never touches a real private stream URL by default.
    private static string CreateGeneratedWavFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nuvio-perf-{Guid.NewGuid():N}.wav");
        const int sampleRate = 8_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int seconds = 3;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = channels * bitsPerSample / 8;
        var dataSize = sampleRate * seconds * blockAlign;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return path;
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(PerfProbe).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private sealed record ProbeReport(
        string Tool,
        string Version,
        string Platform,
        string Rid,
        string TimestampUtc,
        string GcMode,
        int ProcessorCount,
        IReadOnlyList<ScenarioResult> Scenarios);

    private sealed record ScenarioResult(
        string Name,
        string Status,
        double ElapsedMs,
        long ManagedBeforeBytes,
        long ManagedAfterBytes,
        long ManagedDeltaBytes,
        long? PeakManagedBytes,
        long WorkingSetBytes,
        string? Notes)
    {
        public static ScenarioResult Skipped(string name, string notes) =>
            new(name, "skipped", 0, 0, 0, 0, null, 0, notes);

        public static ScenarioResult Errored(string name, string notes) =>
            new(name, "error", 0, 0, 0, 0, null, 0, notes);
    }
}
