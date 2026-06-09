using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Nuvio.Core.Media;
using Nuvio.Core.Models;
using Nuvio.Desktop.Services;

namespace Nuvio.Desktop.ViewModels;

/// <summary>
/// Featured/hero carousel shown above the Home rails. Items are a seeded-shuffled, de-duplicated sample
/// of all loaded rail items (parity with upstream HomeRepository.publishCurrentState hero logic).
/// </summary>
public sealed class HomeHeroViewModel : ViewModelBase
{
    private readonly CancellationToken _cancellationToken;
    private HomeHeroItemViewModel? _selectedItem;

    public HomeHeroViewModel(
        IReadOnlyList<CatalogItem> items,
        Func<CatalogItem, Task> openDetailsAsync,
        IDesktopImageLoader? imageLoader,
        CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        var openCommand = PosterGridBuilder.CreateOpenCommand(openDetailsAsync);
        Items = new ObservableCollection<HomeHeroItemViewModel>(
            items.Select(item => new HomeHeroItemViewModel(item, openCommand, imageLoader)));
        SelectCommand = new RelayCommand<HomeHeroItemViewModel>(Select);

        if (Items.Count > 0)
        {
            Select(Items[0]);
            // Warm the poster thumbnails so the strip renders real art immediately.
            foreach (var item in Items)
            {
                _ = item.EnsurePosterAsync(cancellationToken);
            }
        }
    }

    public ObservableCollection<HomeHeroItemViewModel> Items { get; }

    public ICommand SelectCommand { get; }

    public bool HasItems => Items.Count > 0;

    public HomeHeroItemViewModel? SelectedItem
    {
        get => _selectedItem;
        private set => SetProperty(ref _selectedItem, value);
    }

    private void Select(HomeHeroItemViewModel? item)
    {
        if (item is null || ReferenceEquals(item, SelectedItem))
        {
            return;
        }

        foreach (var candidate in Items)
        {
            candidate.IsSelected = ReferenceEquals(candidate, item);
        }

        SelectedItem = item;
        _ = item.EnsureBackdropAsync(_cancellationToken);
    }
}

public sealed class HomeHeroItemViewModel : ViewModelBase
{
    private const int BackdropDecodeWidth = 1024;
    private const int PosterThumbnailDecodeWidth = 120;

    private readonly IDesktopImageLoader? _imageLoader;
    private IImage? _backdropImage;
    private IImage? _posterImage;
    private bool _isSelected;
    private bool _backdropRequested;
    private bool _posterRequested;

    public HomeHeroItemViewModel(CatalogItem item, ICommand openCommand, IDesktopImageLoader? imageLoader)
    {
        Item = item;
        OpenCommand = openCommand;
        _imageLoader = imageLoader;
    }

    public CatalogItem Item { get; }

    public string Title => Item.Name;

    public string TypeLabel => MediaTypeLabel.ForType(Item.Type);

    public string Subtitle => string.Join(
        "   •   ",
        new[] { Item.ReleaseInfo, TypeLabel }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string Initial => string.IsNullOrWhiteSpace(Title)
        ? "N"
        : Title.Trim()[0].ToString().ToUpperInvariant();

    public ICommand OpenCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public IImage? BackdropImage
    {
        get => _backdropImage;
        private set
        {
            if (SetProperty(ref _backdropImage, value))
            {
                OnPropertyChanged(nameof(ShowBackdropInitial));
            }
        }
    }

    public bool ShowBackdropInitial => BackdropImage is null;

    public IImage? PosterImage
    {
        get => _posterImage;
        private set
        {
            if (SetProperty(ref _posterImage, value))
            {
                OnPropertyChanged(nameof(ShowPosterInitial));
            }
        }
    }

    public bool ShowPosterInitial => PosterImage is null;

    public async Task EnsureBackdropAsync(CancellationToken cancellationToken)
    {
        var source = Item.BackgroundUrl ?? Item.PosterUrl;
        if (_imageLoader is null || source is null || _backdropRequested || BackdropImage is not null)
        {
            return;
        }

        _backdropRequested = true;
        try
        {
            BackdropImage = await _imageLoader.LoadAsync(source, BackdropDecodeWidth, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _backdropRequested = false;
        }
        catch
        {
            // Leave null so the gradient + initial shows.
        }
    }

    public async Task EnsurePosterAsync(CancellationToken cancellationToken)
    {
        if (_imageLoader is null || Item.PosterUrl is null || _posterRequested || PosterImage is not null)
        {
            return;
        }

        _posterRequested = true;
        try
        {
            PosterImage = await _imageLoader.LoadAsync(Item.PosterUrl, PosterThumbnailDecodeWidth, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _posterRequested = false;
        }
        catch
        {
            // Leave null so the initial shows.
        }
    }
}
