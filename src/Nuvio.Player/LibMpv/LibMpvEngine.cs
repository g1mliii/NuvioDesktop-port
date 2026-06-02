using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Nuvio.Core.Models;
using Nuvio.Player.ExternalMpv;
using Nuvio.Platform;

namespace Nuvio.Player.LibMpv;

/// <summary>
/// Embedded player engine backed by libmpv through <see cref="LibMpvNative"/>. Implements the same
/// <see cref="IPlayerEngine"/> contract and emits the same high-level <see cref="PlayerEvent"/> records as
/// <see cref="ExternalMpvEngine"/>, so the desktop view-model is engine-agnostic. Video rendering is layered
/// on separately through <see cref="CreateRenderSession"/>; without a render session the engine still plays
/// audio and reports state (used for headless validation and the render spike).
/// </summary>
public sealed class LibMpvEngine : IPlayerEngine, IPlayerRenderSource
{
    // mpv property names mapped to stable observer ids.
    private const ulong ObsPause = 1;
    private const ulong ObsTimePos = 2;
    private const ulong ObsDuration = 3;
    private const ulong ObsPausedForCache = 4;
    private const ulong ObsTrackList = 5;
    private const ulong ObsAid = 6;
    private const ulong ObsSid = 7;

    private static readonly string[] SkipForEmbedded = ["vo", "gpu-api"];

    private readonly Channel<PlayerEvent> _events = Channel.CreateBounded<PlayerEvent>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly object _stateLock = new();

    private IntPtr _handle;
    private Thread? _eventPump;
    private volatile bool _stopRequested;
    private bool _isDisposed;
    private IPlayerRenderSession? _renderSession;

    private TimeSpan _position;
    private TimeSpan? _duration;
    private IReadOnlyList<PlayerTrack> _tracks = [];
    private string? _selectedAudioTrackId;
    private string? _selectedSubtitleTrackId;

    public event EventHandler<PlayerRenderFailureEventArgs>? RenderFailed;

