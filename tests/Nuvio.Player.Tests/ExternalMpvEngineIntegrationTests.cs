using System.Diagnostics;
using Nuvio.Core.Models;
using Nuvio.Platform;
using Nuvio.Player.ExternalMpv;

namespace Nuvio.Player.Tests;

public sealed class ExternalMpvEngineIntegrationTests
{
    [Fact]
    public async Task ExternalMpvEngine_LoadsGeneratedFixtureAndControlsPlayback_WhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NUVIO_RUN_MPV_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var discovery = new MpvProcessLocator().Locate();
        Assert.True(discovery.IsAvailable, discovery.Message);

        var fixturePath = CreateGeneratedWavFixture();
        var events = new List<PlayerEvent>();
        var engine = new ExternalMpvEngine();
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

        var options = PlayerOptions.ExternalMpvDefault with
        {
            MpvOptions = new Dictionary<string, string>(ExternalMpvQualityDefaults.Options)
            {
                ["vo"] = "null",
                ["ao"] = "null"
            }
        };

        try
        {
            await engine.InitializeAsync(options, cancellation.Token);
            await engine.LoadAsync(new StreamSource("generated-wav", new Uri(fixturePath), "Generated WAV", "audio", new Dictionary<string, string>(), [], true), cancellation.Token);
            await WaitForEventAsync(events, playerEvent => playerEvent is PlayerEvent.FileLoaded, cancellation.Token);
            await engine.PlayAsync(cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await engine.PauseAsync(cancellation.Token);
            await engine.SeekAsync(TimeSpan.FromSeconds(1), cancellation.Token);
            await engine.SetVolumeAsync(35, cancellation.Token);
            await engine.SetFullscreenAsync(false, cancellation.Token);
            await engine.SelectSubtitleTrackAsync(null, cancellation.Token);
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

    [Fact]
    public async Task ExternalMpvEngine_RejectsRepeatedInitialize_WhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NUVIO_RUN_MPV_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var discovery = new MpvProcessLocator().Locate();
        Assert.True(discovery.IsAvailable, discovery.Message);

        await using var engine = new ExternalMpvEngine();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var options = PlayerOptions.ExternalMpvDefault with
        {
            MpvOptions = new Dictionary<string, string>(ExternalMpvQualityDefaults.Options)
            {
                ["vo"] = "null",
                ["ao"] = "null"
            }
        };

        await engine.InitializeAsync(options, cancellation.Token);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.InitializeAsync(options, cancellation.Token));
        Assert.Contains("already initialized", exception.Message);
    }

    // Phase 9 (9.5 / 9.12): repeated load/play/stop/dispose must not leak orphan mpv child processes. After
    // the loop, the count of running mpv processes must return to the pre-loop baseline.
    [Fact]
    public async Task ExternalMpvEngine_RepeatedStartStop_LeavesNoOrphanProcess_WhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NUVIO_RUN_MPV_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var discovery = new MpvProcessLocator().Locate();
        Assert.True(discovery.IsAvailable, discovery.Message);

        var baseline = CountMpvProcesses();
        var fixturePath = CreateGeneratedWavFixture();
        var options = PlayerOptions.ExternalMpvDefault with
        {
            MpvOptions = new Dictionary<string, string>(ExternalMpvQualityDefaults.Options)
            {
                ["vo"] = "null",
                ["ao"] = "null"
            }
        };

        try
        {
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var events = new List<PlayerEvent>();
                var engine = new ExternalMpvEngine();
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

                try
                {
                    await engine.InitializeAsync(options, cancellation.Token);
                    await engine.LoadAsync(
                        new StreamSource("generated-wav", new Uri(fixturePath), "Generated WAV", "audio", new Dictionary<string, string>(), [], true),
                        cancellation.Token);
                    await WaitForEventAsync(events, playerEvent => playerEvent is PlayerEvent.FileLoaded, cancellation.Token);
                    await engine.PlayAsync(cancellation.Token);
                    await Task.Delay(300, cancellation.Token);
                    await engine.StopAsync(cancellation.Token);
                }
                finally
                {
                    await engine.DisposeAsync();
                    await eventCancellation.CancelAsync();
                    await WaitForEventsAsync(eventTask);
                }
            }
        }
        finally
        {
            DeleteFixture(fixturePath);
        }

        var after = await WaitForProcessCountAtMostAsync(baseline, TimeSpan.FromSeconds(10));
        Assert.True(after <= baseline, $"orphan mpv processes detected: baseline={baseline}, after={after}");
    }

    private static int CountMpvProcesses()
    {
        var processes = Process.GetProcessesByName("mpv");
        try
        {
            return processes.Length;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static async Task<int> WaitForProcessCountAtMostAsync(int target, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var count = CountMpvProcesses();
        while (count > target)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(100);
            count = CountMpvProcesses();
        }

        return count;
    }

    private static string CreateGeneratedWavFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nuvio-generated-{Guid.NewGuid():N}.wav");
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

    private static void DeleteFixture(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
