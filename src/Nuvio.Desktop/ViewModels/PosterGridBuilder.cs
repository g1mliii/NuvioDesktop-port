using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using System.Windows.Input;

namespace Nuvio.Desktop.ViewModels;

public static class PosterGridBuilder
{
    public const int DefaultColumns = 5;

    public static IReadOnlyList<PosterGridRowViewModel> BuildRows(
        IReadOnlyList<CatalogItem> items,
        ICommand openCommand,
        int columns = DefaultColumns)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Poster grid columns must be positive.");
        }

        var rows = new List<PosterGridRowViewModel>();
        foreach (var chunk in items.Chunk(columns))
        {
            rows.Add(new PosterGridRowViewModel(chunk.ToArray(), openCommand));
        }

        return rows;
    }

    public static IReadOnlyList<PosterCardViewModel> BuildCards(
        IReadOnlyList<CatalogItem> items,
        ICommand openCommand)
    {
        return items.Select(item => CreateCard(item, openCommand)).ToArray();
    }

    public static IAsyncRelayCommand<CatalogItem> CreateOpenCommand(Func<CatalogItem, Task> openDetailsAsync)
    {
        return new AsyncRelayCommand<CatalogItem>(item =>
            item is null ? Task.CompletedTask : openDetailsAsync(item));
    }

    private static PosterCardViewModel CreateCard(CatalogItem item, ICommand openCommand)
    {
        return new PosterCardViewModel(item, openCommand);
    }
}
