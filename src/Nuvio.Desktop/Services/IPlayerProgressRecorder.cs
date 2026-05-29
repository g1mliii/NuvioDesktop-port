using Nuvio.Core.Models;
using Nuvio.Player;

namespace Nuvio.Desktop.Services;

public interface IPlayerProgressRecorder : IDisposable
{
    void Start(MediaDetails details, string? episodeId = null);

    Task RecordAsync(PlayerEvent playerEvent, CancellationToken cancellationToken);

    Task FlushAsync(bool isEnded, CancellationToken cancellationToken);

    void Reset();
}
