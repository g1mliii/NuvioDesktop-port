using Nuvio.Player.ExternalMpv;

namespace Nuvio.Player.Tests;

public sealed class MpvCommandLogRedactorTests
{
    [Fact]
    public void Redact_RemovesSensitivePlaybackUrlValues()
    {
        var redacted = MpvCommandLogRedactor.Redact("""{"command":["loadfile","https://example.test/video.m3u8?token=secret"]}""");

        Assert.Contains("?<redacted>", redacted);
        Assert.DoesNotContain("secret", redacted);
    }

    [Fact]
    public void Redact_RemovesSensitiveHeaderValues()
    {
        var redacted = MpvCommandLogRedactor.Redact("Authorization: Bearer secret");

        Assert.Equal("Authorization: <redacted>", redacted);
    }
}
