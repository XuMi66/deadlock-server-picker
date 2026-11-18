using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DeadlockServerPicker.ViewModels;
using DeadlockServerPicker.Views;

namespace DeadlockServerPicker;

public partial class App : Application
{
    /// <summary>Set right before the tray "退出" command so the main window may really close.</summary>
    public static bool AllowExit { get; private set; }

    /// <summary>True when this process is the second copy and should just hand over to the first one.</summary>
    public static bool IsSecondaryInstance { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!SingleInstance.TryAcquire(ShowMainWindow))
            {
                IsSecondaryInstance = true;
                SingleInstance.SignalExisting();
                desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) return;
            if (lifetime.MainWindow is not { } window) return;
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });
    }

    private void OnTrayShow(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayExit(object? sender, EventArgs e)
    {
        AllowExit = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
