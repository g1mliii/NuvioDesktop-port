using Avalonia;
using System;

namespace Nuvio.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless packaging smoke: boot the service graph, report platform + player
        // discovery, and exit without ever creating a window. Handled before Avalonia
        // starts, mirroring the --fixture-data flag plumbing in App.
        if (SelfCheck.IsRequested(args))
        {
            return SelfCheck.Run(Console.Out);
        }

        // Phase 9 performance/memory probe: boot the service graph headlessly, run scripted scenarios,
        // and emit a JSON report without ever opening a window. Handled before Avalonia starts.
        if (PerfProbe.IsRequested(args))
        {
            return PerfProbe.Run(args, Console.Out);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
