using Avalonia;
using Velopack;

namespace MandrillSimulator;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: on the first launch after an install or
        // an update this handles the hooks and exits, so no window should have
        // been created by then.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
