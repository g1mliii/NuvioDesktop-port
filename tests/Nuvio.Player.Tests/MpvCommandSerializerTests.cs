using System.Text.Json;
using Nuvio.Core.Models;
using Nuvio.Player.ExternalMpv;

namespace Nuvio.Player.Tests;

public sealed class MpvCommandSerializerTests
{
    [Fact]
    public void Serialize_LoadFileCommand_UsesExpectedJsonShape()
    {
        var source = new StreamSource(
            "fixture",
            new Uri("https://example.test/video.mkv?token=secret"),
            "Fixture",
            "1080p",
            new Dictionary<string, string>
            {
                ["User-Agent"] = "NuvioTest"
            },
            [],
            IsUserProvided: false);

        var json = MpvCommandSerializer.Serialize(MpvCommands.LoadFile(source), 42);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(42, root.GetProperty("request_id").GetInt32());
        Assert.Equal("loadfile", root.GetProperty("command")[0].GetString());
        Assert.Equal(source.Url.AbsoluteUri, root.GetProperty("command")[1].GetString());
        Assert.Equal("replace", root.GetProperty("command")[2].GetString());
        Assert.Equal("User-Agent: NuvioTest", root.GetProperty("command")[3].GetProperty("http-header-fields")[0].GetString());
    }

    [Fact]
    public void SerializeLine_AppendsNewlineForMpvIpc()
    {
        var line = MpvCommandSerializer.SerializeLine(MpvCommands.Pause(), 1);

        Assert.EndsWith("\n", line);
    }

    [Theory]
    [InlineData(10, """{"command":["seek",10,"absolute"],"request_id":7}""")]
    public void Serialize_SeekCommand_IsStable(int seconds, string expected)
    {
        var json = MpvCommandSerializer.Serialize(MpvCommands.Seek(TimeSpan.FromSeconds(seconds)), 7);

        Assert.Equal(expected, json);
    }

    [Fact]
    public void ParseResponse_ReadsSuccessAndData()
    {
        using var document = JsonDocument.Parse("""{"request_id":3,"error":"success","data":"ok"}""");

        var response = MpvCommandSerializer.ParseResponse(document.RootElement);

        Assert.True(response.IsSuccess);
        Assert.Equal(3, response.RequestId);
        Assert.Equal("ok", response.Data!.Value.GetString());
    }
}
