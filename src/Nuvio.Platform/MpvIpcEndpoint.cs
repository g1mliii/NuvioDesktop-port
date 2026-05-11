using System.IO.Pipes;
using System.Net.Sockets;

namespace Nuvio.Platform;

public sealed class MpvIpcEndpoint : IAsyncDisposable
{
    private readonly string? _unixSocketPath;
    private readonly string? _pipeName;

    private MpvIpcEndpoint(string mpvArgument, string? unixSocketPath, string? pipeName)
    {
        MpvArgument = mpvArgument;
        _unixSocketPath = unixSocketPath;
        _pipeName = pipeName;
    }

    public string MpvArgument { get; }

    public static MpvIpcEndpoint Create()
    {
        var name = $"nuvio-mpv-{Guid.NewGuid():N}";
        if (OperatingSystem.IsWindows())
        {
            return new MpvIpcEndpoint(name, null, name);
        }

        var socketPath = Path.Combine(Path.GetTempPath(), $"{name}.sock");
        return new MpvIpcEndpoint(socketPath, socketPath, null);
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        if (_pipeName is not null)
        {
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            return pipe;
        }

        var endpoint = new UnixDomainSocketEndPoint(_unixSocketPath!);

        while (true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(endpoint, timeoutSource.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (!timeoutSource.IsCancellationRequested)
            {
                socket.Dispose();
                await Task.Delay(100, timeoutSource.Token).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_unixSocketPath is not null)
        {
            try
            {
                File.Delete(_unixSocketPath);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return ValueTask.CompletedTask;
    }
}
