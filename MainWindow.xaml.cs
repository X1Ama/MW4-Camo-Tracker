using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MasteryTracker;

public partial class MainWindow : Window
{
    sealed record Row(string Key, string Name, string Cls, bool Fixed, string? CustomId);

    readonly Api api;
    readonly Profile me;
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

    public MainWindow(Api api, Profile me)
    {
        this.api = api;
        this.me = me;
        TitleBar.Apply(this);
        InitializeComponent();
        AdminButton.Visibility = me.IsStaff ? Visibility.Visible : Visibility.Collapsed;
        UserText.Text = $"{me.Label}  ·  {Catalog.RoleLabel(me.Role)}  ·  v{Updater.CurrentText}";

        BuildTabs();
        BuildFilters();

        if (!me.IsStaff)
        {
            SidePanel.Visibility = Visibility.Collapsed;
            SideCol.Width = new GridLength(0);
            target = me;
            Loaded += (_, _) => realtime?.Watch(me.Id);
        }

        timer.Tick += async (_, _) =>
        {
            // Fast safety-net polling until the server confirms live updates are working.
            bool idle = !IsActive || WindowState == WindowState.Minimized;
            timer.Interval = TimeSpan.FromSeconds(liveInstant ? (idle ? 120 : 30) : (idle ? 10 : 2));
            if (inflight == 0) await RefreshAsync();
        };
        Loaded += async (_, _) =>
        {
            if (me.IsStaff) await LoadClientsAsync();
            else { UpdateTitle(); await RefreshAsync(); }
            timer.Start();
        };
        Closed += (_, _) => { timer.Stop(); searchDebounce.Stop(); realtime?.Dispose(); };
        searchDebounce.Tick += (_, _) => { searchDebounce.Stop(); Render(); };
        if (api.SupportsRealtime)
        {
            realtime = new RealtimeClient(api.Config.Url, api.Config.AnonKey, api.GetAccessTokenAsync);
            realtime.Changed += OnRemoteChange;
            realtime.ConnectionChanged += ok => Dispatcher.BeginInvoke(() => liveInstant = ok);
        }
        _ = CheckUpdateAsync();
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        updateTimer.Tick += async (_, _) => await CheckUpdateAsync();
        updateTimer.Start();
        Closed += (_, _) => updateTimer.Stop();
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
                Background = m.Id == modeId ? Res("Panel2") : Brushes.Transparent,
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
                Background = filter == id ? Res("Fg") : Res("Panel"),
                Foreground = filter == id ? Res("AccentFg") : Res("Muted"),
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
        TitleText.Text = target == null ? "MASTERY CAMO TRACKER" : target.Label;
        KickerText.Text = me.IsStaff ? "MW4 · MANAGING" : "MW4 · MASTERY CAMO TRACKER";
    }

    UpdateInfo? pendingUpdate;

