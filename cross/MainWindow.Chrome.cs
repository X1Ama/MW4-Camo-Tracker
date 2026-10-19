using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class MainWindow
{
    // ---- updates ----------------------------------------------------------------

    UpdateInfo? pendingUpdate;
    UpdatePanel? updatePanel;

    async Task CheckUpdateAsync()
    {
        if (api is DemoApi && !Updater.Preview) return;
        var url = ServerConfig.Load().UpdateUrl;
        if (url.Length == 0 && !Updater.Preview) return;
        lastUpdateCheck = DateTime.UtcNow;
        pendingUpdate = await Updater.CheckAsync(url);
        if (pendingUpdate != null)
        {
            UpdateButton.IsVisible = true;
            ToolTip.SetTip(UpdateButton, pendingUpdate.Notes);
            Updater.Prefetch(pendingUpdate);          // download quietly now so UPDATE NOW is instant
            // pop the notes up once for each new version, whenever it is found while the app is open
            var ver = pendingUpdate.Version.ToString();
            if (popupVersion != ver)
            {
                popupVersion = ver;
                Update_Click();
            }
        }
    }

    void Update_Click()
    {
        if (pendingUpdate == null) return;
        if (updatePanel == null)
        {
            updatePanel = new UpdatePanel { IsVisible = false };
            updatePanel.CloseRequested += () => updatePanel.IsVisible = false;
            AddOverlay(updatePanel, 30);
        }
        updatePanel.Show(pendingUpdate);
    }

    // ---- the other screens -----------------------------------------------------------

    PasswordPanel? passwordPanel;
    AdminPanel? adminPanel;
    SettingsPanel? settingsPanel;

    void ChangePassword_Click()
    {
        if (passwordPanel == null)
        {
            passwordPanel = new PasswordPanel(api, me) { IsVisible = false };
            passwordPanel.CloseRequested += () => passwordPanel.IsVisible = false;
            AddOverlay(passwordPanel, 20);
        }
        passwordPanel.Reset();
        passwordPanel.IsVisible = true;
    }

    public void OpenAdminPanel() => Admin_Click();

    void Admin_Click()
    {
        if (adminPanel == null)
        {
            adminPanel = new AdminPanel(api, me) { IsVisible = false };
            adminPanel.Saved += () => realtime?.BroadcastPeopleChanged();   // everyone else refreshes straight away
            adminPanel.Changed += async () => await LoadClientsAsync();
            adminPanel.CloseRequested += () => adminPanel.IsVisible = false;
            AddOverlay(adminPanel, 10);
        }
        adminPanel.IsVisible = true;
    }

    void Settings_Click()
    {
        if (settingsPanel == null)
        {
            settingsPanel = new SettingsPanel(me) { IsVisible = false };
            settingsPanel.CloseRequested += () => settingsPanel!.IsVisible = false;
            settingsPanel.ChangePasswordRequested += () =>
            {
                settingsPanel!.IsVisible = false;
                ChangePassword_Click();
            };
            settingsPanel.SignOutRequested += SignOut;
            AddOverlay(settingsPanel, 15);
        }
        settingsPanel.Reload();
        settingsPanel.IsVisible = true;
        settingsPanel.Focus();
    }

    // ---- the "client opened the tracker" banner --------------------------------------

    StackPanel? toastHost;

    void OnClientOnline(string id)
    {
        if (me == null || !me.IsStaff) return;
        var settings = ServerConfig.Load();
        if (!settings.NotifyOnline) return;
        var who = clients.FirstOrDefault(c => c.Id == id);
        if (who == null) return;                      // not one of this person's clients
        if (settings.NotifySound) NotifySound.Play();

        if (toastHost == null)
        {
            toastHost = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 20, 20),
            };
            AddOverlay(toastHost, 40);
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new Ellipse
        {
            Width = 9, Height = 9, Fill = Res("Good"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        });
        line.Children.Add(new TextBlock { Text = who.Label, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        line.Children.Add(new TextBlock { Text = "  opened the tracker", FontSize = 14, Foreground = Res("Muted") });
        var toast = new Border
        {
            Background = Res("Panel2"),
            BorderBrush = Res("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = line,
            Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
            },
        };
        async void Close()
        {
            toast.Opacity = 0;
            await Task.Delay(260);
            toastHost?.Children.Remove(toast);
        }
        toast.PointerPressed += (_, _) => Close();
        toastHost.Children.Add(toast);
        Dispatcher.UIThread.Post(() => toast.Opacity = 1);
        var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        life.Tick += (_, _) => { life.Stop(); if (toastHost?.Children.Contains(toast) == true) Close(); };
        life.Start();
    }

    // ---- tray ---------------------------------------------------------------------------

    TrayIconHost? tray;

    /// <summary>Starts without showing the window: only a small icon until it is opened.</summary>
    public void StartInTray()
    {
        ShowTrayIcon();
    }

    void ShowTrayIcon()
    {
        if (tray != null) return;
        tray = new TrayIconHost("MW4 Camo Tracker");
        tray.Open += () => Dispatcher.UIThread.Post(RestoreFromTray);
        tray.Exit += () => Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown());
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
