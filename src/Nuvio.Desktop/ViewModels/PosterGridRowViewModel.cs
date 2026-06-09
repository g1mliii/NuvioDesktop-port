using System.Windows.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class PosterGridRowViewModel
{
    private readonly IReadOnlyList<CatalogItem> _items;
    private readonly ICommand _openCommand;
    private readonly IDesktopImageLoader? _imageLoader;
    private IReadOnlyList<PosterCardViewModel>? _cardViewModels;

    public PosterGridRowViewModel(
        IReadOnlyList<CatalogItem> items,
        ICommand openCommand,
        IDesktopImageLoader? imageLoader = null)
    {
        _items = items;
        _openCommand = openCommand;
        _imageLoader = imageLoader;
    }

    public IReadOnlyList<PosterCardViewModel> Items =>
        _cardViewModels ??= _items.Select(item => new PosterCardViewModel(item, _openCommand, _imageLoader)).ToArray();
}
