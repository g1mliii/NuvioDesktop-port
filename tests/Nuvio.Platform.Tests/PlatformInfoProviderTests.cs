using Nuvio.Platform;

namespace Nuvio.Platform.Tests;

public sealed class PlatformInfoProviderTests
{
    [Fact]
    public void Current_ReturnsSupportedDesktopPlatformOnBuildAgents()
    {
        var info = PlatformInfoProvider.Current();

        Assert.True(info.IsSupportedDesktop, $"Unexpected platform: {info}");
        Assert.Contains('-', info.RuntimeIdentifier);
        Assert.False(string.IsNullOrWhiteSpace(info.Description));
    }
}
