namespace Nuvio.Desktop.ViewModels;

public sealed class HomeSectionViewModel
{
    public HomeSectionViewModel(string title, IReadOnlyList<PosterCardViewModel> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }

    public IReadOnlyList<PosterCardViewModel> Items { get; }
}
