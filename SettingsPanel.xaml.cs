using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MasteryTracker;

/// <summary>Window and account settings, shown inside the main window.</summary>
public partial class SettingsPanel : UserControl
{
    public event Action? CloseRequested;
    public event Action? ChangePasswordRequested;
    public event Action? SignOutRequested;

    public SettingsPanel(Profile me)
    {
        InitializeComponent();
        NotifyRow.Visibility = me.IsStaff ? Visibility.Visible : Visibility.Collapsed;
        WhoText.Text = $"{me.Label}  \u00B7  {Catalog.RoleLabel(me.Role)}  \u00B7  v{Updater.CurrentText}";
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
        loading = false;
    }

    static void Store(Action<ServerConfig> change)
    {
        var cfg = ServerConfig.Load();
        change(cfg);
        cfg.Save();
    }

    bool loading;

    void Minimized_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        Store(c => c.StartMinimized = MinimizedCheck.IsChecked == true);
    }

    void Notify_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        Store(c => c.NotifyOnline = NotifyCheck.IsChecked == true);
    }

    void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        bool on = StartupCheck.IsChecked == true;
        Store(c => c.LaunchAtStartup = on);
        StartupSettings.Apply(on);
    }

    void ChangePassword_Click(object sender, RoutedEventArgs e) => ChangePasswordRequested?.Invoke();

    void SignOut_Click(object sender, RoutedEventArgs e) => SignOutRequested?.Invoke();

    void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    void Panel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
        }
    }
}
