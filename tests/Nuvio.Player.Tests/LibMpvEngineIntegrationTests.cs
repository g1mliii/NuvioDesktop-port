using Nuvio.Core.Models;
using Nuvio.Player.LibMpv;

namespace Nuvio.Player.Tests;

/// <summary>
/// Embedded libmpv smoke coverage. Opt-in: set <c>NUVIO_RUN_LIBMPV_INTEGRATION=1</c> on a machine/runner
/// that has libmpv installed (mirrors the external-mpv <c>NUVIO_RUN_MPV_INTEGRATION</c> gate). When the flag
/// is unset the tests no-op so the normal suite stays green without native dependencies.
/// </summary>
public sealed class LibMpvEngineIntegrationTests
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("NUVIO_RUN_LIBMPV_INTEGRATION"), "1", StringComparison.Ordinal);

    [Fact]
    public async Task LibMpvEngine_LoadsGeneratedFixtureAndControlsPlayback_WhenEnabled()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.True(LibMpvLibraryLoader.EnsureLoaded().IsAvailable, "libmpv must be installed for this gated test.");

        var fixturePath = CreateGeneratedWavFixture();
        var events = new List<PlayerEvent>();
        var engine = new LibMpvEngine();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var eventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);

        var eventTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var playerEvent in engine.Events(eventCancellation.Token))
                {
                    lock (events)
                    {
                        events.Add(playerEvent);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        // Audio-only fixture: keep libmpv headless (no video output, render context not attached).
        var options = PlayerOptions.ExternalMpvDefault with
        {
            PreferredEngine = "libmpv",
            MpvOptions = new Dictionary<string, string>(ExternalMpvQualityDefaults.Options)
            {
                ["vo"] = "null",
                ["ao"] = "null"
            }
        };

        try
        {
            await engine.InitializeAsync(options, cancellation.Token);
            await engine.LoadAsync(
                new StreamSource("generated-wav", new Uri(fixturePath), "Generated WAV", "audio", new Dictionary<string, string>(), [], true),
                cancellation.Token);
            await WaitForEventAsync(events, playerEvent => playerEvent is PlayerEvent.FileLoaded, cancellation.Token);
            await engine.PlayAsync(cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await engine.SeekAsync(TimeSpan.FromSeconds(1), cancellation.Token);
            await engine.SetVolumeAsync(35, cancellation.Token);
            await engine.SelectSubtitleTrackAsync(null, cancellation.Token);
            await engine.PauseAsync(cancellation.Token);
            await engine.StopAsync(cancellation.Token);
        }
        finally
        {
            await engine.DisposeAsync();
            await eventCancellation.CancelAsync();
            await WaitForEventsAsync(eventTask);
            DeleteFixture(fixturePath);
        }

        lock (events)
        {
            Assert.Contains(events, playerEvent => playerEvent is PlayerEvent.AvailabilityChanged { IsAvailable: true });
            Assert.Contains(events, playerEvent => playerEvent is PlayerEvent.FileLoaded);
            Assert.Contains(events, playerEvent => playerEvent is PlayerEvent.PlaybackPositionChanged);
        }
    }

    // Phase 9 (9.5 / 9.12): teardown stress. A longer create/dispose loop must not throw and must not leak
    // managed memory (the engine wrapper, P/Invoke GC handles, event channels) across cycles. We assert on
    // the managed heap rather than process working set: GC.GetTotalMemory after a full collect is
    // deterministic, whereas WorkingSet64 swings with the allocator/JIT/native decode buffers and flakes.
    [Fact]
    public async Task LibMpvEngine_RepeatedCreateDispose_DoesNotThrowOrLeak_WhenEnabled()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.True(LibMpvLibraryLoader.EnsureLoaded().IsAvailable, "libmpv must be installed for this gated test.");

        var options = PlayerOptions.ExternalMpvDefault with
        {
            PreferredEngine = "libmpv",
            MpvOptions = new Dictionary<string, string>(ExternalMpvQualityDefaults.Options)
            {
                ["vo"] = "null",
                ["ao"] = "null"
            }
        };

        // Warm up once so first-load JIT/allocations don't count as "growth".
        await CreatePlayDisposeAsync(options);
        var baseline = SettledManagedBytes();

        for (var iteration = 0; iteration < 12; iteration++)
        {
            await CreatePlayDisposeAsync(options);
        }

        var growth = SettledManagedBytes() - baseline;
        // Generous bound: catches a gross managed leak across 12 cycles (which would grow roughly linearly)
        // without flaking on normal allocator noise.
        Assert.True(growth < 16L * 1024 * 1024, $"libmpv create/dispose managed heap grew {growth} bytes across 12 cycles");
    }

    private static long SettledManagedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static async Task CreatePlayDisposeAsync(PlayerOptions options)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var engine = new LibMpvEngine();
        await engine.InitializeAsync(options, cancellation.Token);
        await engine.PlayAsync(cancellation.Token);
        await engine.DisposeAsync();
    }

    private static string CreateGeneratedWavFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nuvio-libmpv-{Guid.NewGuid():N}.wav");
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

    private static void DeleteFixture(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static async Task WaitForEventsAsync(Task eventTask)
    {
        try
        {
            await eventTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
        }
    }

    private static async Task WaitForEventAsync(List<PlayerEvent> events, Func<PlayerEvent, bool> predicate, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (events)
            {
                if (events.Any(predicate))
                {
                    return;
                }
            }

            await Task.Delay(50, cancellationToken);
        }
    }
}
