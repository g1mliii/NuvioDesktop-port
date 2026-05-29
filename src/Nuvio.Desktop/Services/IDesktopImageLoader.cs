using Avalonia.Media;

namespace Nuvio.Desktop.Services;

public interface IDesktopImageLoader
{
    Task<IImage?> LoadAsync(Uri sourceUrl, CancellationToken cancellationToken);
}
