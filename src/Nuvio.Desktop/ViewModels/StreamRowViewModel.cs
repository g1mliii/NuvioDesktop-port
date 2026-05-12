using System.Windows.Input;
using Nuvio.Core.Models;

namespace Nuvio.Desktop.ViewModels;

public sealed class StreamRowViewModel
{
    public StreamRowViewModel(StreamSource source, string description, ICommand playCommand)
    {
        Source = source;
        Description = description;
        PlayCommand = playCommand;
    }

    public StreamSource Source { get; }

    public string Title => Source.Title ?? "Stream";

    public string QualityLabel => Source.QualityLabel ?? "Auto";

    public string Description { get; }

    public ICommand PlayCommand { get; }
}
