namespace Nuvio.Desktop.ViewModels;

public sealed class PlaceholderPageViewModel : ViewModelBase
{
    public PlaceholderPageViewModel(string title, string summary)
    {
        Title = title;
        Summary = summary;
    }

    public string Title { get; }

    public string Summary { get; }
}
