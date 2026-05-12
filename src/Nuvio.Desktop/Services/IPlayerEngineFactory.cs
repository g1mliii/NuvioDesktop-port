using Nuvio.Player;
using Nuvio.Player.ExternalMpv;

namespace Nuvio.Desktop.Services;

public interface IPlayerEngineFactory
{
    IPlayerEngine Create();
}

public sealed class ExternalMpvPlayerEngineFactory : IPlayerEngineFactory
{
    public IPlayerEngine Create() => new ExternalMpvEngine();
}
