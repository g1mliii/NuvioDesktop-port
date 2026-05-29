using Nuvio.Core.Models;
using Nuvio.Core.Progress;
using Nuvio.Player;

namespace Nuvio.Desktop.Services;

public sealed class PlayerProgressRecorder : IPlayerProgressRecorder
{
    public static TimeSpan DefaultDebounceInterval { get; } = TimeSpan.FromSeconds(5);

    private readonly IWatchProgressRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _debounceInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _mediaId;
    private string? _episodeId;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _hasPosition;
    private bool _dirty;
    private int _disposeStarted;
    private bool _disposed;
    private DateTimeOffset _lastWriteAt = DateTimeOffset.MinValue;

    public PlayerProgressRecorder(
        IWatchProgressRepository repository,
        TimeProvider? timeProvider = null,
        TimeSpan? debounceInterval = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _debounceInterval = debounceInterval ?? DefaultDebounceInterval;
    }

    public void Start(MediaDetails details, string? episodeId = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            ResetLocked();
            _mediaId = details.Id;
            _episodeId = episodeId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAsync(PlayerEvent playerEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            switch (playerEvent)
            {
                case PlayerEvent.PlaybackPositionChanged position:
                    _position = position.Position;
                    _duration = position.Duration ?? TimeSpan.Zero;
                    _hasPosition = true;
                    _dirty = true;
                    if (_timeProvider.GetUtcNow() - _lastWriteAt >= _debounceInterval)
                    {
                        await FlushLockedAsync(isEnded: false, cancellationToken).ConfigureAwait(false);
                    }

                    break;
                case PlayerEvent.PlaybackStateChanged { IsPlaying: false }:
                case PlayerEvent.PlayerError:
                    await FlushLockedAsync(isEnded: false, cancellationToken).ConfigureAwait(false);
                    break;
                case PlayerEvent.PlaybackEnded:
                    await FlushLockedAsync(isEnded: true, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(bool isEnded, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await FlushLockedAsync(isEnded, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Reset()
    {
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            ResetLocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ResetLocked()
    {
        _mediaId = null;
        _episodeId = null;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _hasPosition = false;
        _dirty = false;
        _lastWriteAt = DateTimeOffset.MinValue;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
        {
            return;
        }

        _gate.Wait();
        try
        {
            ResetLocked();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task FlushLockedAsync(bool isEnded, CancellationToken cancellationToken)
    {
        if (!_hasPosition || string.IsNullOrWhiteSpace(_mediaId) || (!_dirty && !isEnded))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var percent = _duration > TimeSpan.Zero
            ? Math.Clamp(_position.TotalMilliseconds / _duration.TotalMilliseconds * 100d, 0d, 100d)
            : 0d;
        var progress = WatchProgressRules.Normalize(
            new WatchProgress(_mediaId, _episodeId, _position, _duration, percent, now),
            isEnded);

        if (WatchProgressRules.ShouldStoreProgress(progress.Position, progress.Duration))
        {
            await _repository.UpsertAsync(progress, cancellationToken).ConfigureAwait(false);
            _lastWriteAt = now;
        }

        _dirty = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PlayerProgressRecorder));
        }
    }
}
