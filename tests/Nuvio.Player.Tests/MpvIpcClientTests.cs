using Nuvio.Player.ExternalMpv;

namespace Nuvio.Player.Tests;

public sealed class MpvIpcClientTests
{
    [Fact]
    public async Task DisposeAsync_IgnoresBrokenPipeDuringWriterFlush()
    {
        var stream = new BrokenFlushStream();
        await using var client = new MpvIpcClient(stream);

        stream.BreakFlush = true;
        await client.DisposeAsync();
    }

    private sealed class BrokenFlushStream : Stream
    {
        public bool BreakFlush { get; set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (BreakFlush)
            {
                throw new IOException("Pipe is broken.");
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
