using Nuvio.Core.Models;

namespace Nuvio.Player;

public interface IPlayerEngine : IAsyncDisposable
{
    Task InitializeAsync(PlayerOptions options, CancellationToken cancellationToken);
    Task LoadAsync(StreamSource source, CancellationToken cancellationToken);
    Task PlayAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken);
    Task SetVolumeAsync(int volume, CancellationToken cancellationToken);
    Task SelectAudioTrackAsync(string trackId, CancellationToken cancellationToken);
    Task SelectSubtitleTrackAsync(string? trackId, CancellationToken cancellationToken);
    IAsyncEnumerable<PlayerEvent> Events(CancellationToken cancellationToken);
}
