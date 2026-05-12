using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class SearchPageViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(150);
    private readonly IDesktopFixtureService _fixtures;
    private readonly IAsyncRelayCommand<CatalogItem> _openDetailsCommand;
    private CancellationTokenSource? _searchCancellation;
    private string _searchText = string.Empty;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private int _resultCount;
    private int _searchGeneration;
    private int _isDisposed;

    public SearchPageViewModel(IDesktopFixtureService fixtures, Func<CatalogItem, Task> openDetailsAsync)
    {
        _fixtures = fixtures;
        _openDetailsCommand = PosterGridBuilder.CreateOpenCommand(openDetailsAsync);
    }

    public ObservableCollection<PosterGridRowViewModel> Rows { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _ = SearchAsync(value, SearchDebounce);
            }
        }
    }

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

    public int ResultCount
    {
        get => _resultCount;
        private set => SetProperty(ref _resultCount, value);
    }

    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEmpty => HasQuery && !IsLoading && ResultCount == 0 && !HasError;

    public Task SearchAsync(string query) => SearchAsync(query, TimeSpan.Zero);

    private async Task SearchAsync(string query, TimeSpan debounce)
    {
        if (Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var generation = ++_searchGeneration;

        OnPropertyChanged(nameof(HasQuery));
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));

        if (string.IsNullOrWhiteSpace(query))
        {
            Rows.Clear();
            ResultCount = 0;
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            if (debounce > TimeSpan.Zero)
            {
                await Task.Delay(debounce, cancellationToken);
            }

            var results = await _fixtures.SearchAsync(query, cancellationToken);
            if (generation != _searchGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Rows.Clear();
            foreach (var row in PosterGridBuilder.BuildRows(results, _openDetailsCommand))
            {
                Rows.Add(row);
            }

            ResultCount = results.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (generation == _searchGeneration)
            {
                ErrorMessage = ex.Message;
                Rows.Clear();
                ResultCount = 0;
            }
        }
        finally
        {
            if (generation == _searchGeneration)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }
}
