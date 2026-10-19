using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MasteryTracker;

/// <summary>Window and account settings, shown inside the main window.</summary>
public partial class SettingsPanel : UserControl
{
    public event Action? CloseRequested;
    public event Action? ChangePasswordRequested;
    public event Action? SignOutRequested;

    bool loading;

    public SettingsPanel(Profile me)
    {
        InitializeComponent();
        WhoText.Text = $"{me.Label}  ·  {Catalog.RoleLabel(me.Role)}  ·  v{Updater.CurrentText}";
        NotifyRow.IsVisible = me.IsStaff;

        // a phone has no tray, no start-up entry and no window to minimize
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            foreach (var c in new Control[] { WindowLabel, StartupCheck, StartupNote, MinimizedCheck, MinimizedNote, TrayCheck, TrayNote })
                c.IsVisible = false;
        }

        // each system has its own words for these
        bool mac = OperatingSystem.IsMacOS(), win = OperatingSystem.IsWindows();
        string where = mac ? "menu bar" : "system tray";
        StartupCheck.Content = win ? "Launch when Windows starts" : mac ? "Launch when I log in" : "Launch when I sign in";
        StartupNote.Text = "Opens the tracker by itself every time you sign in to " + (win ? "Windows." : mac ? "your Mac." : "your computer.");
        MinimizedCheck.Content = "Start hidden in the " + where;
        MinimizedNote.Text = $"The app starts out of sight with a small icon in the {where}. Click the icon to open it.";
        TrayCheck.Content = "Minimize to the " + where;
        TrayNote.Text = $"The minimize button hides the app in the {where} instead of leaving it in the {(mac ? "Dock" : "taskbar")}.";

        StartupCheck.IsCheckedChanged += Startup_Changed;
        MinimizedCheck.IsCheckedChanged += (_, _) => { if (!loading) Store(c => c.StartMinimized = MinimizedCheck.IsChecked == true); };
        TrayCheck.IsCheckedChanged += (_, _) => { if (!loading) Store(c => c.MinimizeToTray = TrayCheck.IsChecked == true); };
        NotifyCheck.IsCheckedChanged += (_, _) => { if (!loading) Store(c => c.NotifyOnline = NotifyCheck.IsChecked == true); };
        SoundCheck.IsCheckedChanged += (_, _) => { if (!loading) Store(c => c.NotifySound = SoundCheck.IsChecked == true); };
        PasswordButton.Click += (_, _) => ChangePasswordRequested?.Invoke();
        SignOutButton.Click += (_, _) => SignOutRequested?.Invoke();
        DoneButton.Click += (_, _) => CloseRequested?.Invoke();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; }
        };
        Reload();
    }

    /// <summary>Reads the saved choices again each time the page is opened.</summary>
    public void Reload()
    {
        loading = true;
        var cfg = ServerConfig.Load();
        MinimizedCheck.IsChecked = cfg.StartMinimized;
        StartupCheck.IsChecked = cfg.LaunchAtStartup;
        NotifyCheck.IsChecked = cfg.NotifyOnline;
        SoundCheck.IsChecked = cfg.NotifySound;
        TrayCheck.IsChecked = cfg.MinimizeToTray;
        loading = false;
    }

    static void Store(Action<ServerConfig> change)
    {
        var cfg = ServerConfig.Load();
        change(cfg);
        cfg.Save();
    }

    void Startup_Changed(object? sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool on = StartupCheck.IsChecked == true;
        Store(c => c.LaunchAtStartup = on);
        Autostart.Apply(on);
    }
}
