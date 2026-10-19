using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class MainWindow : Window
{
    sealed record Row(string Key, string Name, string Cls, bool Fixed, string? CustomId);

    Api api = null!;
    Profile me = null!;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    readonly DispatcherTimer searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    List<Profile> clients = new();
    Profile? target;
    ClientData data = new();
    string lastSig = "";
    string modeId = Catalog.Modes[0].Id;
    string filter = "all";
    string query = "";
    int inflight;
    RealtimeClient? realtime;
    LoginPanel? loginPanel;
    DateTime lastUpdateCheck = DateTime.MinValue;
    bool CanEdit => target != null && (me.IsStaff || target.Id == me.Id);

    // ---- look-ups shared by the screens ---------------------------------------

    internal static IBrush Res(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) && v is IBrush b
            ? b : Brushes.Magenta;

    internal static FontFamily Font(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) && v is FontFamily f
            ? f : FontFamily.Default;

    internal static FontFamily Mono => Font("Mono");
    internal static FontFamily DisplayFont => Font("Display");

    /// <summary>Adds a full-window screen on top of the tracker (the sign-in form, admin panel, settings and so on).</summary>
    internal void AddOverlay(Control overlay, int z)
    {
        Grid.SetColumnSpan(overlay, 2);
        overlay.ZIndex = z;
        Root.Children.Add(overlay);
    }

    internal void RemoveOverlay(Control? overlay)
    {
        if (overlay != null) Root.Children.Remove(overlay);
    }

    // ---- start-up ---------------------------------------------------------------

    public MainWindow() : this(true) { }

    MainWindow(bool allowResume)
    {
        InitializeComponent();
        BuildTabs();
        BuildFilters();

        ClientList.SelectionChanged += ClientList_SelectionChanged;
        ClientSearch.TextChanged += (_, _) => ApplyClientFilter();
        SearchBox.TextChanged += SearchBox_TextChanged;
        UpdateButton.Click += (_, _) => Update_Click();
        AdminButton.Click += (_, _) => Admin_Click();
        SettingsButton.Click += (_, _) => Settings_Click();
        ClientList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Profile>((p, _) => ClientRow(p));

        timer.Tick += async (_, _) =>
        {
            if (api == null) return;
            bool idle = !IsActive || WindowState == WindowState.Minimized || !IsVisible;
            if (idle && !trimmed) { trimmed = true; GC.Collect(2, GCCollectionMode.Optimized, false); }
            if (!idle) trimmed = false;
            // the safety-net poll slows down once the live link is confirmed
            timer.Interval = TimeSpan.FromSeconds(liveInstant ? (idle ? 60 : 20) : (idle ? 10 : 2));
            if (inflight == 0) await RefreshAsync();
        };
        searchDebounce.Tick += (_, _) => { searchDebounce.Stop(); Render(); };
        updateTimer.Tick += async (_, _) => await CheckUpdateAsync();
        Activated += async (_, _) =>
        {
            // coming back to the window is a good moment to look for a new version too
            if (api != null && (DateTime.UtcNow - lastUpdateCheck).TotalSeconds > 60) await CheckUpdateAsync();
        };
        Closed += (_, _) => { timer.Stop(); searchDebounce.Stop(); updateTimer.Stop(); realtime?.Dispose(); tray?.Dispose(); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) Window_StateChanged();
        };

        ShowLogin();
        if (allowResume)
        {
            var startup = ServerConfig.Load();
            if (startup.LaunchAtStartup) Autostart.Apply(true);       // keeps the entry pointing at this program
            _ = Task.Run(DesktopEntry.Ensure);                         // Linux: the applications menu entry
            if (startup.IsComplete && startup.RememberMe && SessionStore.Load() != null) loginPanel?.HideForm();
            _ = TryResumeAsync();
        }
    }

    /// <summary>Opens straight into a session, used by the offline demo modes.</summary>
    public MainWindow(Api api, Profile me) : this(false)
    {
        HideLogin();
        StartSession(api, me);
    }

    bool trimmed;

    ColumnDefinition SideCol => Root.ColumnDefinitions[0];
}