    async Task CheckUpdateAsync()
    {
        if (api is DemoApi) return;
        var url = ServerConfig.Load().UpdateUrl;
        if (url.Length == 0) return;
        pendingUpdate = await Updater.CheckAsync(url);
        if (pendingUpdate != null)
        {
            UpdateButton.Visibility = Visibility.Visible;
            UpdateButton.ToolTip = pendingUpdate.Notes;
        }
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate == null) return;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "DOWNLOADING…";
        try
        {
            await Updater.DownloadAndRestartAsync(pendingUpdate);
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "UPDATE AVAILABLE";
            SetStatus("Update failed: " + ex.Message, false);
        }
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
            adminPanel.Changed += async () => await LoadClientsAsync();
            adminPanel.CloseRequested += () => adminPanel.Visibility = Visibility.Collapsed;
            ((Grid)Content).Children.Add(adminPanel);
        }
        adminPanel.Visibility = Visibility.Visible;
    }

    void SignOut_Click(object sender, RoutedEventArgs e)
    {
        timer.Stop();
        new LoginWindow().Show();
        Close();
    }

    readonly DispatcherTimer searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        query = SearchBox.Text.Trim();
        searchDebounce.Stop();
        searchDebounce.Start();
    }

    // ---- clients (staff) ------------------------------------------------------

    async Task LoadClientsAsync()
    {
        try
        {
            clients = await api.GetManagedClientsAsync();
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
        Render();
        await RefreshAsync();
    }

    // ---- sync -----------------------------------------------------------------

    bool liveInstant;
    int remoteTicket;

    /// <summary>A change landed on the server: re-read soon (a short delay lets bursts of changes merge).</summary>
    readonly HashSet<string> pendingTables = new();
    bool pendingFull;

    void OnRemoteChange(string? table)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (table == null) pendingFull = true; else pendingTables.Add(table);
            int ticket = Interlocked.Increment(ref remoteTicket);
            await Task.Delay(120);
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
            if (sig != lastSig)
            {
                data = fresh;
                lastSig = sig;
                Render();
            }
            SetStatus((liveInstant ? "Live (instant) · " : "Live · ") + "checked " + DateTime.Now.ToString("HH:mm:ss"), true);
        }
        catch (Exception ex)
        {
            SetStatus("Offline, retrying… " + ex.Message, false);
        }
    }

    async void Mutate(Action apply, Action undo, Func<Task> send)
    {
        if (!CanEdit) return;
        apply();
        Render();
        inflight++;
        try
        {
            await send();
            lastSig = data.Signature();
        }
        catch (Exception ex)
        {
            undo();
            Render();
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
        TrackerPanel.Children.Clear();
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
            var chips = new List<(string Id, string Label, bool Track)>();
            if (mode.Track) chips.Add(("track-" + w.Key, "Camo Track", true));
            foreach (var c in mode.Camos) chips.Add(($"{mode.Id}-{w.Key}-{Catalog.Slug(c)}", c, false));

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

            groups[cls].Add(BuildRow(w, chips, d, all));
        }

        foreach (var (code, rows) in groups)
        {
            if (rows.Count == 0) continue;
            var (gd, gt) = groupDone[code];
            var head = new DockPanel { Margin = new Thickness(0, 22, 0, 0) };
            head.Children.Add(new TextBlock
            {
                Text = $"{gd}/{gt}",
                FontFamily = Mono,
                Foreground = Res("Muted"),
                FontSize = 12,
            });
            DockPanel.SetDock(head.Children[0], Dock.Right);
            head.Children.Add(new TextBlock
            {
                Text = Catalog.ClassName(code).ToUpperInvariant(),
                FontFamily = DisplayFont,
                FontSize = 18,
                Foreground = Res("Muted"),
            });
            TrackerPanel.Children.Add(head);
            foreach (var r in rows) TrackerPanel.Children.Add(r);
        }

        if (TrackerPanel.Children.Count == 0)
            TrackerPanel.Children.Add(new TextBlock { Text = "No matches.", Foreground = Res("Muted"), Margin = new Thickness(0, 16, 0, 0) });

        // Weapons are added in Catalog.cs by the app's maintainer, so there is no "Add weapon" box here.

        int pct = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
        Progress.Value = pct;
        ProgressText.Text = $"{done} / {total}  ·  {pct}%";
    }

    UIElement BuildRow(Row w, List<(string Id, string Label, bool Track)> chips, int d, bool all)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // left: name, count, type picker, level
        var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        var title = new DockPanel();
        if (CanEdit && w.CustomId != null)
        {
            var rm = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = "×",
                Padding = new Thickness(7, 0, 7, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "Remove weapon",
            };
            var id = w.CustomId;
            var snapshot = data.Customs.First(c => c.Id == id);
            rm.Click += (_, _) => Mutate(
                () => data.Customs.RemoveAll(c => c.Id == id),
                () => data.Customs.Add(snapshot),
                () => api.RemoveCustomAsync(id));
            DockPanel.SetDock(rm, Dock.Right);
            title.Children.Add(rm);
        }
        title.Children.Add(new TextBlock
        {
            Text = w.Name.ToUpperInvariant(),
            FontFamily = DisplayFont,
            FontSize = 21,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        left.Children.Add(title);
        left.Children.Add(new TextBlock
        {
            Text = $"{d}/{chips.Count} camos",
            FontFamily = Mono,
            FontSize = 11,
            Foreground = Res("Muted"),
        });

        if (CanEdit && !w.Fixed)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
            combo.Items.Add(new ComboBoxItem { Content = "Set type…", Tag = "" });
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
            left.Children.Add(combo);
        }

        left.Children.Add(BuildLevel(w));
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        // right: camo buttons
        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in chips)
        {
            bool on = data.Ticks.Contains(c.Id);
            var b = new Button
            {
                Style = (Style)FindResource("Chip"),
                Content = (on ? "✓  " : "◇  ") + c.Label.ToUpperInvariant(),
                Width = 226,
                Margin = new Thickness(0, 0, 6, 6),
                Background = on ? Res("Accent") : Res("Panel2"),
                Foreground = on ? Res("AccentFg") : Res("Fg"),
                BorderBrush = on ? Res("Accent") : Res("Line"),
                IsHitTestVisible = CanEdit,
            };
            var cid = c.Id;
            b.Click += (_, _) =>
            {
                bool now = !data.Ticks.Contains(cid);
                Mutate(
                    () => { if (now) data.Ticks.Add(cid); else data.Ticks.Remove(cid); },
                    () => { if (now) data.Ticks.Remove(cid); else data.Ticks.Add(cid); },
                    () => api.SetTickAsync(target!.Id, cid, now));
            };
            wrap.Children.Add(b);
        }
        Grid.SetColumn(wrap, 1);
        grid.Children.Add(wrap);

        return Panel(grid, all ? Res("Accent") : null);
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
            Background = Res("Fg"),
            Opacity = .8,
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
                    () => api.SetLevelAsync(target!.Id, w.Key, v));
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
            track.MouseLeftButtonDown += (_, e) =>
                SetLevel((int)Math.Round(e.GetPosition(track).X / Math.Max(1, track.ActualWidth) * Catalog.MaxLevel));

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            foreach (var (text, delta) in new[] { ("−", -1), ("+", 1) })
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
