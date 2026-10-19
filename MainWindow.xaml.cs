using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MasteryTracker;

public partial class MainWindow : Window
{
    sealed record Row(string Key, string Name, string Cls, bool Fixed, string? CustomId);

    Api api = null!;
    Profile me = null!;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    List<Profile> clients = new();
    Profile? target;
    ClientData data = new();
    string lastSig = "";
    string modeId = Catalog.Modes[0].Id;
    string filter = "all";
    string query = "";
    int inflight;
    RealtimeClient? realtime;
    bool CanEdit => target != null && (me.IsStaff || target.Id == me.Id);

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    static readonly FontFamily Mono = new("Consolas");
    static readonly Brush DoneTint = Frozen(Color.FromRgb(0x24, 0x20, 0x0A));
    static readonly Brush DoneLine = Frozen(Color.FromRgb(0xA8, 0x8A, 0x1A));
    static readonly Geometry MinusGeo = Geometry.Parse("M0,5 L10,5");
    static readonly Geometry PlusGeo = Geometry.Parse("M0,5 L10,5 M5,0 L5,10");

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static FontFamily DisplayFont => (FontFamily)Application.Current.Resources["Display"];

    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    LoginPanel? loginPanel;
    bool trimmed;
    DateTime lastTrim = DateTime.UtcNow;

    public MainWindow() : this(true) { }

    MainWindow(bool allowResume)
    {
        TitleBar.Apply(this);
        InitializeComponent();
        BuildTabs();
        BuildFilters();

        timer.Tick += async (_, _) =>
        {
            if (api == null) return;
            // Fast safety-net polling until the server confirms live updates are working.
            bool idle = !IsActive || WindowState == WindowState.Minimized;
            if (idle) { if (!trimmed) { trimmed = true; lastTrim = DateTime.UtcNow; MemoryTrim.Run(); } }
            else
            {
                trimmed = false;
                // while in use, hand unused memory back every so often as well
                if ((DateTime.UtcNow - lastTrim).TotalSeconds > 45) { lastTrim = DateTime.UtcNow; MemoryTrim.Run(); }
            }
            timer.Interval = TimeSpan.FromSeconds(liveInstant ? (idle ? 60 : 20) : (idle ? 10 : 2));
            if (inflight == 0) await RefreshAsync();
        };
        searchDebounce.Tick += (_, _) => { searchDebounce.Stop(); Render(); };
        StateChanged += Window_StateChanged;
        updateTimer.Tick += async (_, _) => await CheckUpdateAsync();
        // coming back to the window is a good moment to look for a new version too
        Activated += async (_, _) =>
        {
            if (api != null && (DateTime.UtcNow - lastUpdateCheck).TotalSeconds > 60) await CheckUpdateAsync();
        };
        Closed += (_, _) => { timer.Stop(); searchDebounce.Stop(); updateTimer.Stop(); realtime?.Dispose(); tray?.Dispose(); };

        ShowLogin();
        if (allowResume)
        {
            var startup = ServerConfig.Load();
            if (startup.LaunchAtStartup) StartupSettings.Apply(true);   // keeps the entry pointing at this exe
            _ = Task.Run(StartMenuShortcut.Ensure);                      // so Windows Search can find the app
            var saved = ServerConfig.Load();
            if (saved.IsComplete && saved.RememberMe && SessionStore.Load() != null) loginPanel?.HideForm();
            _ = TryResumeAsync();
        }
    }

    /// <summary>Opens straight into a session, used by the offline demo modes.</summary>
    public MainWindow(Api api, Profile me) : this(false)
    {
        HideLogin();
        StartSession(api, me);
    }

    // ---- sign-in lives inside this window --------------------------------------

    void ShowLogin()
    {
        if (loginPanel == null)
        {
            loginPanel = new LoginPanel();
            Grid.SetColumnSpan(loginPanel, 2);
            System.Windows.Controls.Panel.SetZIndex(loginPanel, 100);
            loginPanel.SignedIn += (a, p, remember) => StartSession(a, p, remember);
            ((Grid)Content).Children.Add(loginPanel);
        }
        loginPanel.Visibility = Visibility.Visible;
        loginPanel.Reset();
    }

    void HideLogin()
    {
        if (loginPanel == null) return;
        ((Grid)Content).Children.Remove(loginPanel);
        loginPanel = null;
    }

