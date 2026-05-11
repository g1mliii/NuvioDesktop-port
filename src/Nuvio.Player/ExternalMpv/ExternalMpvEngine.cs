using System.Diagnostics;
using System.Threading.Channels;
using Nuvio.Core.Models;
using Nuvio.Platform;

namespace Nuvio.Player.ExternalMpv;

public sealed class ExternalMpvEngine : IPlayerEngine
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly MpvProcessLocator _locator;
    private readonly Channel<PlayerEvent> _events = Channel.CreateBounded<PlayerEvent>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    private readonly MpvEventMapper _eventMapper = new();
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private MpvIpcEndpoint? _endpoint;
    private MpvIpcClient? _client;
    private Process? _process;
    private Task? _stderrTask;
    private Task? _exitTask;
    private bool _isDisposed;

    public ExternalMpvEngine()
        : this(new MpvProcessLocator())
    {
    }

    public ExternalMpvEngine(MpvProcessLocator locator)
    {
        _locator = locator;
    }

    public async Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_client is not null || _process is not null)
            {
                throw new InvalidOperationException("mpv is already initialized for this player engine.");
            }

            var discovery = _locator.Locate();
            if (!discovery.IsAvailable || string.IsNullOrWhiteSpace(discovery.ExecutablePath))
            {
                WriteEvent(new PlayerEvent.AvailabilityChanged(false, null, DateTimeOffset.UtcNow));
                throw new InvalidOperationException(discovery.Message ?? "mpv is not available.");
            }

            _lifetime = new CancellationTokenSource();

            try
            {
                _endpoint = MpvIpcEndpoint.Create();
                _process = StartMpv(discovery.ExecutablePath, _endpoint.MpvArgument, options);
                _stderrTask = ReadDiagnosticsAsync(_process, _lifetime.Token);
                _exitTask = WatchExitAsync(_process, _lifetime.Token);

                var stream = await _endpoint.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
                _client = new MpvIpcClient(stream);
                _client.EventReceived += HandleMpvEvent;
                _client.ErrorReceived += exception => WriteError(MpvCommandLogRedactor.Redact(exception.Message));

                await SendAsync(MpvCommands.RequestLogMessages("warn"), cancellationToken, ignoreFailure: true).ConfigureAwait(false);
                await ObservePhaseTwoPropertiesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }

            WriteEvent(new PlayerEvent.AvailabilityChanged(true, discovery.Version, DateTimeOffset.UtcNow));
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task LoadAsync(StreamSource source, CancellationToken cancellationToken)
    {
        await SendAsync(MpvCommands.LoadFile(source), cancellationToken).ConfigureAwait(false);

        foreach (var subtitle in source.Subtitles.Where(subtitle => subtitle.Url is not null))
        {
            await SendAsync(MpvCommands.AddSubtitle(subtitle), cancellationToken, ignoreFailure: true).ConfigureAwait(false);
        }
    }

    public Task PlayAsync(CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.Play(), cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.Pause(), cancellationToken);

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.Seek(position), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.Stop(), cancellationToken);

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.SetVolume(volume), cancellationToken);

    public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.SetFullscreen(isFullscreen), cancellationToken);

    public Task SelectAudioTrackAsync(string trackId, CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.SelectAudioTrack(trackId), cancellationToken);

    public Task SelectSubtitleTrackAsync(string? trackId, CancellationToken cancellationToken) =>
        SendAsync(MpvCommands.SelectSubtitleTrack(trackId), cancellationToken);

    public IAsyncEnumerable<PlayerEvent> Events(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    private async Task ObservePhaseTwoPropertiesAsync(CancellationToken cancellationToken)
    {
        var properties = new[]
        {
            "pause",
            "time-pos",
            "duration",
            "idle-active",
            "demuxer-cache-state",
            "track-list",
            "aid",
            "sid"
        };

        for (var index = 0; index < properties.Length; index++)
        {
            await SendAsync(MpvCommands.ObserveProperty(index + 1, properties[index]), cancellationToken, ignoreFailure: true).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(IReadOnlyList<object?> command, CancellationToken cancellationToken, bool ignoreFailure = false)
    {
        ThrowIfDisposed();

        var client = _client ?? throw new InvalidOperationException("mpv is not initialized.");

        try
        {
            await client.SendAsync(command, CommandTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ignoreFailure)
        {
            WriteError(MpvCommandLogRedactor.Redact(ex.Message));
        }
    }

    private Process StartMpv(string executablePath, string ipcEndpoint, PlayerOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in ExternalMpvQualityDefaults.ToCommandLineArguments(options.MpvOptions))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--idle=yes");
        startInfo.ArgumentList.Add("--terminal=no");
        startInfo.ArgumentList.Add("--input-terminal=no");
        startInfo.ArgumentList.Add($"--input-ipc-server={ipcEndpoint}");

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start mpv.");
    }

    private async Task ReadDiagnosticsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var message = MpvCommandLogRedactor.Redact(line);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    WriteEvent(new PlayerEvent.PlayerLog(message, DateTimeOffset.UtcNow));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            WriteError(MpvCommandLogRedactor.Redact(ex.Message));
        }
    }

    private async Task WatchExitAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (!_isDisposed)
            {
                WriteEvent(new PlayerEvent.PlaybackEnded($"mpv exited with code {process.ExitCode}", DateTimeOffset.UtcNow));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleMpvEvent(System.Text.Json.JsonElement root)
    {
        foreach (var playerEvent in _eventMapper.Map(root))
        {
            WriteEvent(playerEvent);
        }
    }

    private void WriteError(string message) =>
        WriteEvent(new PlayerEvent.PlayerError(message, DateTimeOffset.UtcNow));

    private void WriteEvent(PlayerEvent playerEvent) =>
        _events.Writer.TryWrite(playerEvent);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_client is not null)
        {
            try
            {
                await _client.SendAsync(MpvCommands.Quit(), TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            await _client.DisposeAsync().ConfigureAwait(false);
        }

        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }

        if (_process is not null && !_process.HasExited)
        {
            try
            {
                if (!_process.WaitForExit(milliseconds: 3_000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        await WaitForBackgroundTaskAsync(_stderrTask).ConfigureAwait(false);
        await WaitForBackgroundTaskAsync(_exitTask).ConfigureAwait(false);

        if (_endpoint is not null)
        {
            await _endpoint.DisposeAsync().ConfigureAwait(false);
        }

        _lifetime?.Dispose();
        _process?.Dispose();
        _events.Writer.TryComplete();
    }

    private static async Task WaitForBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ExternalMpvEngine));
        }
    }
}
