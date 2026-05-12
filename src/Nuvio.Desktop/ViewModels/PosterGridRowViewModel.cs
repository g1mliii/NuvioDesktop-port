using System.Windows.Input;
using Nuvio.Core.Models;

namespace Nuvio.Desktop.ViewModels;

public sealed class PosterGridRowViewModel
{
    private readonly IReadOnlyList<CatalogItem> _items;
    private readonly ICommand _openCommand;
    private IReadOnlyList<PosterCardViewModel>? _cardViewModels;

    public PosterGridRowViewModel(
        IReadOnlyList<CatalogItem> items,
        ICommand openCommand)
    {
        _items = items;
        _openCommand = openCommand;
    }

    public IReadOnlyList<PosterCardViewModel> Items =>
        _cardViewModels ??= _items.Select(item => new PosterCardViewModel(item, _openCommand)).ToArray();
}
