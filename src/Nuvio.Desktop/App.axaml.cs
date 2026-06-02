using System.Diagnostics;
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

            _ = LoadMpvDiscoverySafelyAsync(viewModel);
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
        var themeController = new ThemeController();
        var viewModel = new MainWindowViewModel(
            PlatformInfoProvider.Current(),
            MpvDiscoveryResult.NotFound("Checking for mpv without blocking app startup."),
            host.DataSource,
            host.AddonService,
            new SelectingPlayerEngineFactory(),
            servicesOwner: host,
            addonDiagnostics: host.Diagnostics,
            settingsStore: host.SettingsStore,
            cacheMaintenance: host.CacheMaintenance,
            decodedImageMemoryCache: host.DecodedImageMemoryCache,
            imageLoader: host.ImageLoader,
            progressRepository: host.ProgressRepository,
            themeController: themeController);

        // Apply the saved theme + shell preferences before the window shows so there is no flash of the
        // wrong variant. BuildLive already loaded settings while constructing storage-backed services.
        try
        {
            themeController.Apply(host.InitialSettings.Theme);
            viewModel.ApplyPersistedShellSettings(host.InitialSettings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Nuvio Desktop theme/shell startup apply failed: {ex}");
        }

        return viewModel;
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

    private static async Task LoadMpvDiscoverySafelyAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await LoadMpvDiscoveryAsync(viewModel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var discovery = MpvDiscoveryResult.NotFound($"mpv discovery failed: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => viewModel.ApplyMpvDiscovery(discovery));
        }
    }
}
