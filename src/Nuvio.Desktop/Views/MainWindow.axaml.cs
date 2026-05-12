using Avalonia.Controls;
using Avalonia.Input;
using System.Diagnostics;
using Nuvio.Desktop.ViewModels;

namespace Nuvio.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += OnClosed;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel viewModel &&
                viewModel.CanHandleShortcut(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                await viewModel.HandleShortcutAsync(e.Key, e.KeyModifiers);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Nuvio Desktop shortcut handling failed: {ex}");
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        if (DataContext is IAsyncDisposable disposable)
        {
            DataContext = null;
            try
            {
                await disposable.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Nuvio Desktop shutdown cleanup failed: {ex}");
            }
        }
    }
}
