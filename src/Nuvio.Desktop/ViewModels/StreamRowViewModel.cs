using System.Windows.Input;
using Nuvio.Core.Models;

namespace Nuvio.Desktop.ViewModels;

public sealed class StreamRowViewModel
{
    public StreamRowViewModel(StreamSource source, string description, ICommand playCommand, string? providerName = null)
    {
        Source = source;
        Description = description;
        PlayCommand = playCommand;
        ProviderName = providerName ?? string.Empty;
    }

    public StreamSource Source { get; }

    public string Title => Source.Title ?? "Stream";

    public string QualityLabel => Source.QualityLabel ?? "Auto";

    public string Description { get; }

    public string ProviderName { get; }

    public bool HasProvider => !string.IsNullOrWhiteSpace(ProviderName);

    public ICommand PlayCommand { get; }
}
