using System.Text;
using Nuvio.Core.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests.Net;

public sealed class BoundedHttpResponseReaderTests
{
    [Fact]
    public async Task ReadStringAsync_BelowLimit_ReturnsBody()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var body = await BoundedHttpResponseReader.ReadStringAsync(stream, maxBytes: 16, "manifest", CancellationToken.None);
        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task ReadStringAsync_OverLimit_Throws()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 64));
        using var stream = new MemoryStream(bytes);
        var error = await Assert.ThrowsAsync<NuvioValidationException>(() =>
            BoundedHttpResponseReader.ReadStringAsync(stream, maxBytes: 32, "manifest", CancellationToken.None));
        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
