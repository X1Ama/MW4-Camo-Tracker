using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace MasteryTracker;

/// <summary>The desktop window around the tracker: title bar, tray icon, minimize-to-tray and "second launch" handling.</summary>
public partial class MainWindow : Window
{
    readonly MainView view;
    TrayIconHost? tray;

    public MainWindow() : this(new MainView()) { }

    public MainWindow(MainView view)
    {
        InitializeComponent();
        this.view = view;
        Content = view;
        view.IsForeground = () => IsActive && WindowState != WindowState.Minimized && IsVisible;
        Activated += (_, _) => view.NotifyActivated();
        Closed += (_, _) => { view.Shutdown(); tray?.Dispose(); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) Window_StateChanged();
        };
    }

    /// <summary>Starts without showing the window: only a small icon until it is opened.</summary>
    public void StartInTray() => ShowTrayIcon();

    void ShowTrayIcon()
    {
        if (tray != null) return;
        tray = new TrayIconHost("MW4 Camo Tracker");
        tray.Open += () => Dispatcher.UIThread.Post(RestoreFromTray);
        tray.Exit += () => Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as
            IClassicDesktopStyleApplicationLifetime)?.Shutdown());
        tray.Show();
    }

    /// <summary>The minimize button hides the window in the tray when that setting is on.</summary>
    void Window_StateChanged()
    {
        if (WindowState != WindowState.Minimized || !ServerConfig.Load().MinimizeToTray) return;
        ShowTrayIcon();
        Hide();
    }

    /// <summary>Called when the app is started a second time: show this window, from the tray or the taskbar.</summary>
    public void BringToFront()
    {
        if (tray != null) { RestoreFromTray(); return; }
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void RestoreFromTray()
    {
        tray?.Dispose();
        tray = null;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
