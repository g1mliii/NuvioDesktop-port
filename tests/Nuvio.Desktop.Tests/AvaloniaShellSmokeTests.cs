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
    public void MainWindowViewModel_DefaultConstructor_DoesNotRunBlockingMpvDiscovery()
    {
        var viewModel = new MainWindowViewModel();

        Assert.Equal("Not found", viewModel.MpvStatus);
        Assert.Equal("Not detected", viewModel.MpvPath);
        Assert.Equal("Unavailable", viewModel.MpvVersion);
        Assert.Contains("without blocking app startup", viewModel.MpvMessage);
    }

    [Fact]
    public void MainWindow_RendersPhaseZeroShellWithPlatformContext()
    {
        EnsureAvaloniaIsSetup();

        var platformInfo = new PlatformInfo(
            PlatformFamily.Windows,
            "win-x64",
            "Windows test platform");
        var mpvDiscovery = MpvDiscoveryResult.Found(
            @"C:\mpv\mpv.exe",
            MpvInstallationSource.CommonLocation,
            "mpv 0.41.0");

        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(platformInfo, mpvDiscovery)
        };

        window.Show();

        Assert.Equal("Nuvio Desktop", window.Title);

        var root = Assert.IsType<Grid>(window.Content);
        Assert.NotNull(root.FindControl<TextBlock>("PlatformNameText"));
        Assert.NotNull(root.FindControl<TextBlock>("RuntimeIdentifierText"));
        Assert.NotNull(root.FindControl<TextBlock>("PlatformDescriptionText"));
        Assert.NotNull(root.FindControl<TextBlock>("MpvStatusText"));
        Assert.NotNull(root.FindControl<TextBlock>("MpvVersionText"));
        Assert.NotNull(root.FindControl<TextBlock>("MpvPathText"));
        Assert.NotNull(root.FindControl<TextBlock>("MpvSourceText"));
        Assert.NotNull(root.FindControl<TextBlock>("MpvPolicyText"));
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
