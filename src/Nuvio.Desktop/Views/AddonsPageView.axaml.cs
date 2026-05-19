using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nuvio.Desktop.Views;

public partial class AddonsPageView : UserControl
{
    public AddonsPageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
