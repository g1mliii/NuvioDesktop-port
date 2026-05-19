using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Nuvio.Desktop.Services;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;

namespace Nuvio.Desktop;

public partial class App : Application
{
    private const string FixtureFlagArg = "--fixture-data";
    private const string FixtureFlagEnv = "NUVIO_FIXTURE_DATA";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var useFixture = ShouldUseFixtureData(desktop.Args);
            var viewModel = useFixture
                ? MainWindowViewModel.CreateFixture()
                : CreateLiveMainWindowViewModel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            _ = LoadMpvDiscoveryAsync(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool ShouldUseFixtureData(string[]? args)
    {
        if (args is not null && args.Any(arg =>
            string.Equals(arg, FixtureFlagArg, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fromEnv = Environment.GetEnvironmentVariable(FixtureFlagEnv);
        return string.Equals(fromEnv, "1", StringComparison.Ordinal) ||
               string.Equals(fromEnv, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static MainWindowViewModel CreateLiveMainWindowViewModel()
    {
        var host = DesktopBootstrap.BuildLive(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        return new MainWindowViewModel(
            PlatformInfoProvider.Current(),
            MpvDiscoveryResult.NotFound("Checking for mpv without blocking app startup."),
            host.DataSource,
            host.AddonService,
            new ExternalMpvPlayerEngineFactory(),
            servicesOwner: host,
            addonDiagnostics: host.Diagnostics);
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
