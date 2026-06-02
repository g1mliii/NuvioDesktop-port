using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Core.Settings;
using Nuvio.Desktop.Services;
using Nuvio.Player;

namespace Nuvio.Desktop.ViewModels;

public sealed class PlayerViewModel : ViewModelBase, IAsyncDisposable
{
    private static readonly TimeSpan PlayerEventUiUpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly IPlayerEngineFactory _engineFactory;
    private readonly Func<Task> _returnToBrowseAsync;
    private readonly Action<Action> _dispatchToUi;
    private readonly ISettingsStore? _settingsStore;
    private readonly IPlayerProgressRecorder? _progressRecorder;
    private readonly object _pendingEventLock = new();
    private IPlayerEngine? _engine;
    private CancellationTokenSource? _lifetime;
    private Task? _eventsTask;
    private IPlayerRenderSource? _renderFailureSource;
    private EventHandler<PlayerRenderFailureEventArgs>? _renderFailureHandler;
    private PlayerEvent.PlaybackPositionChanged? _pendingPositionEvent;
    private string? _pendingLogStatus;
    private int _playbackGeneration;
    private int _isDisposed;
    private bool _positionUpdateQueued;
    private bool _logStatusUpdateQueued;
    private string _title = "Player";
    private string _streamTitle = "No stream loaded";
    private string _status = "Idle";
    private string _errorMessage = string.Empty;
    private string? _fallbackDiagnosticStatus;
    private bool _isPlaying;
    private bool _isBuffering;
    private bool _isAvailable;
    private bool _isFullscreenIntent;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan? _duration;
    private int _volume = PlayerOptions.ExternalMpvDefault.InitialVolume;
    private IPlayerRenderSource? _videoSource;

    public PlayerViewModel(
        IPlayerEngineFactory engineFactory,
        Func<Task> returnToBrowseAsync,
        Action<Action>? dispatchToUi = null,
        ISettingsStore? settingsStore = null,
        IPlayerProgressRecorder? progressRecorder = null)
    {
        _engineFactory = engineFactory;
        _returnToBrowseAsync = returnToBrowseAsync;
        _dispatchToUi = dispatchToUi ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _settingsStore = settingsStore;
        _progressRecorder = progressRecorder;
        PlayCommand = new AsyncRelayCommand(PlayAsync);
        PauseCommand = new AsyncRelayCommand(PauseAsync);
        TogglePlayPauseCommand = new AsyncRelayCommand(TogglePlayPauseAsync);
        SeekBackwardCommand = new AsyncRelayCommand(() => SeekByAsync(TimeSpan.FromSeconds(-10)));
        SeekForwardCommand = new AsyncRelayCommand(() => SeekByAsync(TimeSpan.FromSeconds(10)));
        StopCommand = new AsyncRelayCommand(StopAsync);
        ToggleFullscreenCommand = new AsyncRelayCommand(ToggleFullscreenAsync);
        ReturnToBrowseCommand = new AsyncRelayCommand(ReturnToBrowseAsync);
    }

    public IAsyncRelayCommand PlayCommand { get; }

    public IAsyncRelayCommand PauseCommand { get; }

    public IAsyncRelayCommand TogglePlayPauseCommand { get; }

    public IAsyncRelayCommand SeekBackwardCommand { get; }

    public IAsyncRelayCommand SeekForwardCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IAsyncRelayCommand ToggleFullscreenCommand { get; }

