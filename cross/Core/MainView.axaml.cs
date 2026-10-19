using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

/// <summary>
/// The whole tracker: sign-in, the weapon cards, admin panel, settings. It is a plain control so the desktop program
/// can put it in a window and the Android app can use it as its main screen. It adapts to a narrow (phone) width.
/// </summary>
public partial class MainView : UserControl
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

    // ---- what the program around this view tells it -------------------------------

    /// <summary>The desktop window says whether it is in front; phones are always "in front" while the app runs.</summary>
    public Func<bool>? IsForeground { get; set; }

    /// <summary>Called by the window each time it comes to the front.</summary>
    public async void NotifyActivated()
    {
        if (api != null && (DateTime.UtcNow - lastUpdateCheck).TotalSeconds > 60) await CheckUpdateAsync();
    }

    /// <summary>Called when the window closes: stops the timers and the live link.</summary>
    public void Shutdown()
    {
        timer.Stop(); searchDebounce.Stop(); updateTimer.Stop();
        realtime?.Dispose();
    }

    // ---- look-ups shared by the screens ---------------------------------------

    internal static IBrush Res(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) && v is IBrush b
            ? b : Brushes.Magenta;

    internal static FontFamily Font(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) && v is FontFamily f
            ? f : FontFamily.Default;

    internal static FontFamily Mono => Font("Mono");
    internal static FontFamily DisplayFont => Font("Display");

    /// <summary>Adds a full-screen layer on top of the tracker (the sign-in form, admin panel, settings and so on).</summary>
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

    public MainView() : this(true) { }

    MainView(bool allowResume)
    {
        InitializeComponent();
        BuildFilterRow();
        BuildTabs();
        BuildFilters();

        ClientList.SelectionChanged += ClientList_SelectionChanged;
        ClientSearch.TextChanged += (_, _) => ApplyClientFilter();
        SearchBox.TextChanged += SearchBox_TextChanged;
        UpdateButton.Click += (_, _) => Update_Click();
        AdminButton.Click += (_, _) => Admin_Click();
        SettingsButton.Click += (_, _) => Settings_Click();
        ResetButton.Click += (_, _) => Reset_Click();
        MenuButton.Click += (_, _) => SetDrawer(!drawerOpen);
        SizeChanged += (_, e) => OnWidthChanged(e.NewSize.Width);
        ClientList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Profile>((p, _) => ClientRow(p));

        timer.Tick += async (_, _) =>
        {
            if (api == null) return;
            bool idle = !(IsForeground?.Invoke() ?? true);
            if (idle && !trimmed) { trimmed = true; GC.Collect(2, GCCollectionMode.Optimized, false); }
            if (!idle) trimmed = false;
            // the safety-net poll slows down once the live link is confirmed
            timer.Interval = TimeSpan.FromSeconds(liveInstant ? (idle ? 60 : 20) : (idle ? 10 : 2));
            if (inflight == 0) await RefreshAsync();
        };
        searchDebounce.Tick += (_, _) => { searchDebounce.Stop(); Render(); };
        updateTimer.Tick += async (_, _) => await CheckUpdateAsync();

        // the phone's Back button closes whatever is open on top of the tracker before it leaves the app
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } top) top.BackRequested += OnBackRequested;
        };

        ShowLogin();
        if (allowResume)
        {
            var startup = ServerConfig.Load();
            if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            {
                if (startup.LaunchAtStartup) Autostart.Apply(true);       // keeps the entry pointing at this program
                _ = Task.Run(DesktopEntry.Ensure);                         // Linux: the applications menu entry
            }
            if (startup.IsComplete && startup.RememberMe && SessionStore.Load() != null) loginPanel?.HideForm();
            _ = TryResumeAsync();
        }
    }

    /// <summary>Opens straight into a session, used by the offline demo modes.</summary>
    public MainView(Api api, Profile me) : this(false)
    {
        HideLogin();
        StartSession(api, me);
    }

    bool trimmed;

    void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        foreach (var layer in new Control?[] { resetConfirm, updatePanel, passwordPanel, settingsPanel, adminPanel })
        {
            if (layer != null && layer.IsVisible) { layer.IsVisible = false; e.Handled = true; return; }
        }
        if (Compact && drawerOpen) { SetDrawer(false); e.Handled = true; }
    }

    ColumnDefinition SideCol => Root.ColumnDefinitions[0];

    // ---- phone-sized layout --------------------------------------------------------

    /// <summary>True on a narrow screen: one column of cards, the client list in a slide-out menu.</summary>
    public bool Compact { get; private set; }

    bool drawerOpen;
    double cardWidth = 270;

    /// <summary>The width of one weapon card: a fixed size on a wide screen, the full width on a phone.</summary>
    double CardWidth => Compact ? Math.Max(240, ScrollHost.Bounds.Width - 12) : 270;

    void OnWidthChanged(double width)
    {
        if (width <= 0) return;
        bool nowCompact = width < 720;
        if (nowCompact != Compact)
        {
            Compact = nowCompact;
            ApplyLayoutMode();
            BuildFilterRow();
            BuildTabs();
            BuildFilters();
            Render();
        }
        else if (Compact && Math.Abs(CardWidth - cardWidth) > 6)
        {
            cardWidth = CardWidth;
            Render();
        }
    }

    void ApplyLayoutMode()
    {
        MainArea.Margin = Compact ? new Thickness(14, 0) : new Thickness(24, 0);
        TitleText.FontSize = Compact ? 30 : 38;
        UserText.IsVisible = !Compact;
        UpdateButton.Content = Compact ? "UPDATE" : "UPDATE AVAILABLE";
        MenuButton.IsVisible = Compact && me != null && me.IsStaff;
        if (Compact)
        {
            // the client list becomes a menu that slides over the tracker
            SideCol.Width = new GridLength(0);
            Grid.SetColumnSpan(SidePanel, 2);
            SidePanel.HorizontalAlignment = HorizontalAlignment.Left;
            SidePanel.Width = 300;
            SidePanel.ZIndex = 20;
            SetDrawer(false);
        }
        else
        {
            Grid.SetColumnSpan(SidePanel, 1);
            SidePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            SidePanel.Width = double.NaN;
            SidePanel.ZIndex = 0;
            SideCol.Width = me != null && !me.IsStaff ? new GridLength(0) : new GridLength(280);
            SidePanel.IsVisible = me == null || me.IsStaff;
        }
    }

    void SetDrawer(bool open)
    {
        drawerOpen = open;
        if (Compact) SidePanel.IsVisible = open && me != null && me.IsStaff;
    }

    /// <summary>Lays the progress bar, filters, search box and reset button out for a wide or a narrow screen.</summary>
    void BuildFilterRow()
    {
        foreach (var part in new Control[] { ProgressBox, FilterPanel, SearchBox, ResetButton })
            (part.Parent as Panel)?.Children.Remove(part);
        ProgressBox.Margin = Compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 16, 0);
        Progress.Width = Compact ? 150 : 260;
        FilterPanel.Margin = Compact ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 10, 0);
        SearchBox.Width = Compact ? 130 : 190;
        SearchBox.Margin = Compact ? new Thickness(0, 0, 8, 0) : new Thickness(0);
        ResetButton.Margin = Compact ? new Thickness(0) : new Thickness(10, 0, 0, 0);

        if (Compact)
        {
            var line = new WrapPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(FilterPanel);
            line.Children.Add(SearchBox);
            line.Children.Add(ResetButton);
            var stack = new StackPanel();
            stack.Children.Add(ProgressBox);
            stack.Children.Add(line);
            FilterHost.Child = stack;
        }
        else
        {
            var dock = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(FilterPanel);
            right.Children.Add(SearchBox);
            right.Children.Add(ResetButton);
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
            dock.Children.Add(ProgressBox);
            FilterHost.Child = dock;
        }
    }
}
