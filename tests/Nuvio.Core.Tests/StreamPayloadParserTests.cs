using Nuvio.Core.Addons;
using Nuvio.Core.Security;
using Nuvio.Core.Streams;
using Nuvio.Core.Models;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests;

public sealed class StreamPayloadParserTests
{
    [Fact]
    public void Parse_StreamFixture_MapsSourcesHeadersAndSubtitles()
    {
        var streams = StreamPayloadParser.Parse(
            TestData.ReadFixture("streams-valid.json"),
            providerName: "Fixture Addon",
            providerAddonId: "addon:fixture");

        Assert.Equal(2, streams.Count);
        var stream = streams[0];
        Assert.Equal("Fixture 1080p", stream.Name);
        Assert.Equal("1080p", stream.QualityLabel);
        Assert.Equal("fixture-group", stream.BehaviorHints.BingeGroup);
        Assert.True(stream.BehaviorHints.NotWebReady);
        Assert.Equal(2147483648, stream.BehaviorHints.VideoSize);
        Assert.Equal("Bearer secret", stream.BehaviorHints.ProxyHeaders?.Request?["Authorization"]);

        var subtitle = Assert.Single(stream.Subtitles);
        Assert.Equal("sub-en", subtitle.Id);
        Assert.Equal("en", subtitle.Language);
        Assert.True(subtitle.IsDefault);
        Assert.Equal("http://cdn.example.test/external.mp4", streams[1].ExternalUrl?.ToString());
    }

    [Fact]
    public void ToStreamSource_PreservesPlaybackHeaders_AndRedactorMasksSensitiveValues()
    {
        var stream = StreamPayloadParser.Parse(
            TestData.ReadFixture("streams-valid.json"),
            providerName: "Fixture Addon",
            providerAddonId: "addon:fixture")[0];

        StreamSource source = StreamSourceMapper.ToStreamSource(stream, "stream-1");
        Assert.Equal("Bearer secret", source.Headers["Authorization"]);
        Assert.Equal("NuvioFixture", source.Headers["User-Agent"]);

        var redactedHeaders = LogRedactor.RedactHeaders(source.Headers);
        Assert.Equal("<redacted>", redactedHeaders["Authorization"]);
        Assert.Equal("NuvioFixture", redactedHeaders["User-Agent"]);
        Assert.Equal("https://cdn.example.test/video.m3u8?<redacted>", LogRedactor.RedactUrl(source.Url.ToString()));
    }

    [Fact]
    public void Parse_DuplicateProxyHeaderNames_DoesNotThrow()
    {
        const string payload = """
        {
          "streams": [
            {
              "name": "Duplicate headers",
              "url": "https://cdn.example.test/video.m3u8",
              "behaviorHints": {
                "proxyHeaders": {
                  "request": {
                    "Authorization": "Bearer first",
                    "authorization": "Bearer second"
                  }
                }
              }
            }
          ]
        }
        """;

        var stream = Assert.Single(StreamPayloadParser.Parse(payload, "Fixture Addon", "addon:fixture"));

        Assert.Equal("Bearer second", stream.BehaviorHints.ProxyHeaders?.Request?["Authorization"]);
    }

    [Fact]
    public void Parse_InfoHashOnlyStream_IsNotDirectlyPlayable()
    {
        const string payload = """
        {
          "streams": [
            {
              "name": "Torrent only",
              "infoHash": "0123456789abcdef0123456789abcdef01234567"
            }
          ]
        }
        """;

        var stream = Assert.Single(StreamPayloadParser.Parse(payload, "Fixture Addon", "addon:fixture"));

        Assert.True(stream.HasTorrentSource);
        Assert.False(stream.HasDirectPlaybackSource);
        Assert.False(stream.HasPlayableSource);
        Assert.Throws<ArgumentException>(() => StreamSourceMapper.ToStreamSource(stream, "stream-1"));
    }

    [Fact]
    public void Parse_OversizedPayload_FailsBeforeJsonParsing()
    {
        var payload = new string('x', (int)AddonFetchPolicy.Default.MaxStreamBytes + 1);

        var error = Assert.Throws<NuvioValidationException>(() =>
            StreamPayloadParser.Parse(payload, "Fixture Addon", "addon:fixture"));

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
