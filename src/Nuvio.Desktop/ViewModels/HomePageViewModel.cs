using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class HomePageViewModel : ViewModelBase
{
    private readonly ICatalogDataSource _dataSource;
    private readonly IAsyncRelayCommand<CatalogItem> _openDetailsCommand;
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    public HomePageViewModel(ICatalogDataSource dataSource, Func<CatalogItem, Task> openDetailsAsync)
    {
        _dataSource = dataSource;
        _openDetailsCommand = PosterGridBuilder.CreateOpenCommand(openDetailsAsync);
    }

    public ObservableCollection<HomeSectionViewModel> Sections { get; } = [];

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

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEmpty => !IsLoading && Sections.Count == 0 && !HasError;

    public bool IsLoaded => Sections.Count > 0 && !HasError;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var rails = await _dataSource.GetHomeRailsAsync(cancellationToken);
            Sections.Clear();
            foreach (var rail in rails)
            {
                Sections.Add(new HomeSectionViewModel(
                    rail.Title,
                    PosterGridBuilder.BuildCards(rail.Items, _openDetailsCommand)));
            }
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
}
