using System.Windows.Input;
using Nuvio.Core.Models;

namespace Nuvio.Desktop.ViewModels;

public sealed class PosterCardViewModel
{
    public PosterCardViewModel(CatalogItem item, ICommand openCommand)
    {
        Item = item;
        OpenCommand = openCommand;
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

    public ICommand OpenCommand { get; }
}
