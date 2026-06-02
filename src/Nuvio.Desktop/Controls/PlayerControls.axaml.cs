using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nuvio.Desktop.Controls;

public partial class PlayerControls : UserControl
{
    public PlayerControls()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
