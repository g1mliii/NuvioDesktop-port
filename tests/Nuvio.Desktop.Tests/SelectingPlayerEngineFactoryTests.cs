using Nuvio.Desktop.Services;
using Nuvio.Player;
using Nuvio.Player.ExternalMpv;
using Nuvio.Player.LibMpv;

namespace Nuvio.Desktop.Tests;

public sealed class SelectingPlayerEngineFactoryTests
{
    [Fact]
    public void Create_ReturnsLibMpvEngine_WhenRequestedAndAvailable()
    {
        var factory = new SelectingPlayerEngineFactory(() => true);

        var engine = factory.Create(PlayerOptions.ExternalMpvDefault with { PreferredEngine = SelectingPlayerEngineFactory.LibMpvEngineId });

        Assert.IsType<LibMpvEngine>(engine);
    }

    [Fact]
    public void Create_FallsBackToExternalMpv_WhenLibMpvUnavailable()
    {
        var factory = new SelectingPlayerEngineFactory(() => false);

        var engine = factory.Create(PlayerOptions.ExternalMpvDefault with { PreferredEngine = SelectingPlayerEngineFactory.LibMpvEngineId });

        Assert.IsType<ExternalMpvEngine>(engine);
    }

    [Fact]
    public void Create_ReturnsExternalMpv_WhenExternalRequested()
    {
        // The libmpv probe must not even be consulted when external mpv is the preferred engine.
        var probed = false;
        var factory = new SelectingPlayerEngineFactory(() =>
        {
            probed = true;
            return true;
        });

        var engine = factory.Create(PlayerOptions.ExternalMpvDefault with { PreferredEngine = "external-mpv" });

        Assert.IsType<ExternalMpvEngine>(engine);
        Assert.False(probed);
    }
}