    public async Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_handle != IntPtr.Zero)
            {
                throw new InvalidOperationException("libmpv is already initialized for this player engine.");
            }

            var load = LibMpvLibraryLoader.EnsureLoaded();
            if (!load.IsAvailable)
            {
                WriteEvent(new PlayerEvent.AvailabilityChanged(false, null, DateTimeOffset.UtcNow));
                throw new InvalidOperationException(load.Message ?? "libmpv is not available.");
            }

            var handle = LibMpvNative.mpv_create();
            if (handle == IntPtr.Zero)
            {
                WriteEvent(new PlayerEvent.AvailabilityChanged(false, load.Version, DateTimeOffset.UtcNow));
                throw new InvalidOperationException("mpv_create failed.");
            }

            ApplyStartupOptions(handle, options);

            var initResult = LibMpvNative.mpv_initialize(handle);
            if (initResult < 0)
            {
                LibMpvNative.mpv_terminate_destroy(handle);
                var reason = LibMpvNative.ErrorString(initResult) ?? "unknown error";
                WriteEvent(new PlayerEvent.AvailabilityChanged(false, load.Version, DateTimeOffset.UtcNow));
                throw new InvalidOperationException($"mpv_initialize failed: {reason}");
            }

            _handle = handle;
            SetPropertyString("volume", Math.Clamp(options.InitialVolume, 0, 100).ToString(CultureInfo.InvariantCulture));
            LibMpvNative.mpv_request_log_messages(handle, "warn");
            ObserveProperties(handle);

            _stopRequested = false;
            _eventPump = new Thread(EventPumpLoop)
            {
                IsBackground = true,
                Name = "libmpv-event-pump"
            };
            _eventPump.Start();

            WriteEvent(new PlayerEvent.AvailabilityChanged(true, load.Version, DateTimeOffset.UtcNow));
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public Task LoadAsync(StreamSource source, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (source.Headers.Count > 0)
        {
            var joined = string.Join(",", source.Headers.Select(header => $"{header.Key}: {header.Value}"));
            SetPropertyString("http-header-fields", joined);
        }

        Command("loadfile", source.Url.AbsoluteUri, "replace");

        foreach (var subtitle in source.Subtitles.Where(subtitle => subtitle.Url is not null))
        {
            Command("sub-add", subtitle.Url!.AbsoluteUri, "auto", subtitle.Label, subtitle.Language ?? string.Empty);
        }

        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken cancellationToken)
    {
        SetPropertyString("pause", "no");
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        SetPropertyString("pause", "yes");
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        Command("seek", position.TotalSeconds.ToString(CultureInfo.InvariantCulture), "absolute");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Command("stop");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken)
    {
        SetPropertyString("volume", Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken)
    {
        SetPropertyString("fullscreen", isFullscreen ? "yes" : "no");
        return Task.CompletedTask;
    }

    public Task SelectAudioTrackAsync(string trackId, CancellationToken cancellationToken)
    {
        SetPropertyString("aid", string.IsNullOrWhiteSpace(trackId) ? "no" : trackId);
        return Task.CompletedTask;
    }

    public Task SelectSubtitleTrackAsync(string? trackId, CancellationToken cancellationToken)
    {
        SetPropertyString("sid", string.IsNullOrWhiteSpace(trackId) ? "no" : trackId);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<PlayerEvent> Events(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Creates an OpenGL render session bound to this engine's mpv handle. Called by the Avalonia render
    /// host. Interop types stay internal; the host supplies a managed GL proc-address resolver and an update
    /// callback (invoked from mpv's render thread, so the host must marshal redraws to the UI thread).
    /// </summary>
    public IPlayerRenderSession CreateRenderSession(Func<string, IntPtr> getProcAddress, Action onUpdate)
    {
        ThrowIfDisposed();
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("libmpv is not initialized.");
        }

        // The engine owns the live render session so it can guarantee the render context is freed before
        // mpv_terminate_destroy, regardless of the host control's teardown timing (libmpv requires this).
        _renderSession?.Dispose();
        _renderSession = new LibMpvRenderSession(_handle, getProcAddress, onUpdate);
        return _renderSession;
    }

    public void ReportRenderFailure(Exception exception)
    {
        RenderFailed?.Invoke(this, new PlayerRenderFailureEventArgs(exception));
    }

    private void ApplyStartupOptions(IntPtr handle, PlayerOptions options)
    {
        foreach (var (key, value) in options.MpvOptions)
        {
            if (SkipForEmbedded.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            if (string.Equals(key, "hwdec", StringComparison.Ordinal))
            {
                LibMpvNative.mpv_set_option_string(handle, "hwdec", options.HardwareDecodingEnabled ? value : "no");
                continue;
            }

            LibMpvNative.mpv_set_option_string(handle, key, value);
        }
    }

    private static void ObserveProperties(IntPtr handle)
    {
        LibMpvNative.mpv_observe_property(handle, ObsPause, "pause", MpvFormat.Flag);
        LibMpvNative.mpv_observe_property(handle, ObsTimePos, "time-pos", MpvFormat.Double);
        LibMpvNative.mpv_observe_property(handle, ObsDuration, "duration", MpvFormat.Double);
        LibMpvNative.mpv_observe_property(handle, ObsPausedForCache, "paused-for-cache", MpvFormat.Flag);
        LibMpvNative.mpv_observe_property(handle, ObsTrackList, "track-list", MpvFormat.None);
        LibMpvNative.mpv_observe_property(handle, ObsAid, "aid", MpvFormat.String);
        LibMpvNative.mpv_observe_property(handle, ObsSid, "sid", MpvFormat.String);
    }

    private void EventPumpLoop()
    {
        while (!_stopRequested)
        {
            var eventPtr = LibMpvNative.mpv_wait_event(_handle, 0.1);
            if (eventPtr == IntPtr.Zero)
            {
                continue;
            }

            var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPtr);
            switch (mpvEvent.EventId)
            {
                case MpvEventId.None:
                    break;
                case MpvEventId.Shutdown:
                    WriteEvent(new PlayerEvent.PlaybackEnded("shutdown", DateTimeOffset.UtcNow));
                    return;
                case MpvEventId.LogMessage:
                    HandleLogMessage(mpvEvent.Data);
                    break;
                case MpvEventId.FileLoaded:
                    WriteEvent(new PlayerEvent.FileLoaded(DateTimeOffset.UtcNow));
                    RefreshTrackList();
                    break;
                case MpvEventId.EndFile:
                    HandleEndFile(mpvEvent.Data);
                    break;
                case MpvEventId.PropertyChange:
                    HandlePropertyChange(mpvEvent.Data);
                    break;
            }
        }
    }

    private void HandlePropertyChange(IntPtr dataPtr)
    {
        if (dataPtr == IntPtr.Zero)
        {
            return;
        }

        var property = Marshal.PtrToStructure<MpvEventProperty>(dataPtr);
        var name = Marshal.PtrToStringUTF8(property.Name);
        if (name is null)
        {
            return;
        }

        var observedAt = DateTimeOffset.UtcNow;
        switch (name)
        {
            case "pause" when TryReadFlag(property, out var paused):
                WriteEvent(new PlayerEvent.PlaybackStateChanged(!paused, observedAt));
                break;
            case "time-pos" when TryReadDouble(property, out var seconds):
                lock (_stateLock)
                {
                    _position = TimeSpan.FromSeconds(seconds);
                    WriteEvent(new PlayerEvent.PlaybackPositionChanged(_position, _duration, observedAt));
                }

                break;
            case "duration" when TryReadDouble(property, out var durationSeconds):
                lock (_stateLock)
                {
                    _duration = TimeSpan.FromSeconds(durationSeconds);
                    WriteEvent(new PlayerEvent.PlaybackPositionChanged(_position, _duration, observedAt));
                }

                break;
            case "paused-for-cache" when TryReadFlag(property, out var buffering):
                WriteEvent(new PlayerEvent.BufferingStateChanged(buffering, buffering ? "Buffering" : null, observedAt));
                break;
            case "track-list":
                RefreshTrackList();
                break;
            case "aid":
                _selectedAudioTrackId = NormalizeTrackId(ReadStringProperty(property));
                EmitTrackList(observedAt);
                break;
            case "sid":
                _selectedSubtitleTrackId = NormalizeTrackId(ReadStringProperty(property));
                EmitTrackList(observedAt);
                break;
        }
    }

    private void HandleLogMessage(IntPtr dataPtr)
    {
        if (dataPtr == IntPtr.Zero)
        {
            return;
        }

        var message = Marshal.PtrToStructure<MpvEventLogMessage>(dataPtr);
        var text = MpvCommandLogRedactor.Redact(Marshal.PtrToStringUTF8(message.Text) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var level = Marshal.PtrToStringUTF8(message.Level);
        var observedAt = DateTimeOffset.UtcNow;
        WriteEvent(level is "error" or "fatal"
            ? new PlayerEvent.PlayerError(text, observedAt)
            : new PlayerEvent.PlayerLog(text, observedAt));
    }

    private void HandleEndFile(IntPtr dataPtr)
    {
        var reason = "eof";
        if (dataPtr != IntPtr.Zero)
        {
            var endReason = (MpvEndFileReason)Marshal.ReadInt32(dataPtr);
            reason = endReason switch
            {
                MpvEndFileReason.Eof => "eof",
                MpvEndFileReason.Stop => "stop",
                MpvEndFileReason.Quit => "quit",
                MpvEndFileReason.Error => "error",
                MpvEndFileReason.Redirect => "redirect",
                _ => "eof"
            };
        }

        WriteEvent(new PlayerEvent.PlaybackEnded(reason, DateTimeOffset.UtcNow));
    }

    private void RefreshTrackList()
    {
        var count = ReadIntProperty("track-list/count");
        if (count <= 0)
        {
            lock (_stateLock)
            {
                _tracks = [];
            }

            EmitTrackList(DateTimeOffset.UtcNow);
            return;
        }

        var tracks = new List<PlayerTrack>(count);
        for (var index = 0; index < count; index++)
        {
            var id = ReadPropertyString($"track-list/{index}/id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var type = ReadPropertyString($"track-list/{index}/type") switch
            {
                "audio" => PlayerTrackType.Audio,
                "sub" => PlayerTrackType.Subtitle,
                "video" => PlayerTrackType.Video,
                _ => PlayerTrackType.Unknown
            };

            var title = ReadPropertyString($"track-list/{index}/title");
            var language = ReadPropertyString($"track-list/{index}/lang");
            var label = title ?? language ?? $"{type} {id}";

            tracks.Add(new PlayerTrack(
                id,
                type,
                label,
                language,
                ReadBoolProperty($"track-list/{index}/selected"),
                ReadBoolProperty($"track-list/{index}/default"),
                ReadBoolProperty($"track-list/{index}/external")));
        }

        lock (_stateLock)
        {
            _tracks = tracks;
            _selectedAudioTrackId = tracks.FirstOrDefault(track => track.Type == PlayerTrackType.Audio && track.IsSelected)?.Id ?? _selectedAudioTrackId;
            _selectedSubtitleTrackId = tracks.FirstOrDefault(track => track.Type == PlayerTrackType.Subtitle && track.IsSelected)?.Id ?? _selectedSubtitleTrackId;
        }

        EmitTrackList(DateTimeOffset.UtcNow);
    }

    private void EmitTrackList(DateTimeOffset observedAt)
    {
        IReadOnlyList<PlayerTrack> tracks;
        string? audio;
        string? subtitle;
        lock (_stateLock)
        {
            tracks = _tracks;
            audio = _selectedAudioTrackId;
            subtitle = _selectedSubtitleTrackId;
        }

        WriteEvent(new PlayerEvent.TrackListChanged(tracks, audio, subtitle, observedAt));
    }

    private static unsafe bool TryReadFlag(MpvEventProperty property, out bool value)
    {
        if (property.Format == MpvFormat.Flag && property.Data != IntPtr.Zero)
        {
            value = *(int*)property.Data != 0;
            return true;
        }

        value = false;
        return false;
    }

    private static unsafe bool TryReadDouble(MpvEventProperty property, out double value)
    {
        if (property.Format == MpvFormat.Double && property.Data != IntPtr.Zero)
        {
            value = *(double*)property.Data;
            return true;
        }

        value = 0;
        return false;
    }

    private static string? ReadStringProperty(MpvEventProperty property)
    {
        if (property.Format != MpvFormat.String || property.Data == IntPtr.Zero)
        {
            return null;
        }

        var stringPtr = Marshal.ReadIntPtr(property.Data);
        return stringPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(stringPtr);
    }

    private static string? NormalizeTrackId(string? value) =>
        value is null or "no" or "auto" or "" ? null : value;

    private string? ReadPropertyString(string name)
    {
        var ptr = LibMpvNative.mpv_get_property_string(_handle, name);
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            LibMpvNative.mpv_free(ptr);
        }
    }

    private int ReadIntProperty(string name) =>
        int.TryParse(ReadPropertyString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private bool ReadBoolProperty(string name) =>
        string.Equals(ReadPropertyString(name), "yes", StringComparison.OrdinalIgnoreCase);

    private void SetPropertyString(string name, string value)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        LibMpvNative.mpv_set_property_string(_handle, name, value);
    }

    private void Command(params string[] args)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var pointers = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                pointers[i] = Utf8ToHGlobal(args[i]);
            }

            pointers[args.Length] = IntPtr.Zero;

            var pinned = GCHandle.Alloc(pointers, GCHandleType.Pinned);
            try
            {
                LibMpvNative.mpv_command(_handle, pinned.AddrOfPinnedObject());
            }
            finally
            {
                pinned.Free();
            }
        }
        finally
        {
            foreach (var pointer in pointers)
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
        }
    }

    private static IntPtr Utf8ToHGlobal(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    private void WriteEvent(PlayerEvent playerEvent) => _events.Writer.TryWrite(playerEvent);

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LibMpvEngine));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _stopRequested = true;

        // Wake the event pump so it observes _stopRequested immediately instead of blocking in
        // mpv_wait_event for up to its poll interval.
        if (_handle != IntPtr.Zero)
        {
            LibMpvNative.mpv_wakeup(_handle);
        }

        var pump = _eventPump;
        var pumpStopped = true;
        if (pump is not null && pump.IsAlive)
        {
            pumpStopped = await Task.Run(() => pump.Join(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }

        // Free the render context before destroying the mpv handle, as required by the render API.
        _renderSession?.Dispose();
        _renderSession = null;

        // Only destroy the handle once the pump has actually exited; tearing it down while the pump is
        // still inside mpv_wait_event(_handle) would be a use-after-free. If the pump is wedged we leak
        // the handle rather than risk crashing the process.
        if (pumpStopped && _handle != IntPtr.Zero)
        {
            LibMpvNative.mpv_terminate_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _events.Writer.TryComplete();
    }
}