    public IAsyncRelayCommand ReturnToBrowseCommand { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string StreamTitle
    {
        get => _streamTitle;
        private set => SetProperty(ref _streamTitle, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public bool IsBuffering
    {
        get => _isBuffering;
        private set => SetProperty(ref _isBuffering, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set => SetProperty(ref _isAvailable, value);
    }

    public bool IsFullscreenIntent
    {
        get => _isFullscreenIntent;
        private set => SetProperty(ref _isFullscreenIntent, value);
    }

    public TimeSpan Position
    {
        get => _position;
        private set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(PositionLabel));
            }
        }
    }

    public TimeSpan? Duration
    {
        get => _duration;
        private set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(PositionLabel));
            }
        }
    }

    /// <summary>
    /// The active embedded render source when in-app playback is in use; null for external mpv. The player
    /// view binds its video surface to this without depending on the concrete engine backend.
    /// </summary>
    public IPlayerRenderSource? VideoSource
    {
        get => _videoSource;
        private set => SetProperty(ref _videoSource, value);
    }

    public string PositionLabel => Duration is { } duration
        ? $"{FormatTime(Position)} / {FormatTime(duration)}"
        : FormatTime(Position);

    public int Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _volume, clamped))
            {
                _ = SetVolumeAsync(clamped);
            }
        }
    }

    public async Task LoadAndPlayAsync(StreamSource source, MediaDetails details, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        _playbackGeneration++;
        await ResetEngineAsync();
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var playbackToken = _lifetime.Token;
        var generation = _playbackGeneration;

        Title = details.Name;
        StreamTitle = source.Title ?? "Fixture stream";
        ClearFallbackDiagnosticStatus();
        Status = "Starting external mpv";
        ErrorMessage = string.Empty;
        Position = TimeSpan.Zero;
        Duration = null;
        IsPlaying = false;
        IsBuffering = true;
        _progressRecorder?.Start(details);

        try
        {
            var settings = _settingsStore is null
                ? DesktopSettings.Default
                : await _settingsStore.LoadAsync(playbackToken);
            var playerOptions = ToPlayerOptions(settings);
            Volume = playerOptions.InitialVolume;

            var libMpvRequested = string.Equals(playerOptions.PreferredEngine, SelectingPlayerEngineFactory.LibMpvEngineId, StringComparison.Ordinal);
            var engine = _engineFactory.Create(playerOptions);
            var probeFellBackToExternal = libMpvRequested && engine is not IPlayerRenderSource;
            var embeddedRenderSourceActive = libMpvRequested && engine is IPlayerRenderSource;

            try
            {
                await StartPlaybackAsync(engine, source, playerOptions, playbackToken, generation);
            }
            catch (Exception) when (embeddedRenderSourceActive
                && !playbackToken.IsCancellationRequested
                && generation == _playbackGeneration)
            {
                // Runtime engine failover (parity with upstream's startup engine failover): embedded libmpv
                // failed to start, so switch to external mpv for the same source rather than stranding the user.
                // Bump the generation first so any events still draining from the failed libmpv engine
                // (PlayerError / AvailabilityChanged(false)) are gated out and can't clobber the external-mpv
                // UI state we establish below.
                generation = ++_playbackGeneration;
                await DisposeCurrentEngineForFailoverAsync();
                var externalOptions = playerOptions with { PreferredEngine = "external-mpv" };
                await StartPlaybackAsync(_engineFactory.Create(externalOptions), source, externalOptions, playbackToken, generation);
                if (generation != _playbackGeneration || playbackToken.IsCancellationRequested)
                {
                    return;
                }

                IsPlaying = true;
                IsBuffering = false;
                ErrorMessage = string.Empty;
                SetFallbackDiagnosticStatus("Embedded libmpv failed to start; switched to external mpv");
                return;
            }

            if (generation != _playbackGeneration || playbackToken.IsCancellationRequested)
            {
                return;
            }

            IsPlaying = true;
            IsBuffering = false;
            if (probeFellBackToExternal)
            {
                SetFallbackDiagnosticStatus("libmpv unavailable; playing through external mpv");
            }
            else
            {
                Status = embeddedRenderSourceActive
                    ? "Playing through embedded libmpv"
                    : "Playing through IPlayerEngine";
            }
        }
        catch (OperationCanceledException) when (playbackToken.IsCancellationRequested)
        {
            Status = "Playback canceled";
        }
        catch (Exception ex)
        {
            if (generation != _playbackGeneration || playbackToken.IsCancellationRequested)
            {
                // A newer playback superseded this one (which already tore down our engine), or it was
                // cancelled — don't reset the current engine or surface a stale error over the new session.
                return;
            }

            var message = ex.Message;
            await ResetEngineAsync();
            IsBuffering = false;
            IsPlaying = false;
            Status = "Player error";
            ErrorMessage = message;
        }
    }

    private async Task StartPlaybackAsync(
        IPlayerEngine engine,
        StreamSource source,
        PlayerOptions options,
        CancellationToken playbackToken,
        int generation)
    {
        _engine = engine;
        await engine.InitializeAsync(options, playbackToken);
        AttachRenderFailureHandler(engine as IPlayerRenderSource, source, options, playbackToken, generation);
        _eventsTask = ObserveEventsAsync(engine, playbackToken, generation);
        await engine.LoadAsync(source, playbackToken);
        await engine.PlayAsync(playbackToken);
    }

    private void AttachRenderFailureHandler(
        IPlayerRenderSource? renderSource,
        StreamSource source,
        PlayerOptions options,
        CancellationToken playbackToken,
        int generation)
    {
        DetachRenderFailureHandler();
        VideoSource = renderSource;
        if (renderSource is null)
        {
            return;
        }

        EventHandler<PlayerRenderFailureEventArgs> handler = (_, args) =>
        {
            _dispatchToUi(() => _ = FailOverAfterRenderFailureAsync(source, options, playbackToken, generation, args.Exception));
        };
        _renderFailureSource = renderSource;
        _renderFailureHandler = handler;
        renderSource.RenderFailed += handler;
    }

    private void DetachRenderFailureHandler()
    {
        if (_renderFailureSource is not null && _renderFailureHandler is not null)
        {
            _renderFailureSource.RenderFailed -= _renderFailureHandler;
        }

        _renderFailureSource = null;
        _renderFailureHandler = null;
    }

    private async Task FailOverAfterRenderFailureAsync(
        StreamSource source,
        PlayerOptions playerOptions,
        CancellationToken playbackToken,
        int generation,
        Exception exception)
    {
        if (!string.Equals(playerOptions.PreferredEngine, SelectingPlayerEngineFactory.LibMpvEngineId, StringComparison.Ordinal)
            || generation != _playbackGeneration
            || playbackToken.IsCancellationRequested)
        {
            return;
        }

        generation = ++_playbackGeneration;
        await DisposeCurrentEngineForFailoverAsync();
        if (generation != _playbackGeneration || playbackToken.IsCancellationRequested)
        {
            return;
        }

        var externalOptions = playerOptions with { PreferredEngine = "external-mpv" };
        try
        {
            await StartPlaybackAsync(_engineFactory.Create(externalOptions), source, externalOptions, playbackToken, generation);
            if (generation != _playbackGeneration || playbackToken.IsCancellationRequested)
            {
                return;
            }

            IsPlaying = true;
            IsBuffering = false;
            ErrorMessage = string.Empty;
            SetFallbackDiagnosticStatus($"Embedded libmpv render failed; switched to external mpv ({exception.Message})");
        }
        catch (OperationCanceledException) when (playbackToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (generation != _playbackGeneration || playbackToken.IsCancellationRequested)
            {
                return;
            }

            var message = ex.Message;
            await ResetEngineAsync();
            IsBuffering = false;
            IsPlaying = false;
            Status = "Player error";
            ErrorMessage = message;
        }
    }

    private async Task DisposeCurrentEngineForFailoverAsync()
    {
        // Tear down the failed engine without cancelling the lifetime, so the failover attempt reuses the
        // same cancellation token and keeps the progress recorder running. (The caller bumps the playback
        // generation so stale events from this failed engine are gated out of the UI.)
        var engine = _engine;
        var eventsTask = _eventsTask;
        _engine = null;
        _eventsTask = null;
        DetachRenderFailureHandler();
        VideoSource = null;

        if (engine is not null)
        {
            await engine.DisposeAsync();
        }

        if (eventsTask is not null)
        {
            try
            {
                await eventsTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async Task ExitFullscreenOrReturnAsync()
    {
        if (IsFullscreenIntent)
        {
            await SetFullscreenAsync(false);
            return;
        }

        await ReturnToBrowseAsync();
    }

    private async Task PlayAsync()
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.PlayAsync(PlaybackToken);
        ClearFallbackDiagnosticStatus();
        IsPlaying = true;
        Status = "Playing";
    }

    private async Task PauseAsync()
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.PauseAsync(PlaybackToken);
        ClearFallbackDiagnosticStatus();
        IsPlaying = false;
        Status = "Paused";
        await FlushProgressAsync(isEnded: false, PlaybackToken);
    }

    private Task TogglePlayPauseAsync() => IsPlaying ? PauseAsync() : PlayAsync();

    private async Task SeekByAsync(TimeSpan offset)
    {
        if (_engine is null)
        {
            return;
        }

        var target = Position + offset;
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        await _engine.SeekAsync(target, PlaybackToken);
        ClearFallbackDiagnosticStatus();
        Position = target;
        Status = $"Seek {FormatTime(target)}";
    }

    private async Task StopAsync()
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.StopAsync(PlaybackToken);
        ClearFallbackDiagnosticStatus();
        IsPlaying = false;
        Status = "Stopped";
        await FlushProgressAsync(isEnded: false, PlaybackToken);
    }

    private Task ToggleFullscreenAsync() => SetFullscreenAsync(!IsFullscreenIntent);

    private async Task ReturnToBrowseAsync()
    {
        await FlushProgressAsync(isEnded: false, CancellationToken.None);
        await _returnToBrowseAsync();
    }

    private async Task SetFullscreenAsync(bool isFullscreen)
    {
        if (_engine is not null)
        {
            await _engine.SetFullscreenAsync(isFullscreen, PlaybackToken);
        }

        ClearFallbackDiagnosticStatus();
        IsFullscreenIntent = isFullscreen;
        Status = isFullscreen ? "Fullscreen requested" : "Windowed playback requested";
    }

    private async Task SetVolumeAsync(int volume)
    {
        if (_engine is not null)
        {
            try
            {
                await _engine.SetVolumeAsync(volume, PlaybackToken);
            }
            catch (OperationCanceledException) when (PlaybackToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                ClearFallbackDiagnosticStatus();
                Status = "Player error";
                ErrorMessage = ex.Message;
            }
        }
    }

    private async Task ObserveEventsAsync(IPlayerEngine engine, CancellationToken cancellationToken, int generation)
    {
        try
        {
            await foreach (var playerEvent in engine.Events(cancellationToken))
            {
                await QueuePlayerEventAsync(playerEvent, cancellationToken, generation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _dispatchToUi(() =>
            {
                if (generation == _playbackGeneration && !cancellationToken.IsCancellationRequested)
                {
                    Status = "Player error";
                    ErrorMessage = ex.Message;
                }
            });
        }
    }

    private async Task QueuePlayerEventAsync(PlayerEvent playerEvent, CancellationToken cancellationToken, int generation)
    {
        await RecordProgressAsync(playerEvent, cancellationToken).ConfigureAwait(false);
        switch (playerEvent)
        {
            case PlayerEvent.PlaybackPositionChanged position:
                QueuePositionEvent(position, cancellationToken, generation);
                break;
            case PlayerEvent.PlayerLog log:
                QueueLogStatus(log.Message, cancellationToken, generation);
                break;
            default:
                DispatchPlayerEvent(playerEvent, cancellationToken, generation);
                break;
        }
    }

    private void DispatchPlayerEvent(PlayerEvent playerEvent, CancellationToken cancellationToken, int generation)
    {
        _dispatchToUi(() =>
        {
            if (generation == _playbackGeneration && !cancellationToken.IsCancellationRequested)
            {
                ApplyPlayerEvent(playerEvent);
            }
        });
    }

    private void QueuePositionEvent(
        PlayerEvent.PlaybackPositionChanged position,
        CancellationToken cancellationToken,
        int generation)
    {
        lock (_pendingEventLock)
        {
            _pendingPositionEvent = position;
            if (_positionUpdateQueued)
            {
                return;
            }

            _positionUpdateQueued = true;
        }

        _ = FlushPositionEventAsync(cancellationToken, generation);
    }

    private async Task FlushPositionEventAsync(CancellationToken cancellationToken, int generation)
    {
        try
        {
            await Task.Delay(PlayerEventUiUpdateInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearPendingPositionEvent();
            return;
        }

        PlayerEvent.PlaybackPositionChanged? latest;
        lock (_pendingEventLock)
        {
            latest = _pendingPositionEvent;
            _pendingPositionEvent = null;
            _positionUpdateQueued = false;
        }

        if (latest is not null)
        {
            DispatchPlayerEvent(latest, cancellationToken, generation);
        }
    }

    private void ClearPendingPositionEvent()
    {
        lock (_pendingEventLock)
        {
            _pendingPositionEvent = null;
            _positionUpdateQueued = false;
        }
    }

    private void QueueLogStatus(string message, CancellationToken cancellationToken, int generation)
    {
        lock (_pendingEventLock)
        {
            _pendingLogStatus = message;
            if (_logStatusUpdateQueued)
            {
                return;
            }

            _logStatusUpdateQueued = true;
        }

        _ = FlushLogStatusAsync(cancellationToken, generation);
    }

    private async Task FlushLogStatusAsync(CancellationToken cancellationToken, int generation)
    {
        try
        {
            await Task.Delay(PlayerEventUiUpdateInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearPendingLogStatus();
            return;
        }

        string? latest;
        lock (_pendingEventLock)
        {
            latest = _pendingLogStatus;
            _pendingLogStatus = null;
            _logStatusUpdateQueued = false;
        }

        if (!string.IsNullOrWhiteSpace(latest))
        {
            _dispatchToUi(() =>
            {
                if (generation == _playbackGeneration && !cancellationToken.IsCancellationRequested)
                {
                    Status = latest;
                }
            });
        }
    }

    private void ClearPendingLogStatus()
    {
        lock (_pendingEventLock)
        {
            _pendingLogStatus = null;
            _logStatusUpdateQueued = false;
        }
    }

    private void ApplyPlayerEvent(PlayerEvent playerEvent)
    {
        var preserveFallbackStatus = ShouldPreserveFallbackDiagnosticStatus(playerEvent);
        switch (playerEvent)
        {
            case PlayerEvent.AvailabilityChanged availability:
                IsAvailable = availability.IsAvailable;
                if (!preserveFallbackStatus)
                {
                    Status = availability.IsAvailable
                        ? $"mpv available {availability.Version}".Trim()
                        : "mpv unavailable";
                }

                break;
            case PlayerEvent.PlaybackStateChanged state:
                IsPlaying = state.IsPlaying;
                if (!preserveFallbackStatus)
                {
                    Status = state.IsPlaying ? "Playing" : "Paused";
                }

                break;
            case PlayerEvent.PlaybackPositionChanged position:
                Position = position.Position;
                Duration = position.Duration;
                break;
            case PlayerEvent.FileLoaded:
                if (!preserveFallbackStatus)
                {
                    Status = "Stream loaded";
                }

                break;
            case PlayerEvent.PlaybackEnded ended:
                ClearFallbackDiagnosticStatus();
                IsPlaying = false;
                Status = string.IsNullOrWhiteSpace(ended.Reason) ? "Playback ended" : $"Playback ended: {ended.Reason}";
                break;
            case PlayerEvent.BufferingStateChanged buffering:
                IsBuffering = buffering.IsBuffering;
                if (!preserveFallbackStatus && !string.IsNullOrWhiteSpace(buffering.Summary))
                {
                    Status = buffering.Summary;
                }

                break;
            case PlayerEvent.PlayerError error:
                ClearFallbackDiagnosticStatus();
                ErrorMessage = error.Message;
                Status = "Player error";
                break;
            case PlayerEvent.PlayerLog log:
                if (!preserveFallbackStatus)
                {
                    Status = log.Message;
                }

                break;
        }
    }

    private void SetFallbackDiagnosticStatus(string message)
    {
        _fallbackDiagnosticStatus = message;
        Status = message;
    }

    private void ClearFallbackDiagnosticStatus()
    {
        _fallbackDiagnosticStatus = null;
    }

    private bool ShouldPreserveFallbackDiagnosticStatus(PlayerEvent playerEvent) =>
        _fallbackDiagnosticStatus is not null &&
        playerEvent is PlayerEvent.AvailabilityChanged
            or PlayerEvent.PlaybackStateChanged
            or PlayerEvent.FileLoaded
            or PlayerEvent.BufferingStateChanged
            or PlayerEvent.PlayerLog;

    private async Task ResetEngineAsync()
    {
        _playbackGeneration++;
        var lifetime = _lifetime;
        var eventsTask = _eventsTask;
        var engine = _engine;
        _lifetime = null;
        _eventsTask = null;
        _engine = null;
        DetachRenderFailureHandler();
        VideoSource = null;
        ClearFallbackDiagnosticStatus();

        if (lifetime is not null)
        {
            await lifetime.CancelAsync();
            lifetime.Dispose();
        }

        if (engine is not null)
        {
            await engine.DisposeAsync();
        }

        if (eventsTask is not null)
        {
            try
            {
                await eventsTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        await FlushProgressAsync(isEnded: false, CancellationToken.None);
        ClearPendingPositionEvent();
        ClearPendingLogStatus();
        _progressRecorder?.Reset();
    }

    private Task RecordProgressAsync(PlayerEvent playerEvent, CancellationToken cancellationToken) =>
        SafeProgressAsync(recorder => recorder.RecordAsync(playerEvent, cancellationToken), cancellationToken);

    private Task FlushProgressAsync(bool isEnded, CancellationToken cancellationToken) =>
        SafeProgressAsync(recorder => recorder.FlushAsync(isEnded, cancellationToken), cancellationToken);

    private async Task SafeProgressAsync(Func<IPlayerProgressRecorder, Task> action, CancellationToken cancellationToken)
    {
        if (_progressRecorder is null)
        {
            return;
        }

        try
        {
            await action(_progressRecorder).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        await ResetEngineAsync();
        _progressRecorder?.Dispose();
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");

    private static PlayerOptions ToPlayerOptions(DesktopSettings settings)
    {
        var normalized = settings.Normalize();
        return PlayerOptions.ExternalMpvDefault with
        {
            PreferredEngine = normalized.PlayerMode == PlayerMode.LibMpv
                ? SelectingPlayerEngineFactory.LibMpvEngineId
                : "external-mpv",
            InitialVolume = normalized.InitialVolume,
            HardwareDecodingEnabled = normalized.HardwareDecodingEnabled
        };
    }

    private CancellationToken PlaybackToken => _lifetime?.Token ?? CancellationToken.None;
}
