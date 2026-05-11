using System.Text.Json;
using Nuvio.Player.ExternalMpv;

namespace Nuvio.Player.Tests;

public sealed class MpvEventMapperTests
{
    [Fact]
    public void Map_PauseProperty_EmitsPlaybackState()
    {
        var mapper = new MpvEventMapper();

        var events = mapper.Map(Parse("""{"event":"property-change","name":"pause","data":false}"""));

        var state = Assert.IsType<PlayerEvent.PlaybackStateChanged>(Assert.Single(events));
        Assert.True(state.IsPlaying);
    }

    [Fact]
    public void Map_PositionAndDuration_EmitsPositionWithDuration()
    {
        var mapper = new MpvEventMapper();

        mapper.Map(Parse("""{"event":"property-change","name":"duration","data":120}"""));
        var events = mapper.Map(Parse("""{"event":"property-change","name":"time-pos","data":10.5}"""));

        var position = Assert.IsType<PlayerEvent.PlaybackPositionChanged>(Assert.Single(events));
        Assert.Equal(TimeSpan.FromSeconds(10.5), position.Position);
        Assert.Equal(TimeSpan.FromSeconds(120), position.Duration);
    }

    [Fact]
    public void Map_FileLoadedAndEndFile_EmitLifecycleEvents()
    {
        var mapper = new MpvEventMapper();

        Assert.IsType<PlayerEvent.FileLoaded>(Assert.Single(mapper.Map(Parse("""{"event":"file-loaded"}"""))));

        var ended = Assert.IsType<PlayerEvent.PlaybackEnded>(Assert.Single(mapper.Map(Parse("""{"event":"end-file","reason":"eof"}"""))));
        Assert.Equal("eof", ended.Reason);
    }

    [Fact]
    public void Map_TrackList_EmitsTrackMetadata()
    {
        var mapper = new MpvEventMapper();
        var events = mapper.Map(Parse("""
            {
              "event": "property-change",
              "name": "track-list",
              "data": [
                { "id": 1, "type": "audio", "title": "English", "lang": "en", "selected": true, "default": true },
                { "id": 2, "type": "sub", "title": "English CC", "lang": "en", "external": true }
              ]
            }
            """));

        var tracks = Assert.IsType<PlayerEvent.TrackListChanged>(Assert.Single(events));
        Assert.Equal("1", tracks.SelectedAudioTrackId);
        Assert.Equal(2, tracks.Tracks.Count);
        Assert.True(tracks.Tracks[1].IsExternal);
    }

    [Fact]
    public void Map_NoSubtitleSelector_ClearsSelectedSubtitleTrack()
    {
        var mapper = new MpvEventMapper();

        var events = mapper.Map(Parse("""{"event":"property-change","name":"sid","data":"no"}"""));

        var tracks = Assert.IsType<PlayerEvent.TrackListChanged>(Assert.Single(events));
        Assert.Null(tracks.SelectedSubtitleTrackId);
    }

    [Theory]
    [InlineData("""{"underrun":true,"eof":false}""", true)]
    [InlineData("""{"underrun":false,"eof":false}""", false)]
    [InlineData("""{"eof":true}""", false)]
    public void Map_CacheState_UsesUnderrunAsBufferingSignal(string cacheState, bool expectedBuffering)
    {
        var mapper = new MpvEventMapper();

        var events = mapper.Map(Parse($$"""{"event":"property-change","name":"demuxer-cache-state","data":{{cacheState}}}"""));

        var buffering = Assert.IsType<PlayerEvent.BufferingStateChanged>(Assert.Single(events));
        Assert.Equal(expectedBuffering, buffering.IsBuffering);
    }

    [Fact]
    public void Map_LogMessage_RedactsSensitiveValues()
    {
        var mapper = new MpvEventMapper();
        var events = mapper.Map(Parse("""{"event":"log-message","level":"error","text":"failed https://example.test/video.m3u8?token=secret"}"""));

        var error = Assert.IsType<PlayerEvent.PlayerError>(Assert.Single(events));
        Assert.Contains("?<redacted>", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
