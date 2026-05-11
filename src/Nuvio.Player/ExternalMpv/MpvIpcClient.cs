using System.Collections.Concurrent;
using System.Text.Json;

namespace Nuvio.Player.ExternalMpv;

public sealed class MpvIpcClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<MpvCommandResponse>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readTask;
    private long _requestId;

    public MpvIpcClient(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
        _readTask = Task.Run(ReadLoopAsync);
    }

    public event Action<JsonElement>? EventReceived;

    public event Action<Exception>? ErrorReceived;

    public async Task<MpvCommandResponse> SendAsync(IReadOnlyList<object?> command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var responseSource = new TaskCompletionSource<MpvCommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = responseSource;

        try
        {
            var line = MpvCommandSerializer.Serialize(command, requestId);
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            var response = await responseSource.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new InvalidOperationException($"mpv command failed: {response.Error}");
            }

            return response;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement.Clone();

                if (root.TryGetProperty("request_id", out var requestIdElement))
                {
                    var requestId = requestIdElement.GetInt64();
                    if (_pending.TryRemove(requestId, out var responseSource))
                    {
                        responseSource.TrySetResult(MpvCommandSerializer.ParseResponse(root));
                    }

                    continue;
                }

                EventReceived?.Invoke(root);
            }

            if (!_lifetime.IsCancellationRequested)
            {
                FailPending(new IOException("mpv IPC closed before pending commands completed."));
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke(ex);
            FailPending(ex);
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var pending in _pending)
        {
            if (_pending.TryRemove(pending.Key, out var responseSource))
            {
                responseSource.TrySetException(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _writer.Dispose();
        _reader.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);

        try
        {
            await _readTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        _writeLock.Dispose();
        _lifetime.Dispose();
    }
}
