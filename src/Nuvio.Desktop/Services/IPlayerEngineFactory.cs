using Nuvio.Player;
using Nuvio.Player.ExternalMpv;
using Nuvio.Player.LibMpv;

namespace Nuvio.Desktop.Services;

public interface IPlayerEngineFactory
{
    IPlayerEngine Create(PlayerOptions options);
}

/// <summary>Always returns the external mpv engine. Used by fixtures and tests.</summary>
public sealed class ExternalMpvPlayerEngineFactory : IPlayerEngineFactory
{
    public IPlayerEngine Create(PlayerOptions options) => new ExternalMpvEngine();
}

/// <summary>
/// Selects the engine from <see cref="PlayerOptions.PreferredEngine"/>. When embedded libmpv is requested
/// but the native library cannot be loaded, it falls back to external mpv so embedded-playback failures
/// never strand the user (Phase 6, task 6.12). The view-model detects the fallback by the returned engine
/// type and surfaces a diagnostic.
/// </summary>
public sealed class SelectingPlayerEngineFactory : IPlayerEngineFactory
{
    public const string LibMpvEngineId = "libmpv";

    private readonly Func<bool> _isLibMpvAvailable;

    public SelectingPlayerEngineFactory()
        : this(() => LibMpvLibraryLoader.EnsureLoaded().IsAvailable)
    {
    }

    /// <summary>Test/diagnostic overload: supply the libmpv availability probe explicitly.</summary>
    public SelectingPlayerEngineFactory(Func<bool> isLibMpvAvailable)
    {
        _isLibMpvAvailable = isLibMpvAvailable;
    }

    public IPlayerEngine Create(PlayerOptions options)
    {
        if (string.Equals(options.PreferredEngine, LibMpvEngineId, StringComparison.Ordinal)
            && _isLibMpvAvailable())
        {
            return new LibMpvEngine();
        }

        return new ExternalMpvEngine();
    }
}
