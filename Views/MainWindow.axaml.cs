using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DeadlockServerPicker.ViewModels;

namespace DeadlockServerPicker.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            DispatcherTimer.RunOnce(ResetScroll, TimeSpan.FromMilliseconds(900));
            DispatcherTimer.RunOnce(ResetScroll, TimeSpan.FromMilliseconds(2200));
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.AllowExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void ResetScroll()
    {
        var grid = this.FindControl<DataGrid>("ServersGrid");
        if (grid is null) return;
        var scrollViewer = grid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is not null) scrollViewer.Offset = new Vector(0, 0);
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MainWindowViewModel)?.Shutdown();
        base.OnClosed(e);
    }
}
