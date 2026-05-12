using Nuvio.Desktop.Models;
using System.Windows.Input;

namespace Nuvio.Desktop.ViewModels;

public sealed class NavigationItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public NavigationItemViewModel(DesktopRoute route, ICommand navigateCommand)
    {
        Route = route;
        NavigateCommand = navigateCommand;
    }

    public DesktopRoute Route { get; }

    public ICommand NavigateCommand { get; }

    public string Label => Route.Label;

    public string Glyph => Route.Kind switch
    {
        DesktopRouteKind.Home => "H",
        DesktopRouteKind.Search => "S",
        DesktopRouteKind.Catalog => "C",
        DesktopRouteKind.Addons => "A",
        DesktopRouteKind.Settings => "G",
        _ => "N"
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
