using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class CatalogPageViewModel : ViewModelBase
{
    private readonly IDesktopFixtureService _fixtures;
    private readonly IAsyncRelayCommand<CatalogItem> _openDetailsCommand;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private int _itemCount;

    public CatalogPageViewModel(IDesktopFixtureService fixtures, Func<CatalogItem, Task> openDetailsAsync)
    {
        _fixtures = fixtures;
        _openDetailsCommand = PosterGridBuilder.CreateOpenCommand(openDetailsAsync);
    }

    public ObservableCollection<PosterGridRowViewModel> Rows { get; } = [];

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

    public int ItemCount
    {
        get => _itemCount;
        private set => SetProperty(ref _itemCount, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEmpty => !IsLoading && ItemCount == 0 && !HasError;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var items = await _fixtures.GetCatalogAsync(cancellationToken);
            Rows.Clear();
            foreach (var row in PosterGridBuilder.BuildRows(items, _openDetailsCommand))
            {
                Rows.Add(row);
            }

            ItemCount = items.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ItemCount = 0;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
