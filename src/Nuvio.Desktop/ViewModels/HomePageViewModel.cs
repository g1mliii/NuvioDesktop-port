using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

public sealed class HomePageViewModel : ViewModelBase
{
    private readonly IDesktopFixtureService _fixtures;
    private readonly IAsyncRelayCommand<CatalogItem> _openDetailsCommand;
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    public HomePageViewModel(IDesktopFixtureService fixtures, Func<CatalogItem, Task> openDetailsAsync)
    {
        _fixtures = fixtures;
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

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var sections = await _fixtures.GetHomeSectionsAsync(cancellationToken);
            Sections.Clear();
            foreach (var section in sections)
            {
                Sections.Add(new HomeSectionViewModel(
                    section.Title,
                    PosterGridBuilder.BuildCards(section.Items, _openDetailsCommand)));
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