    /// <summary>When "Remember me" was ticked last time, signs straight back in without asking for the password.</summary>
    async Task TryResumeAsync()
    {
        var token = SessionStore.Load();
        if (token == null || loginPanel == null) { SessionStore.Log(token == null ? "no saved sign-in found" : "no login screen"); return; }
        var cfg = ServerConfig.Load();
        if (!cfg.IsComplete || !cfg.RememberMe) return;
        loginPanel.SetBusy("SIGNING YOU IN\u2026");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var resumed = new Api(cfg);
                Timing.Start("remembered session used");
                var profile = await resumed.ResumeAsync(token);
                SessionStore.Log("resumed ok");
                StartSession(resumed, profile, true);
                return;
            }
            catch (ApiException ex) when (ex.Status is 400 or 401 or 403)
            {
                SessionStore.Log($"server said {ex.Status}: {ex.Message}");
                // Another copy of the app (for example the one being replaced during an update) may have just
                // renewed the token. Use the newer one before giving up on the saved sign-in.
                var latest = SessionStore.Load();
                if (latest != null && latest != token) { token = latest; continue; }
                if (attempt == 0) { await Task.Delay(1500); continue; }   // the old copy may still be saving
                SessionStore.Clear();
                loginPanel?.SetBusy(null);
                return;
            }
            catch (Exception ex)
            {
                SessionStore.Log("could not reach the server: " + ex.Message);
                loginPanel?.SetBusy(null);          // offline: let them try by hand and keep the saved sign-in
                return;
            }
        }
        loginPanel?.SetBusy(null);
    }

    string? popupVersion;          // the update version whose pop-up was already shown
    DateTime lastUpdateCheck = DateTime.MinValue;

    void StartSession(Api newApi, Profile newMe, bool remember = false)
    {
        Timing.Mark("main screen taking over");
        api = newApi;
        me = newMe;
        if (loginPanel != null)
        {
            loginPanel.SetBusy(null);
            loginPanel.Visibility = Visibility.Collapsed;
        }
        if (remember)
        {
            SessionStore.Save(api.RefreshToken);
            api.RefreshTokenChanged = SessionStore.Save;   // keep the saved sign-in current as it renews
        }
        else
        {
            SessionStore.Clear();
        }
        popupVersion = null;

        clients = new();
        target = null;
        data = new ClientData();
        lastSig = "";
        inflight = 0;
        liveInstant = false;
        pendingUpdate = null;
        UpdateButton.Visibility = Visibility.Collapsed;
        query = "";
        SearchBox.Text = "";
        filter = "all";
        modeId = Catalog.Modes[0].Id;
        BuildFilters();
        BuildTabs();

        AdminButton.Visibility = me.IsStaff ? Visibility.Visible : Visibility.Collapsed;
        UserText.Text = $"{me.Label}  \u00B7  {Catalog.RoleLabel(me.Role)}  \u00B7  v{Updater.CurrentText}";
        if (me.IsStaff)
        {
            SidePanel.Visibility = Visibility.Visible;
            SideCol.Width = new GridLength(280);
            ClientList.ItemsSource = null;
        }
        else
        {
            SidePanel.Visibility = Visibility.Collapsed;
            SideCol.Width = new GridLength(0);
            target = me;
        }
        TrackerPanel.Children.Clear();
        UpdateTitle();
        SetStatus("Loading\u2026", true);

        if (api.SupportsRealtime)
        {
            realtime = new RealtimeClient(api.Config.Url, api.Config.AnonKey, api.GetAccessTokenAsync);
            realtime.Changed += OnRemoteChange;
            realtime.Detail = ApplyRemoteDetail;
            realtime.PresenceChanged += () => Dispatcher.BeginInvoke(RefreshPresenceUi);
            realtime.PeopleChanged += () => Dispatcher.BeginInvoke(OnPeopleChanged);
            realtime.CameOnline += id => Dispatcher.BeginInvoke(() => OnClientOnline(id));
            realtime.TrackPresence(me.Id);
            realtime.ConnectionChanged += ok => Dispatcher.BeginInvoke(() => liveInstant = ok);
            if (!me.IsStaff) realtime.Watch(me.Id);
        }

        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Start();
        updateTimer.Start();
        _ = CheckUpdateAsync();
        _ = BeginSessionAsync();
    }

    async Task BeginSessionAsync()
    {
        if (me.IsStaff) await LoadClientsAsync();
        else await InitialLoadAsync();
    }

    void EndSession()
    {
        timer.Stop();
        updateTimer.Stop();
        searchDebounce.Stop();
        realtime?.Dispose();
        realtime = null;
        Presence.Replace(new HashSet<string>());

        var root = (Grid)Content;
        foreach (var overlay in new UIElement?[] { adminPanel, passwordPanel, updatePanel, settingsPanel, resetConfirm })
            if (overlay != null) root.Children.Remove(overlay);
        if (toastHost != null) { root.Children.Remove(toastHost); toastHost = null; }
        adminPanel = null;
        passwordPanel = null;
        updatePanel = null;
        settingsPanel = null;
        resetConfirm = null;

        TrackerPanel.Children.Clear();
        ClientList.ItemsSource = null;
        clients = new();
        data = new ClientData();
        target = null;
        if (api != null) api.RefreshTokenChanged = null;
        api = null!;
        me = null!;
    }
    // ---- chrome ---------------------------------------------------------------

    void BuildTabs()
    {
        Tabs.Children.Clear();
        foreach (var m in Catalog.Modes)
        {
            var b = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = m.Name.ToUpperInvariant(),
                FontSize = 17,
                Padding = new Thickness(18, 9, 18, 9),
                Margin = new Thickness(0, 0, 4, 0),
                Background = m.Id == modeId ? Res("Accent") : Brushes.Transparent,
                Foreground = m.Id == modeId ? Res("AccentFg") : Res("Fg"),
                BorderBrush = m.Id == modeId ? Res("Accent") : Res("Line"),
            };
            var id = m.Id;
            b.Click += (_, _) => { modeId = id; BuildTabs(); Render(); };
            Tabs.Children.Add(b);
        }
    }

    void BuildFilters()
    {
        FilterPanel.Children.Clear();
        foreach (var (id, text) in new[] { ("all", "ALL"), ("todo", "TO DO"), ("done", "DONE") })
        {
            var b = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = text,
                FontSize = 13,
                Padding = new Thickness(12, 7, 12, 7),
                Background = filter == id ? Res("Accent") : Res("Panel"),
                Foreground = filter == id ? Res("AccentFg") : Res("Muted"),
                BorderBrush = filter == id ? Res("Accent") : Res("Line"),
            };
            var f = id;
            b.Click += (_, _) => { filter = f; BuildFilters(); Render(); };
            FilterPanel.Children.Add(b);
        }
    }

    void SetStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = ok ? Res("Muted") : Res("Warn");
    }

    void UpdateTitle()
    {
        TitleText.Text = target == null ? "MW4 CAMO TRACKER" : target.Label;
        KickerText.Text = me.IsStaff ? "MANAGING" : "MW4 CAMO TRACKER";
    }

    UpdateInfo? pendingUpdate;

    async Task CheckUpdateAsync()
    {
        if (api is DemoApi && !Updater.Preview) return;
        var url = ServerConfig.Load().UpdateUrl;
        if (url.Length == 0 && !Updater.Preview) return;
        lastUpdateCheck = DateTime.UtcNow;
        pendingUpdate = await Updater.CheckAsync(url);
        if (pendingUpdate != null)
        {
            UpdateButton.Visibility = Visibility.Visible;
            UpdateButton.ToolTip = pendingUpdate.Notes;
            Updater.Prefetch(pendingUpdate);          // download quietly now so UPDATE NOW is instant
            // pop the notes up once for each new version, whenever it is found while the app is open
            var ver = pendingUpdate.Version.ToString();
            if (popupVersion != ver)
            {
                popupVersion = ver;
                Update_Click(this, new RoutedEventArgs());
            }
        }
    }

    UpdatePanel? updatePanel;

    void Update_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate == null) return;
        if (updatePanel == null)
        {
            updatePanel = new UpdatePanel { Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(updatePanel, 2);
            System.Windows.Controls.Panel.SetZIndex(updatePanel, 30);
            updatePanel.CloseRequested += () => updatePanel.Visibility = Visibility.Collapsed;
            ((Grid)Content).Children.Add(updatePanel);
        }
        updatePanel.Show(pendingUpdate);
    }
    public void OpenAdminPanel() => Admin_Click(this, new RoutedEventArgs());

    PasswordPanel? passwordPanel;

    void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (passwordPanel == null)
        {
            passwordPanel = new PasswordPanel(api, me) { Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(passwordPanel, 2);
            System.Windows.Controls.Panel.SetZIndex(passwordPanel, 20);
            passwordPanel.CloseRequested += () => passwordPanel.Visibility = Visibility.Collapsed;
            ((Grid)Content).Children.Add(passwordPanel);
        }
        passwordPanel.Reset();
        passwordPanel.Visibility = Visibility.Visible;
    }

    AdminPanel? adminPanel;

    void Admin_Click(object sender, RoutedEventArgs e)
    {
        if (adminPanel == null)
        {
            adminPanel = new AdminPanel(api, me) { Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(adminPanel, 2);
            System.Windows.Controls.Panel.SetZIndex(adminPanel, 10);
            adminPanel.Saved += () => realtime?.BroadcastPeopleChanged();   // everyone else refreshes straight away
            adminPanel.Changed += async () => await LoadClientsAsync();
            adminPanel.CloseRequested += () => adminPanel.Visibility = Visibility.Collapsed;
            ((Grid)Content).Children.Add(adminPanel);
        }
        adminPanel.Visibility = Visibility.Visible;
    }

    StackPanel? toastHost;

    /// <summary>A small banner in the corner of the window: "Kam opened the tracker".</summary>
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
            Grid.SetColumnSpan(toastHost, 2);
            System.Windows.Controls.Panel.SetZIndex(toastHost, 40);
            ((Grid)Content).Children.Add(toastHost);
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9, Fill = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        });
        line.Children.Add(new TextBlock { Text = who.Label, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        line.Children.Add(new TextBlock
        {
            Text = "  opened the tracker", FontSize = 14, Foreground = Res("Muted"),
        });
        var toast = new Border
        {
            Background = Res("Panel2"),
            BorderBrush = Res("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0,
            Cursor = Cursors.Hand,
            Child = line,
        };
        void Close()
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => toastHost?.Children.Remove(toast);
            toast.BeginAnimation(OpacityProperty, fade);
        }
        toast.MouseLeftButtonDown += (_, _) => Close();
        toastHost.Children.Add(toast);
        toast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        life.Tick += (_, _) => { life.Stop(); if (toastHost?.Children.Contains(toast) == true) Close(); };
        life.Start();
    }

    /// <summary>Moves this window between the client and Grinder views without reconnecting.</summary>
    bool fadeInPending;

    static Task FadeAsync(UIElement el, double to, int ms)
    {
        var done = new TaskCompletionSource();
        var a = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase(),
        };
        a.Completed += (_, _) => done.TrySetResult();
        el.BeginAnimation(OpacityProperty, a);
        return done.Task;
    }

    /// <summary>Brings the window content back after a role switch, as soon as the new data is drawn.</summary>
    void FadeIn()
    {
        if (!fadeInPending) return;
        fadeInPending = false;
        _ = FadeAsync((Grid)Content, 1, 220);
    }

    async void SwitchRole(Profile fresh)
    {
        var root = (Grid)Content;
        fadeInPending = true;
        await FadeAsync(root, 0, 110);     // a short fade instead of a hard cut
        if (me == null) return;
        me = fresh;
        foreach (var overlay in new UIElement?[] { adminPanel, settingsPanel })
            if (overlay != null) root.Children.Remove(overlay);
        adminPanel = null;
        settingsPanel = null;
        resetConfirm = null;

        clients = new();
        target = null;
        data = new ClientData();
        lastSig = "";
        inflight = 0;

        AdminButton.Visibility = me.IsStaff ? Visibility.Visible : Visibility.Collapsed;
        UserText.Text = $"{me.Label}  \u00B7  {Catalog.RoleLabel(me.Role)}  \u00B7  v{Updater.CurrentText}";
        if (me.IsStaff)
        {
            SidePanel.Visibility = Visibility.Visible;
            SideCol.Width = new GridLength(280);
            ClientList.ItemsSource = null;
        }
        else
        {
            SidePanel.Visibility = Visibility.Collapsed;
            SideCol.Width = new GridLength(0);
            target = me;
            realtime?.Watch(me.Id);
        }
        TrackerPanel.Children.Clear();
        UpdateTitle();
        SetStatus("Loading\u2026", true);
        _ = BeginSessionAsync();
        // safety net: never leave the window faded out if the data is slow
        _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.BeginInvoke(FadeIn));
    }

    int peopleTicket;

    /// <summary>
    /// An admin changed people somewhere: re-read this person's own profile (role, name) and, for staff, the
    /// client list. A changed role switches this window between the client and Grinder views on the spot.
    /// </summary>
    async void OnPeopleChanged()
    {
        int ticket = Interlocked.Increment(ref peopleTicket);
        await Task.Delay(40);                        // a burst of changes becomes one refresh
        if (ticket != peopleTicket || api == null || me == null) return;
        var current = api;
        try
        {
            // the client list is asked for at the same time as the profile: a Grinder needs it either way, and
            // somebody who has just been promoted needs it straight away
            var clientsTask = current.GetManagedClientsAsync();
            if (me.IsStaff) current.PrefetchedClients = clientsTask;
            var fresh = await current.GetMyProfileAsync();
            if (ticket != peopleTicket || api != current) return;
            if (fresh == null) { SignOut_Click(this, new RoutedEventArgs()); return; }

            if (fresh.Role != me.Role)
            {
                // keep the connection and the live link; only the screen changes between client and Grinder
                current.PrefetchedOwnData = null;
                current.PrefetchedClients = fresh.IsStaff ? clientsTask : null;
                SwitchRole(fresh);
                return;
            }

            if (fresh.Label != me.Label) { me = fresh; UserText.Text = $"{me.Label}  \u00B7  {Catalog.RoleLabel(me.Role)}  \u00B7  v{Updater.CurrentText}"; }
            if (me.IsStaff)
            {
                await LoadClientsAsync();
                if (adminPanel != null) await adminPanel.ReloadAsync();
            }
        }
        catch (ApiException ex) when (ex.Status is 401 or 403)
        {
            SignOut_Click(this, new RoutedEventArgs());   // the account was removed
        }
        catch { /* the next change or the safety refresh will catch up */ }
    }

    TrayIcon? tray;

    /// <summary>Starts without showing the window: only a small icon near the clock until it is opened.</summary>
    public void StartInTray()
    {
        ShowInTaskbar = false;
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        ShowTrayIcon();
    }

    void ShowTrayIcon()
    {
        if (tray != null) return;
        tray = new TrayIcon("MW4 Camo Tracker");
        tray.Open += () => Dispatcher.BeginInvoke(RestoreFromTray);
        tray.Exit += () => Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        tray.Show();
    }

    /// <summary>The minimize button hides the window in the tray when that setting is on.</summary>
    void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized || !ServerConfig.Load().MinimizeToTray) return;
        ShowTrayIcon();
        ShowInTaskbar = false;
        Hide();
    }

    /// <summary>Called when the app is started a second time: show this window, from the tray or the taskbar.</summary>
    public void BringToFront()
    {
        if (tray != null) { RestoreFromTray(); return; }
        ShowInTaskbar = true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;      // a brief nudge so Windows lets it come to the front
        Topmost = false;
        Focus();
    }

    void RestoreFromTray()
    {
        tray?.Dispose();
        tray = null;
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    SettingsPanel? settingsPanel;

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsPanel == null)
        {
            settingsPanel = new SettingsPanel(me) { Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(settingsPanel, 2);
            System.Windows.Controls.Panel.SetZIndex(settingsPanel, 15);
            settingsPanel.CloseRequested += () => settingsPanel!.Visibility = Visibility.Collapsed;
            settingsPanel.ChangePasswordRequested += () =>
            {
                settingsPanel!.Visibility = Visibility.Collapsed;
                ChangePassword_Click(this, new RoutedEventArgs());
            };
            settingsPanel.SignOutRequested += () => SignOut_Click(this, new RoutedEventArgs());
            ((Grid)Content).Children.Add(settingsPanel);
        }
        settingsPanel.Reload();
        settingsPanel.Visibility = Visibility.Visible;
        settingsPanel.Focus();
    }

    void SignOut_Click(object sender, RoutedEventArgs e)
    {
        SessionStore.Clear();
        EndSession();
        ShowLogin();
    }

    readonly DispatcherTimer searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        query = SearchBox.Text.Trim();
        searchDebounce.Stop();
        searchDebounce.Start();
    }

    DateTime lastFullLoad = DateTime.MinValue;

    /// <summary>The client list that was requested during sign-in, or a fresh request if that one failed.</summary>
    async Task<List<Profile>> TakeClientsAsync()
    {
        var t = api.PrefetchedClients;
        api.PrefetchedClients = null;
        if (t != null)
        {
            try { return await t; } catch { /* fall through and ask again */ }
        }
        return await api.GetManagedClientsAsync();
    }

    /// <summary>A client's first screen: use the data requested during sign-in when there is some.</summary>
    async Task InitialLoadAsync()
    {
        var pre = api.PrefetchedOwnData;
        api.PrefetchedOwnData = null;
        if (pre != null)
        {
            try
            {
                var d = await pre;
                data = d;
                lastSig = d.Signature();
                lastFullLoad = DateTime.UtcNow;
                Timing.Mark("own data received");
                Render();
                Timing.Mark("first screen drawn");
                Timing.Finish();
                SetStatus("Live \u00B7 checked " + DateTime.Now.ToString("HH:mm:ss"), true);
                FadeIn();
                return;
            }
            catch { /* ask again below */ }
        }
        await RefreshAsync();
    }

    // ---- clients (staff) ------------------------------------------------------

    async Task LoadClientsAsync()
    {
        try
        {
            // only clients belong in this list: never the signed-in person, never another Grinder or owner
            clients = (await TakeClientsAsync()).Where(c => c.Role == "client" && c.Id != me.Id).ToList();
            Timing.Mark("client list received");
            ApplyClientFilter();
            if (clients.Count == 0)
                SetStatus("No clients assigned to you yet. Ask an owner to assign some.", false);
            else
                if (target == null || clients.All(c => c.Id != target.Id)) ClientList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            SetStatus("Could not load clients: " + ex.Message, false);
        }
    }

    void ApplyClientFilter()
    {
        var q = ClientSearch.Text.Trim();
        var keep = target?.Id;
        ClientList.ItemsSource = clients
            .Where(c => q.Length == 0 || c.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || c.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (keep != null && ClientList.ItemsSource is List<Profile> l)
            ClientList.SelectedItem = l.FirstOrDefault(c => c.Id == keep);
        var renewed = clients.FirstOrDefault(c => c.Id == keep);
        if (renewed != null && target != null && me != null && target.Id != me.Id) { target = renewed; UpdateTitle(); }
    }

    void ClientSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyClientFilter();

    async void ClientList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientList.SelectedItem is not Profile p || p.Id == target?.Id) return;
        target = p;
        realtime?.Watch(p.Id);
        data = new ClientData();
        lastSig = "";
        UpdateTitle();
        TrackerPanel.Children.Clear();       // the real screen is drawn once, when the data has arrived
        cardByKey.Clear();
        groupCount.Clear();
        TrackerPanel.Children.Add(new TextBlock { Text = "Loading\u2026", Foreground = Res("Muted"), Margin = new Thickness(0, 20, 0, 0) });
        await RefreshAsync();
    }

    // ---- sync -----------------------------------------------------------------

    bool liveInstant;
    int remoteTicket;

    /// <summary>Somebody opened or closed the app: refresh the green dots.</summary>
    void RefreshPresenceUi()
    {
        ApplyClientFilter();
        UpdateTitle();
    }

    /// <summary>A change landed on the server: re-read soon (a short delay lets bursts of changes merge).</summary>
    readonly HashSet<string> pendingTables = new();
    bool pendingFull;

    /// <summary>
    /// Applies a change straight from the realtime event so it shows up without another round trip.
    /// When it cannot be applied safely the normal re-read takes over.
    /// </summary>
    bool ApplyRemoteDetail(RemoteChange c)
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool applied;
            try { applied = ApplyOnUi(c); }
            catch { applied = false; }
            if (!applied) OnRemoteChange(c.Table);
        });
        return true;
    }

    bool ApplyOnUi(RemoteChange c)
    {
        var who = target;
        if (who == null || api == null) return true;                  // nothing on screen to update
        if (c.ClientId != null && c.ClientId != who.Id) return true;  // somebody else's row
        if (c.ClientId == null || inflight > 0) return false;
        bool gone = c.Type == "DELETE";
        switch (c.Table)
        {
            case "ticks" when c.CamoId != null:
                if (gone) data.Ticks.Remove(c.CamoId); else data.Ticks.Add(c.CamoId);
                break;
            case "levels" when c.WeaponKey != null:
                if (gone) data.Levels.Remove(c.WeaponKey);
                else if (c.Level != null) data.Levels[c.WeaponKey] = c.Level.Value;
                else return false;
                break;
            case "classes" when c.WeaponKey != null:
                if (gone) data.Classes.Remove(c.WeaponKey);
                else if (c.Cls != null) data.Classes[c.WeaponKey] = c.Cls;
                else return false;
                break;
            default:
                return false;
        }
        var sig = data.Signature();
        if (sig != lastSig)
        {
            lastSig = sig;
            Render();
        }
        SetStatus("Live (instant) \u00B7 changed " + DateTime.Now.ToString("HH:mm:ss"), true);
        return true;
    }

    void OnRemoteChange(string? table)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            // the "just connected" ping right after a fresh load would only repeat it
            if (table == null && (DateTime.UtcNow - lastFullLoad).TotalSeconds < 4) return;
            if (table == null) pendingFull = true; else pendingTables.Add(table);
            int ticket = Interlocked.Increment(ref remoteTicket);
            await Task.Delay(40);
            if (ticket != remoteTicket) return; // a newer change will do the refresh
            for (int i = 0; i < 20 && inflight > 0; i++) await Task.Delay(100);
            string? only = !pendingFull && pendingTables.Count == 1 ? pendingTables.First() : null;
            pendingFull = false;
            pendingTables.Clear();
            await RefreshAsync(only);
        });
    }

    async Task RefreshAsync(string? only = null)
    {
        var who = target;
        if (who == null) return;
        try
        {
            var fresh = new ClientData();
            if (only != null) fresh.CopyFrom(data); // keep the other tables, re-read just this one
            await api.LoadAsync(who.Id, fresh, only);
            if (inflight > 0 || who.Id != target?.Id) return;
            var sig = fresh.Signature();
            if (only == null) lastFullLoad = DateTime.UtcNow;
            if (sig != lastSig)
            {
                data = fresh;
                lastSig = sig;
                Timing.Mark("client data received");
                Render();
                Timing.Mark("first screen drawn");
                Timing.Finish();
            }
            SetStatus((liveInstant ? "Live (instant) \u00B7 " : "Live \u00B7 ") + "checked " + DateTime.Now.ToString("HH:mm:ss"), true);
            FadeIn();
        }
        catch (Exception ex)
        {
            SetStatus("Offline, retrying\u2026 " + ex.Message, false);
        }
    }

    void Redraw(string? weaponKey)
    {
        if (weaponKey != null) RefreshCard(weaponKey);
        else Render();
    }

    async void Mutate(Action apply, Action undo, Func<Task> send, string? weaponKey = null)
    {
        if (!CanEdit) return;
        apply();
        Redraw(weaponKey);
        inflight++;
        try
        {
            await send();
            lastSig = data.Signature();
        }
        catch (Exception ex)
        {
            undo();
            Redraw(weaponKey);
            SetStatus("Could not save: " + ex.Message, false);
        }
        finally
        {
            inflight--;
        }
    }

    // ---- tracker --------------------------------------------------------------

    List<Row> Rows()
    {
        var rows = Catalog.Weapons
            .Select(w =>
            {
                var key = Catalog.Slug(w.Name);
                var cls = w.Cls.Length > 0 ? w.Cls : data.Classes.GetValueOrDefault(key, "");
                return new Row(key, w.Name, cls, w.Cls.Length > 0, null);
            })
            .ToList();
        foreach (var c in data.Customs)
        {
            var key = "c-" + c.Id[..Math.Min(8, c.Id.Length)];
            rows.Add(new Row(key, c.Name, data.Classes.GetValueOrDefault(key, c.Cls), false, c.Id));
        }
        return rows;
    }

    static Border Panel(UIElement child, Brush? left = null) => new()
    {
        // A finished weapon gets an even green outline and a faint green tint, not a thick left edge
        // (a thick side next to rounded corners looks lopsided).
        Background = left == null ? Res("Panel") : DoneTint,
        BorderBrush = left == null ? Res("Line") : DoneLine,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(14, 12, 14, 12),
        Margin = new Thickness(0, 8, 0, 0),
        CornerRadius = new CornerRadius(10),
        Child = child,
    };

    void Render()
    {
        RenderCore();
    }

    readonly Dictionary<string, Border> cardByKey = new();
    readonly Dictionary<string, TextBlock> groupCount = new();

    List<(string Id, string Label, bool Track)> ChipsFor(Row w, Mode mode)
    {
        var chips = new List<(string Id, string Label, bool Track)>();
        if (mode.Track) chips.Add(("track-" + w.Key, "Camo Track", true));
        foreach (var c in mode.Camos) chips.Add(($"{mode.Id}-{w.Key}-{Catalog.Slug(c)}", c, false));
        return chips;
    }

    /// <summary>Redraws one weapon's card in place (a tick or a level change) instead of the whole list.</summary>
    void RefreshCard(string key)
    {
        if (target == null || me == null || !cardByKey.TryGetValue(key, out var old) || old.Parent is not Panel parent)
        {
            Render();
            return;
        }
        var mode = Catalog.Modes.First(m => m.Id == modeId);
        var w = Rows().FirstOrDefault(r => r.Key == key);
        if (w == null) { Render(); return; }
        var chips = ChipsFor(w, mode);
        int d = chips.Count(c => data.Ticks.Contains(c.Id));
        bool all = d == chips.Count;
        if ((filter == "todo" && all) || (filter == "done" && !all)) { Render(); return; }   // it leaves this list

        var card = (Border)BuildRow(w, chips, d, all);
        int i = parent.Children.IndexOf(old);
        parent.Children.RemoveAt(i);
        parent.Children.Insert(i, card);
        cardByKey[key] = card;
        UpdateCounts(mode);
    }

    /// <summary>The group counters and the overall progress bar, without rebuilding anything.</summary>
    void UpdateCounts(Mode mode)
    {
        int total = 0, done = 0;
        var perGroup = new Dictionary<string, (int D, int T)>();
        foreach (var w in Rows())
        {
            var chips = ChipsFor(w, mode);
            int d = chips.Count(c => data.Ticks.Contains(c.Id));
            total += chips.Count;
            done += d;
            var cls = Catalog.Classes.Any(c => c.Code == w.Cls) ? w.Cls : "";
            var (gd, gt) = perGroup.GetValueOrDefault(cls);
            perGroup[cls] = (gd + d, gt + chips.Count);
        }
        foreach (var (code, text) in groupCount)
            if (perGroup.TryGetValue(code, out var g)) text.Text = $"{g.D}/{g.T}";
        int pct = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        Progress.Value = pct;
        ProgressText.Text = $"{done} / {total}  \u00B7  {pct}%";
    }

    ConfirmDialog? resetConfirm;

    /// <summary>"Reset tab": clears every ticked camo shown on the current tab, after an "are you sure?" box.</summary>
    async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (me == null || target == null || !CanEdit) return;
        var mode = Catalog.Modes.First(m => m.Id == modeId);
        var ids = Rows().SelectMany(w => ChipsFor(w, mode)).Select(c => c.Id)
            .Where(id => data.Ticks.Contains(id)).Distinct().ToList();
        var levels = data.Levels.Where(kv => kv.Value > 0).ToList();
        if (ids.Count == 0 && levels.Count == 0) { SetStatus($"Nothing to reset in {mode.Name} yet.", true); return; }

        if (resetConfirm == null)
        {
            resetConfirm = new ConfirmDialog();
            Grid.SetColumnSpan(resetConfirm, 2);
            System.Windows.Controls.Panel.SetZIndex(resetConfirm, 50);
            ((Grid)Content).Children.Add(resetConfirm);
        }
        var who = target.Id == me.Id ? "your progress" : target.Label + "'s progress";
        var body = $"This clears {ids.Count} ticked camo{(ids.Count == 1 ? "" : "s")} in the {mode.Name} tab and sets every weapon level back to 0 ({who}). This cannot be undone."
            + (mode.Track ? " Camo Track and weapon levels are shared with the other tabs." : " Weapon levels are shared with the other tabs.");
        if (!await resetConfirm.AskAsync($"Reset {mode.Name}?", body, "RESET")) return;

        var cid = target.Id;
        try { Backups.Save(cid, target.Label, data, "before-reset"); }      // safety copy so a reset can be undone
        catch { }
        Mutate(
            () => { foreach (var id in ids) data.Ticks.Remove(id); data.Levels.Clear(); },
            () =>
            {
                foreach (var id in ids) data.Ticks.Add(id);
                foreach (var kv in levels) data.Levels[kv.Key] = kv.Value;
            },
            async () =>
            {
                await api.ResetTicksAsync(cid, ids);
                await api.ResetLevelsAsync(cid);
            });
    }

    /// <summary>"Save": writes the ticks and levels to a file in Documents\MW4 Camo Tracker\Backups.</summary>
    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (me == null || target == null || !CanEdit) return;
        try
        {
            var path = Backups.Save(target.Id, target.Label, data);
            SetStatus("Saved to " + path, true);
        }
        catch (Exception ex) { SetStatus("Could not save: " + ex.Message, false); }
    }

    /// <summary>"Restore": puts back the newest saved copy (adds the saved camos and levels back).</summary>
    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (me == null || target == null || !CanEdit) return;
        var snap = Backups.Latest(target.Id);
        if (snap == null) { SetStatus("No saved copy found yet. Press SAVE first.", true); return; }

        if (resetConfirm == null)
        {
            resetConfirm = new ConfirmDialog();
            Grid.SetColumnSpan(resetConfirm, 2);
            System.Windows.Controls.Panel.SetZIndex(resetConfirm, 50);
            ((Grid)Content).Children.Add(resetConfirm);
        }
        var body = $"Puts back {snap.Ticks.Count} camos and {snap.Levels.Count(kv => kv.Value > 0)} weapon levels from the copy saved on {snap.Saved:g}.";
        if (!await resetConfirm.AskAsync("Restore saved progress?", body, "RESTORE")) return;

        var cid = target.Id;
        var oldTicks = data.Ticks.ToList();
        var oldLevels = new Dictionary<string, int>(data.Levels);
        Mutate(
            () => { foreach (var t in snap.Ticks) data.Ticks.Add(t); foreach (var kv in snap.Levels) data.Levels[kv.Key] = kv.Value; },
            () =>
            {
                data.Ticks.Clear(); foreach (var t in oldTicks) data.Ticks.Add(t);
                data.Levels.Clear(); foreach (var kv in oldLevels) data.Levels[kv.Key] = kv.Value;
            },
            () => api!.RestoreAsync(cid, snap.Ticks, snap.Levels));
    }

    void RenderCore()
    {
        if (me == null || api == null) return;
        SaveButton.Visibility = RestoreButton.Visibility = ResetButton.Visibility = target != null && CanEdit ? Visibility.Visible : Visibility.Collapsed;
        TrackerPanel.Children.Clear();
        cardByKey.Clear();
        groupCount.Clear();
        if (target == null)
        {
            TrackerPanel.Children.Add(new TextBlock
            {
                Text = me.IsStaff ? "Pick a client on the left." : "",
                Foreground = Res("Muted"),
                Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }

        var mode = Catalog.Modes.First(m => m.Id == modeId);
        int total = 0, done = 0;
        var groups = Catalog.Classes.Select(c => c.Code).Append("").ToDictionary(c => c, _ => new List<UIElement>());
        var groupDone = groups.Keys.ToDictionary(k => k, _ => (0, 0));

        foreach (var w in Rows())
        {
            var chips = ChipsFor(w, mode);

            int d = chips.Count(c => data.Ticks.Contains(c.Id));
            total += chips.Count;
            done += d;
            var cls = groups.ContainsKey(w.Cls) ? w.Cls : "";
            var (gd, gt) = groupDone[cls];
            groupDone[cls] = (gd + d, gt + chips.Count);

            bool all = d == chips.Count;
            if (query.Length > 0 && !w.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter == "todo" && all) continue;
            if (filter == "done" && !all) continue;

            var built = (Border)BuildRow(w, chips, d, all);
            cardByKey[w.Key] = built;
            groups[cls].Add(built);
        }

        foreach (var (code, rows) in groups)
        {
            if (rows.Count == 0) continue;
            var (gd, gt) = groupDone[code];
            var head = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
            var countText = new TextBlock
            {
                Text = $"{gd}/{gt}",
                FontFamily = Mono,
                Foreground = Res("Muted"),
                FontSize = 12,
            };
            groupCount[code] = countText;
            head.Children.Add(countText);
            DockPanel.SetDock(countText, Dock.Right);
            head.Children.Add(new TextBlock
            {
                Text = Catalog.ClassName(code).ToUpperInvariant(),
                FontFamily = DisplayFont,
                FontSize = 18,
                Foreground = Res("Muted"),
            });
            TrackerPanel.Children.Add(head);
            var cards = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var r in rows) cards.Children.Add(r);
            TrackerPanel.Children.Add(cards);
        }

        if (TrackerPanel.Children.Count == 0)
            TrackerPanel.Children.Add(new TextBlock { Text = "No matches.", Foreground = Res("Muted"), Margin = new Thickness(0, 16, 0, 0) });

        // Weapons are added in Catalog.cs by the app's maintainer, so there is no "Add weapon" box here.

        TrackerPanel.Children.Add(new TextBlock
        {
            Text = "By X1Ama",
            Foreground = Res("Muted"),
            FontFamily = Mono,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 4),
        });

        int pct = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        Progress.Value = pct;
        ProgressText.Text = $"{done} / {total}  \u00B7  {pct}%";
    }

    static string Initials(string label) =>
        string.Concat(label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0])));

    /// <summary>One weapon as a card: its name and level on top, a strip of camo tiles underneath.</summary>
    UIElement BuildRow(Row w, List<(string Id, string Label, bool Track)> chips, int d, bool all)
    {
        var body = new StackPanel();

        // name (with the remove button for hand-added weapons)
        var title = new Grid();
        title.Children.Add(new TextBlock
        {
            Text = w.Name.ToUpperInvariant(),
            FontFamily = DisplayFont,
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(22, 0, 22, 0),
        });
        if (CanEdit && w.CustomId != null)
        {
            var rm = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = "\u00D7",
                Padding = new Thickness(7, 0, 7, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "Remove weapon",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var id = w.CustomId;
            var snapshot = data.Customs.First(c => c.Id == id);
            rm.Click += (_, _) => Mutate(
                () => data.Customs.RemoveAll(c => c.Id == id),
                () => data.Customs.Add(snapshot),
                () => api.RemoveCustomAsync(id));
            title.Children.Add(rm);
        }
        body.Children.Add(title);
        body.Children.Add(new TextBlock
        {
            Text = $"{d}/{chips.Count} camos",
            FontFamily = Mono,
            FontSize = 11,
            Foreground = Res("Muted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        });

        if (CanEdit && !w.Fixed)
        {
            // a plain button until it is used: building a drop-down for every weapon made the list slow to draw
            var host = new ContentControl { Margin = new Thickness(0, 6, 0, 0) };
            var pick = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = w.Cls.Length > 0 ? Catalog.ClassName(w.Cls) : "Set type\u2026",
                FontSize = 12,
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            pick.Click += (_, _) =>
            {
                var combo = new ComboBox { FontSize = 12 };
                combo.Items.Add(new ComboBoxItem { Content = "Set type\u2026", Tag = "" });
                foreach (var c in Catalog.Classes) combo.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Code });
                combo.SelectedIndex = Math.Max(0, Array.FindIndex(Catalog.Classes, c => c.Code == w.Cls) + 1);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem it) return;
                    var code = (string)it.Tag;
                    if (code == w.Cls) return;
                    var old = data.Classes.GetValueOrDefault(w.Key, "");
                    Mutate(
                        () => { if (code.Length > 0) data.Classes[w.Key] = code; else data.Classes.Remove(w.Key); },
                        () => { if (old.Length > 0) data.Classes[w.Key] = old; else data.Classes.Remove(w.Key); },
                        () => api.SetClassAsync(target!.Id, w.Key, code));
                };
                host.Content = combo;
                combo.IsDropDownOpen = true;
            };
            host.Content = pick;
            body.Children.Add(host);
        }

        body.Children.Add(BuildLevel(w));

        // the camo tiles: initials on a square, yellow when ticked, full name on hover
        var strip = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var c in chips)
        {
            bool on = data.Ticks.Contains(c.Id);
            var b = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = new TextBlock
                {
                    Text = on ? "\u2713" : Initials(c.Label),
                    FontFamily = Mono,
                    FontSize = on ? 15 : 12,
                    FontWeight = FontWeights.SemiBold,
                },
                Height = 38,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = on ? Res("Accent") : Res("Panel2"),
                Foreground = on ? Res("AccentFg") : Res("Fg"),
                BorderBrush = on ? Res("Accent") : Res("Line"),
                ToolTip = c.Label,
                IsHitTestVisible = CanEdit,
            };
            var cid = c.Id;
            b.Click += (_, _) =>
            {
                bool now = !data.Ticks.Contains(cid);
                Mutate(
                    () => { if (now) data.Ticks.Add(cid); else data.Ticks.Remove(cid); },
                    () => { if (now) data.Ticks.Remove(cid); else data.Ticks.Add(cid); },
                    () => api.SetTickAsync(target!.Id, cid, now),
                    w.Key);
            };
            strip.Children.Add(b);
        }
        body.Children.Add(strip);

        var card = Panel(body, all ? Res("Accent") : null);
        card.Width = 270;
        card.Margin = new Thickness(0, 10, 10, 0);
        return card;
    }
    UIElement BuildLevel(Row w)
    {
        int lv = Math.Min(Catalog.MaxLevel, data.Levels.GetValueOrDefault(w.Key, 0));
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        g.Children.Add(new TextBlock
        {
            Text = $"LV {lv}",
            FontFamily = Mono,
            FontSize = 11,
            Width = 46,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var track = new Grid { Height = 10, Background = Res("Panel2"), Cursor = CanEdit ? Cursors.Hand : Cursors.Arrow };
        var fill = new Border
        {
            Background = Res("Accent"),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        track.Children.Add(fill);
        track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * lv / Catalog.MaxLevel;
        var trackBox = new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), Child = track, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(5), Padding = new Thickness(1) };
        Grid.SetColumn(trackBox, 1);
        g.Children.Add(trackBox);

        if (CanEdit)
        {
            void SetLevel(int v)
            {
                v = Math.Clamp(v, 0, Catalog.MaxLevel);
                int old = data.Levels.GetValueOrDefault(w.Key, 0);
                if (v == old) return;
                Mutate(
                    () => data.Levels[w.Key] = v,
                    () => data.Levels[w.Key] = old,
                    () => api.SetLevelAsync(target!.Id, w.Key, v),
                    w.Key);
            }
            // a box to type the level directly (digits only, 0 to the maximum)
            g.Children.RemoveAt(0);
            var levelBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Width = 76 };
            levelBox.Children.Add(new TextBlock
            {
                Text = "LV",
                FontFamily = Mono,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            var input = new TextBox
            {
                Text = lv.ToString(),
                Width = 40,
                Height = 24,
                MaxLength = 3,
                FontFamily = Mono,
                FontSize = 12,
                Padding = new Thickness(2, 0, 2, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = $"Type a level from 0 to {Catalog.MaxLevel}",
            };
            input.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
            DataObject.AddPastingHandler(input, (_, e) =>
            {
                if (e.DataObject.GetData(DataFormats.Text) is string s && !s.All(char.IsAsciiDigit)) e.CancelCommand();
            });
            input.GotKeyboardFocus += (_, _) => input.SelectAll();
            void CommitLevel()
            {
                if (int.TryParse(input.Text, out var typed)) SetLevel(typed);
                input.Text = Math.Min(Catalog.MaxLevel, data.Levels.GetValueOrDefault(w.Key, 0)).ToString();
            }
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { CommitLevel(); Keyboard.ClearFocus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { input.Text = lv.ToString(); Keyboard.ClearFocus(); e.Handled = true; }
            };
            input.LostKeyboardFocus += (_, _) => CommitLevel();
            levelBox.Children.Add(input);
            Grid.SetColumn(levelBox, 0);
            g.Children.Add(levelBox);

            track.Background = Res("Panel2");
            // press and drag along the bar to scrub the level; it is saved once, when the mouse is let go
            bool scrubbing = false;
            int LevelAt(MouseEventArgs e) =>
                Math.Clamp((int)Math.Round(e.GetPosition(track).X / Math.Max(1, track.ActualWidth) * Catalog.MaxLevel), 0, Catalog.MaxLevel);
            void Preview(int v)
            {
                fill.Width = track.ActualWidth * v / Catalog.MaxLevel;
                input.Text = v.ToString();
            }
            track.MouseLeftButtonDown += (_, e) =>
            {
                scrubbing = true;
                track.CaptureMouse();
                Preview(LevelAt(e));
                e.Handled = true;
            };
            track.MouseMove += (_, e) =>
            {
                if (scrubbing) Preview(LevelAt(e));
            };
            track.MouseLeftButtonUp += (_, e) =>
            {
                if (!scrubbing) return;
                scrubbing = false;
                track.ReleaseMouseCapture();
                SetLevel(LevelAt(e));
            };
            track.LostMouseCapture += (_, _) =>
            {
                if (!scrubbing) return;
                scrubbing = false;
                Preview(lv);          // capture was taken away: put the bar back
            };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            foreach (var (text, delta) in new[] { ("\u2212", -1), ("+", 1) })
            {
                var b = new Button
                {
                    Style = (Style)FindResource("Chip"),
                    Content = text,
                    Width = 26,
                    Height = 26,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                };
                b.Content = new System.Windows.Shapes.Path { Data = delta < 0 ? MinusGeo : PlusGeo, Stroke = Res("Fg"), StrokeThickness = 1.6, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
                var dlt = delta;
                b.Click += (_, _) => SetLevel(data.Levels.GetValueOrDefault(w.Key, 0) + dlt);
                btns.Children.Add(b);
            }
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);
        }
        return g;
    }

}
