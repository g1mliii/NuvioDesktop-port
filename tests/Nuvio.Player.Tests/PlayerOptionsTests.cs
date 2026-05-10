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
    }
}
