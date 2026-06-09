using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;
using System.Windows.Input;

namespace Nuvio.Desktop.ViewModels;

public static class PosterGridBuilder
{
    public const int DefaultColumns = 5;

    public static IReadOnlyList<PosterGridRowViewModel> BuildRows(
        IReadOnlyList<CatalogItem> items,
        ICommand openCommand,
        int columns = DefaultColumns,
        IDesktopImageLoader? imageLoader = null)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Poster grid columns must be positive.");
        }

        var rows = new List<PosterGridRowViewModel>();
        foreach (var chunk in items.Chunk(columns))
        {
            rows.Add(new PosterGridRowViewModel(chunk.ToArray(), openCommand, imageLoader));
        }

        return rows;
    }

    public static IReadOnlyList<PosterCardViewModel> BuildCards(
        IReadOnlyList<CatalogItem> items,
        ICommand openCommand,
        IDesktopImageLoader? imageLoader = null)
    {
        return items.Select(item => CreateCard(item, openCommand, imageLoader)).ToArray();
    }

    public static IAsyncRelayCommand<CatalogItem> CreateOpenCommand(Func<CatalogItem, Task> openDetailsAsync)
    {
        return new AsyncRelayCommand<CatalogItem>(item =>
            item is null ? Task.CompletedTask : openDetailsAsync(item));
    }

    private static PosterCardViewModel CreateCard(
        CatalogItem item,
        ICommand openCommand,
        IDesktopImageLoader? imageLoader)
    {
        return new PosterCardViewModel(item, openCommand, imageLoader);
    }
}
