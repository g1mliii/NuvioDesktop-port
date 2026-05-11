using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Threading;
using System.Linq;
using Avalonia.Markup.Xaml;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;

namespace Nuvio.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            _ = LoadMpvDiscoveryAsync(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task LoadMpvDiscoveryAsync(MainWindowViewModel viewModel)
    {
        var discovery = await Task.Run(() =>
        {
            try
            {
                return new MpvProcessLocator().Locate();
            }
            catch (Exception ex)
            {
                return MpvDiscoveryResult.NotFound($"mpv discovery failed: {ex.Message}");
            }
        }).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() => viewModel.ApplyMpvDiscovery(discovery));
    }
}
