using Nuvio.Core.Security;

namespace Nuvio.Core.Tests;

public sealed class LogRedactorTests
{
    [Fact]
    public void RedactUrl_RemovesQueryString()
    {
        var redacted = LogRedactor.RedactUrl("https://example.test/video.m3u8?token=secret");

        Assert.Equal("https://example.test/video.m3u8?<redacted>", redacted);
    }

    [Fact]
    public void RedactUrl_RedactsSensitiveHeaderValues()
    {
        var redacted = LogRedactor.RedactUrl("Authorization: Bearer secret");

        Assert.Equal("Authorization: <redacted>", redacted);
    }
}
