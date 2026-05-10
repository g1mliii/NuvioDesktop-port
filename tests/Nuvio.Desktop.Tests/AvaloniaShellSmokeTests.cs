using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Nuvio.Desktop;
using Nuvio.Desktop.ViewModels;
using Nuvio.Desktop.Views;
using Nuvio.Platform;

namespace Nuvio.Desktop.Tests;

public sealed class AvaloniaShellSmokeTests
{
    private static readonly object SetupLock = new();
    private static bool s_isSetup;

    [Fact]
    public void MainWindow_RendersPhaseZeroShellWithPlatformContext()
    {
        EnsureAvaloniaIsSetup();

        var platformInfo = new PlatformInfo(
            PlatformFamily.Windows,
            "win-x64",
            "Windows test platform");

        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(platformInfo)
        };

        window.Show();

        Assert.Equal("Nuvio Desktop", window.Title);

        var root = Assert.IsType<Grid>(window.Content);
        Assert.NotNull(root.FindControl<TextBlock>("PlatformNameText"));
        Assert.NotNull(root.FindControl<TextBlock>("RuntimeIdentifierText"));
        Assert.NotNull(root.FindControl<TextBlock>("PlatformDescriptionText"));
    }

    private static void EnsureAvaloniaIsSetup()
    {
        lock (SetupLock)
        {
            if (s_isSetup)
            {
                return;
            }

            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .WithInterFont()
                .LogToTrace()
                .SetupWithoutStarting();

            s_isSetup = true;
        }
    }
}
