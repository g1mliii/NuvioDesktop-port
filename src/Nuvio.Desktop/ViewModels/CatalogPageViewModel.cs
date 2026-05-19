using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class CatalogPageViewModel : ViewModelBase, IDisposable
{
    private readonly ICatalogDataSource _dataSource;
    private readonly IAsyncRelayCommand<CatalogItem> _openDetailsCommand;
    private readonly List<CatalogItem> _items = [];
    private readonly CancellationTokenSource _disposeCancellation = new();
    private CancellationTokenSource? _loadMoreCancellation;
    private bool _isLoading;
    private bool _isLoadingMore;
    private string _errorMessage = string.Empty;
    private int _nextSkip;
    private bool _hasMore;
    private int _isDisposed;

    public CatalogPageViewModel(ICatalogDataSource dataSource, Func<CatalogItem, Task> openDetailsAsync)
    {
        _dataSource = dataSource;
        _openDetailsCommand = PosterGridBuilder.CreateOpenCommand(openDetailsAsync);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => HasMore && !IsLoading && !IsLoadingMore);
    }

    public ObservableCollection<PosterGridRowViewModel> Rows { get; } = [];

    public IAsyncRelayCommand LoadMoreCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                LoadMoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        private set
        {
            if (SetProperty(ref _isLoadingMore, value))
            {
                LoadMoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public int ItemCount => _items.Count;

    public bool HasMore
    {
        get => _hasMore;
        private set
        {
            if (SetProperty(ref _hasMore, value))
            {
                LoadMoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEmpty => !IsLoading && ItemCount == 0 && !HasError;

    public bool IsLoaded => _items.Count > 0 && !HasError;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        _items.Clear();
        Rows.Clear();
        _nextSkip = 0;
        HasMore = false;
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var page = await _dataSource.GetCatalogPageAsync(0, cancellationToken);
            _items.AddRange(page.Items);
            _nextSkip = page.NextSkip;
            HasMore = page.HasMore;
            foreach (var row in PosterGridBuilder.BuildRows(_items, _openDetailsCommand))
            {
                Rows.Add(row);
            }
            OnPropertyChanged(nameof(ItemCount));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading || IsLoadingMore || Volatile.Read(ref _isDisposed) == 1)
        {
            return;
        }

        _loadMoreCancellation?.Cancel();
        _loadMoreCancellation?.Dispose();
        _loadMoreCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposeCancellation.Token);
        var cancellationToken = _loadMoreCancellation.Token;

        IsLoadingMore = true;
        try
        {
            var page = await _dataSource.GetCatalogPageAsync(_nextSkip, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _nextSkip = page.NextSkip;
            HasMore = page.HasMore;

            if (page.Items.Count == 0)
            {
                return;
            }

            var previousItemCount = _items.Count;
            _items.AddRange(page.Items);
            AppendRows(previousItemCount);
            OnPropertyChanged(nameof(ItemCount));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            _loadMoreCancellation?.Dispose();
            _loadMoreCancellation = null;
            if (Volatile.Read(ref _isDisposed) == 0)
            {
                IsLoadingMore = false;
            }
        }
    }

    public void CancelPendingRequests()
    {
        _loadMoreCancellation?.Cancel();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _disposeCancellation.Cancel();
        _loadMoreCancellation?.Cancel();
        _disposeCancellation.Dispose();
    }

    private void AppendRows(int previousItemCount)
    {
        const int columns = PosterGridBuilder.DefaultColumns;
        var trailingPartial = previousItemCount % columns;
        if (trailingPartial > 0 && Rows.Count > 0)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        var rebuildStart = previousItemCount - trailingPartial;
        var appendItems = _items.GetRange(rebuildStart, _items.Count - rebuildStart);
        foreach (var row in PosterGridBuilder.BuildRows(appendItems, _openDetailsCommand, columns))
        {
            Rows.Add(row);
        }
    }
}
