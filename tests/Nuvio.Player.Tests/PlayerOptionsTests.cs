using Nuvio.Player;

namespace Nuvio.Player.Tests;

public sealed class PlayerOptionsTests
{
    [Fact]
    public void ExternalMpvDefault_UsesExternalEngine()
    {
        Assert.Equal("external-mpv", PlayerOptions.ExternalMpvDefault.PreferredEngine);
        Assert.InRange(PlayerOptions.ExternalMpvDefault.InitialVolume, 0, 100);
        Assert.True(PlayerOptions.ExternalMpvDefault.HardwareDecodingEnabled);
        Assert.Equal("gpu-next", PlayerOptions.ExternalMpvDefault.MpvOptions["vo"]);
        Assert.Equal("auto-safe", PlayerOptions.ExternalMpvDefault.MpvOptions["hwdec"]);
        Assert.Equal("no", PlayerOptions.ExternalMpvDefault.MpvOptions["config"]);
    }

    [Fact]
    public void ExternalMpvQualityDefaults_ReturnCommandLineArguments()
    {
        var arguments = ExternalMpvQualityDefaults.ToCommandLineArguments();

        Assert.Contains("--vo=gpu-next", arguments);
        Assert.Contains("--hwdec=auto-safe", arguments);
        Assert.Contains("--save-position-on-quit=no", arguments);
    }
}
