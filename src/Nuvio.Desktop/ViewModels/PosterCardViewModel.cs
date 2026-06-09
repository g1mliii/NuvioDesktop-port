using System.Windows.Input;
using Avalonia.Media;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class PosterCardViewModel : ViewModelBase
{
    private readonly IDesktopImageLoader? _imageLoader;
    private CancellationTokenSource? _imageCancellation;
    private IImage? _posterImage;
    private bool _imageRequested;

    public PosterCardViewModel(
        CatalogItem item,
        ICommand openCommand,
        IDesktopImageLoader? imageLoader = null,
        double progress = 0d)
    {
        Item = item;
        OpenCommand = openCommand;
        _imageLoader = imageLoader;
        Progress = Math.Clamp(progress, 0d, 1d);
    }

    public CatalogItem Item { get; }

    public string Id => Item.Id;

    public string Type => Item.Type;

    public string Name => Item.Name;

    public string? ReleaseInfo => Item.ReleaseInfo;

    public string PosterUrl => Item.PosterUrl?.ToString() ?? string.Empty;

    public string BackdropUrl => Item.BackgroundUrl?.ToString() ?? string.Empty;

    public string PosterInitial => string.IsNullOrWhiteSpace(Name)
        ? "N"
        : Name.Trim()[0].ToString().ToUpperInvariant();

    /// <summary>Watch progress fraction (0..1). Non-zero only for Continue Watching cards; drives the
    /// progress bar overlay and is exposed via <see cref="ShowProgress"/>.</summary>
    public double Progress { get; }

    public bool ShowProgress => Progress > 0d;

    public IImage? PosterImage
    {
        get => _posterImage;
        private set
        {
            if (SetProperty(ref _posterImage, value))
            {
                OnPropertyChanged(nameof(ShowInitial));
            }
        }
    }

    /// <summary>True while there is no decoded poster image, so the template shows the letter initial.</summary>
    public bool ShowInitial => PosterImage is null;

    public ICommand OpenCommand { get; }

    /// <summary>Loads the poster art on demand (called when the card is realized in a virtualized row).
    /// Best-effort: on failure or with no loader/URL, leaves <see cref="PosterImage"/> null so the initial
    /// stays visible.</summary>
    public async Task EnsureImageAsync(int decodePixelWidth, CancellationToken cancellationToken)
    {
        if (_imageLoader is null || Item.PosterUrl is null || _imageRequested || PosterImage is not null)
        {
            return;
        }

        _imageRequested = true;
        _imageCancellation?.Cancel();
        _imageCancellation?.Dispose();
        _imageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _imageCancellation.Token;

        try
        {
            var image = await _imageLoader.LoadAsync(Item.PosterUrl, decodePixelWidth, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                PosterImage = image;
            }
        }
        catch (OperationCanceledException)
        {
            // Card recycled before the load finished; allow a later realize to retry.
            _imageRequested = false;
        }
        catch
        {
            // Leave PosterImage null so the initial placeholder shows.
        }
    }

    /// <summary>Cancels an in-flight poster load when the card is recycled out of view.</summary>
    public void CancelImageLoad()
    {
        _imageCancellation?.Cancel();
        _imageCancellation?.Dispose();
        _imageCancellation = null;
    }
}
