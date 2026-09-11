using Avalonia;
using Avalonia.Headless;

namespace MandrillSimulator.Tests;

// Entry point the headless session builds the real application from, so the
// tests exercise the same App.axaml (styles, theme dictionaries) as the desktop.
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
