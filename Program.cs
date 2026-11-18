using System;
using Avalonia;

namespace DeadlockServerPicker;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppStartup.Args = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
