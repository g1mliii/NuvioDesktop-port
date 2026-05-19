using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class DetailsPageViewModel : ViewModelBase
{
    private readonly ICatalogDataSource _dataSource;
    private readonly Func<StreamSource, MediaDetails, Task> _playAsync;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _providerErrorSummary = string.Empty;
    private MediaDetails? _details;

    public DetailsPageViewModel(ICatalogDataSource dataSource, Func<StreamSource, MediaDetails, Task> playAsync)
    {
        _dataSource = dataSource;
        _playAsync = playAsync;
    }

    public ObservableCollection<StreamRowViewModel> Streams { get; } = [];

    public MediaDetails? Details
    {
        get => _details;
        private set
        {
            if (SetProperty(ref _details, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(ReleaseLine));
                OnPropertyChanged(nameof(GenreLine));
                OnPropertyChanged(nameof(PosterInitial));
                OnPropertyChanged(nameof(PosterUrl));
                OnPropertyChanged(nameof(BackdropUrl));
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public string Title => Details?.Name ?? "Details";

    public string Description => Details?.Description ?? string.Empty;

    public string ReleaseLine => string.Join("  |  ", new[] { Details?.ReleaseInfo, Details?.Runtime }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string GenreLine => Details is null ? string.Empty : string.Join(", ", Details.Genres);

    public string PosterInitial => string.IsNullOrWhiteSpace(Title) ? "N" : Title.Trim()[0].ToString().ToUpperInvariant();

    public string PosterUrl => Details?.PosterUrl?.ToString() ?? string.Empty;

    public string BackdropUrl => Details?.BackgroundUrl?.ToString() ?? string.Empty;

    public bool HasDetails => Details is not null;

    public bool HasStreams => Streams.Count > 0;

    public bool HasNoStreams => HasDetails && !IsLoading && !HasStreams && !HasError;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string ProviderErrorSummary
    {
        get => _providerErrorSummary;
        private set
        {
            if (SetProperty(ref _providerErrorSummary, value))
            {
                OnPropertyChanged(nameof(HasProviderErrors));
            }
        }
    }

    public bool HasProviderErrors => !string.IsNullOrWhiteSpace(ProviderErrorSummary);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEmpty => !IsLoading && !HasDetails && !HasError;

    public async Task LoadAsync(string mediaId, string? mediaType, CancellationToken cancellationToken)
    {
        IsLoading = true;
        Details = null;
        Streams.Clear();
        ErrorMessage = string.Empty;
        ProviderErrorSummary = string.Empty;
        NotifyStreamState();
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var state = await _dataSource.GetDetailsAsync(mediaId, mediaType, cancellationToken);
            Details = state.Details;
            Streams.Clear();
            ProviderErrorSummary = state.FailedProviders is { Count: > 0 }
                ? $"{state.FailedProviders.Count} provider(s) returned no streams: {string.Join(", ", state.FailedProviders)}"
                : string.Empty;

            var streamIndex = 0;
            foreach (var stream in state.Streams.Where(stream => stream.HasPlayableSource))
            {
                var id = $"{state.Details.Id}:stream:{++streamIndex}";
                var source = StreamSourceMapper.ToStreamSource(stream, id);
                var description = stream.StreamSubtitle ?? stream.BehaviorHints.Filename ?? source.Url.Host;
                Streams.Add(new StreamRowViewModel(
                    source,
                    description,
                    new AsyncRelayCommand(() => _playAsync(source, state.Details)),
                    providerName: stream.ProviderName));
            }

            NotifyStreamState();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Details = null;
            Streams.Clear();
            ProviderErrorSummary = string.Empty;
            NotifyStreamState();
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
            NotifyStreamState();
        }
    }

    private void NotifyStreamState()
    {
        OnPropertyChanged(nameof(HasStreams));
        OnPropertyChanged(nameof(HasNoStreams));
    }
}
