using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MasteryTracker;

public partial class AdminPanel : UserControl
{
    readonly Api api;
    readonly Profile me;
    List<Profile> people = new();
    List<(string AdminId, string ClientId)> links = new();
    Profile? selected;
    bool loadingDetail;
    bool rebinding;

    /// <summary>Raised after anything changed so the main window can refresh its client list.</summary>
    public event Action? Changed;

    /// <summary>Raised the moment the server accepted a change, before the lists are re-read, so others can refresh at once.</summary>
    public event Action? Saved;

    public event Action? CloseRequested;

    bool IsOwner => me.Role == "owner";

    public AdminPanel(Api api, Profile me)
    {
        this.api = api;
        this.me = me;
        InitializeComponent();

        var roles = IsOwner ? new[] { "client", "admin", "owner" } : new[] { "client" };
        foreach (var r in roles)
        {
            NewRole.Items.Add(new ComboBoxItem { Content = Catalog.RoleLabel(r), Tag = r });
            DetailRole.Items.Add(new ComboBoxItem { Content = Catalog.RoleLabel(r), Tag = r });
        }
        NewRole.SelectedIndex = 0;
        RoleRow.IsVisible = IsOwner;

        UserList.ItemTemplate = new FuncDataTemplate<Profile>((p, _) => PersonRow(p));
        UserList.SelectionChanged += UserList_SelectionChanged;
        UserSearch.TextChanged += (_, _) => ApplyFilter();
        NewUserButton.Click += NewUser_Click;
        BackButton.Click += (_, _) => CloseRequested?.Invoke();
        CreateButton.Click += Create_Click;
        SaveNameButton.Click += SaveName_Click;
        SetPasswordButton.Click += SetPassword_Click;
        ShowPassButton.Click += (_, _) =>
        {
            bool hidden = DetailPass.PasswordChar != default(char);
            DetailPass.PasswordChar = hidden ? default(char) : (char)0x25CF;
            ShowPassButton.Content = hidden ? "HIDE" : "SHOW";
        };
        SetRoleButton.Click += SetRole_Click;
        DeleteButton.Click += Delete_Click;
        PeopleBackButton.Click += (_, _) =>
        {
            detailOpen = false;
            selected = null;
            UserList.SelectedItem = null;
            ShowDetail();
            UpdatePanes();
        };
        ListBackButton.Click += (_, _) => CloseRequested?.Invoke();
        SizeChanged += (_, e) => ApplyCompact(e.NewSize.Width < 720);

        AttachedToVisualTree += async (_, _) => await ReloadAsync();
    }

    /// <summary>One line of the people list: name, with the role badge on the right.</summary>
    Control PersonRow(Profile? p)
    {
        var dock = new DockPanel();
        if (p == null) return dock;
        var color = p.Role == "owner" ? "Accent" : p.Role == "admin" ? "Warn" : "Fg";
        var badge = new Border
        {
            BorderThickness = new Thickness(1), Padding = new Thickness(7, 2), Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center,
            Background = MainView.Res("Panel2"), BorderBrush = MainView.Res("Muted"),
            Child = new TextBlock
            {
                Text = p.RoleLabel, FontFamily = MainView.Mono, FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = MainView.Res(color),
            },
        };
        DockPanel.SetDock(badge, Dock.Right);
        dock.Children.Add(badge);
        dock.Children.Add(new TextBlock
        {
            Text = p.Label, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        return dock;
    }

    void Say(string text, bool ok = true)
    {
        MessageText.Text = text;
        MessageText.Foreground = MainView.Res(ok ? "Accent" : "Warn");
    }

    public async Task ReloadAsync()
    {
        try
        {
            people = await api.GetAllProfilesAsync();
            links = await api.GetAssignmentsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Say("Could not load people: " + ex.Message, false);
        }
    }

    void ApplyFilter()
    {
        var q = UserSearch.Text?.Trim() ?? "";
        var keepId = selected?.Id;
        var list = people
            .Where(p => q.Length == 0 || p.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Role == "owner" ? 0 : p.Role == "admin" ? 1 : 2)
            .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        rebinding = true;
        try
        {
            UserList.ItemsSource = list;
            CountText.Text = $"PEOPLE ({people.Count})";
            if (keepId != null) UserList.SelectedItem = list.FirstOrDefault(p => p.Id == keepId);
        }
        finally { rebinding = false; }
        ShowDetail();
    }

    // ---- phone layout: the list and the details take turns --------------------------------

    bool compact;
    bool detailOpen;

    void ApplyCompact(bool on)
    {
        if (on == compact) return;
        compact = on;
        if (on)
        {
            Root.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            Root.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(DetailPane, 0);
            ListPane.BorderThickness = new Thickness(0);
            ListBackButton.IsVisible = true;
        }
        else
        {
            Root.ColumnDefinitions[0].Width = new GridLength(300);
            Root.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(DetailPane, 1);
            ListPane.BorderThickness = new Thickness(0, 0, 1, 0);
            ListPane.IsVisible = true;
            DetailPane.IsVisible = true;
            PeopleBackButton.IsVisible = false;
            ListBackButton.IsVisible = false;
        }
        UpdatePanes();
    }

    void UpdatePanes()
    {
        if (!compact) return;
        ListPane.IsVisible = !detailOpen;
        DetailPane.IsVisible = detailOpen;
        PeopleBackButton.IsVisible = detailOpen;
    }

    void NewUser_Click(object? sender, RoutedEventArgs e)
    {
        detailOpen = true;
        UpdatePanes();
        selected = null;
        UserList.SelectedItem = null;
        ShowDetail();
        NewUser.Focus();
    }

    void UserList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (rebinding) return;
        selected = UserList.SelectedItem as Profile;
        if (selected != null) { detailOpen = true; UpdatePanes(); }
        ShowDetail();
    }

    void ShowDetail()
    {
        var p = UserList.SelectedItem as Profile ?? selected;
        if (p == null || !people.Any(x => x.Id == p.Id))
        {
            selected = null;
            CreatePanel.IsVisible = true;
            DetailPanel.IsVisible = false;
            return;
        }
        selected = people.First(x => x.Id == p.Id);
        DetailPanel.IsVisible = true;
        CreatePanel.IsVisible = false;
        DetailTitle.Text = selected.Label;
        DetailSub.Text = $"{selected.UserName}  ·  {Catalog.RoleLabel(selected.Role)}";
        loadingDetail = true;
        DetailName.Text = selected.DisplayName;
        DetailPass.Text = knownPass.GetValueOrDefault(selected.UserName, "");
        DetailRole.SelectedItem = DetailRole.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == selected.Role);
        loadingDetail = false;

        bool self = selected.Id == me.Id;
        bool canTouch = !self && (IsOwner || selected.Role == "client");
        // Grinders can only change their own clients; another Grinder or an owner is read-only for them.
        bool canEdit = IsOwner || (!self && selected.Role == "client");
        DetailPanel.IsEnabled = canEdit;
        if (!canEdit) Say("Only an owner can change another Grinder or an owner.", false);
        DeleteButton.IsEnabled = canTouch;
        RoleRow.IsEnabled = IsOwner && !self;

        // Owners can pick which clients an admin manages.
        AssignList.Children.Clear();
        bool showAssign = IsOwner && selected.Role == "admin";
        AssignPanel.IsVisible = showAssign;
        if (showAssign)
        {
            var clients = people.Where(c => c.Role == "client")
                .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();
            if (clients.Count == 0)
                AssignList.Children.Add(new TextBlock { Text = "No clients yet.", Foreground = MainView.Res("Muted") });
            var adminId = selected.Id;
            foreach (var c in clients)
            {
                var box = new CheckBox
                {
                    Content = c.Label,
                    IsChecked = links.Any(l => l.AdminId == adminId && l.ClientId == c.Id),
                    Width = 220,
                    Margin = new Thickness(0, 0, 10, 10),
                };
                box.Classes.Add("pill");
                var cid = c.Id;
                box.IsCheckedChanged += async (_, _) =>
                {
                    if (!box.IsEnabled) return;
                    bool on = box.IsChecked == true;
                    bool wasOn = links.Any(l => l.AdminId == adminId && l.ClientId == cid);
                    if (on == wasOn) return;
                    box.IsEnabled = false;
                    try
                    {
                        await api.AssignAsync(adminId, cid, on);
                        Saved?.Invoke();
                        links.RemoveAll(l => l.AdminId == adminId && l.ClientId == cid);
                        if (on) links.Add((adminId, cid));
                        Say(on ? "Client assigned." : "Client unassigned.");
                        Changed?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        box.IsChecked = !on;
                        Say(ex.Message, false);
                    }
                    finally { box.IsEnabled = true; }
                };
                AssignList.Children.Add(box);
            }
        }
    }

    async Task RunAsync(Func<Task> work, string okMessage)
    {
        try
        {
            await work();
            Saved?.Invoke();
            Say(okMessage);
            await ReloadAsync();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Say(ex.Message, false);
        }
    }

    async void Create_Click(object? sender, RoutedEventArgs e)
    {
        var user = NewUser.Text?.Trim() ?? "";
        var pass = NewPass.Text ?? "";
        var name = NewName.Text?.Trim() ?? "";
        var role = (NewRole.SelectedItem as ComboBoxItem)?.Tag as string ?? "client";
        if (user.Length == 0 || pass.Length == 0) { Say("Enter a user name and a password.", false); return; }
        if (!user.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
        {
            Say("User names use only letters, numbers, dots, dashes and underscores.", false);
            return;
        }
        CreateButton.IsEnabled = false;
        await RunAsync(() => api.CreateUserAsync(user, pass, name, role), $"Created. User name: {user}   Password: {pass}");
        knownPass[user] = pass;
        if (MessageText.Foreground == MainView.Res("Accent"))
        {
            NewUser.Text = ""; NewPass.Text = ""; NewName.Text = "";
        }
        CreateButton.IsEnabled = true;
    }

    async void SaveName_Click(object? sender, RoutedEventArgs e)
    {
        if (selected == null || loadingDetail) return;
        var id = selected.Id;
        await RunAsync(() => api.SetDisplayNameAsync(id, DetailName.Text?.Trim() ?? ""), "Display name saved.");
    }

    // passwords cannot be read back from the server, so this keeps the ones set in this session to show them again
    readonly Dictionary<string, string> knownPass = new();

    async void SetPassword_Click(object? sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var pass = DetailPass.Text ?? "";
        if (pass.Length < 6) { Say("Password must be at least 6 characters.", false); return; }
        var id = selected.Id;
        var userName = selected.UserName;
        knownPass[userName] = pass;
        await RunAsync(() => api.SetPasswordAsync(id, pass), $"Password set for {userName}. Press SHOW to see it.");
    }

    async void SetRole_Click(object? sender, RoutedEventArgs e)
    {
        if (selected == null || (DetailRole.SelectedItem as ComboBoxItem)?.Tag is not string role) return;
        var id = selected.Id;
        await RunAsync(() => api.SetRoleAsync(id, role), "Role changed.");
    }

    ConfirmDialog? confirmDialog;

    /// <summary>The in-window confirmation box, created the first time it is needed.</summary>
    ConfirmDialog Confirm()
    {
        if (confirmDialog == null)
        {
            confirmDialog = new ConfirmDialog { ZIndex = 50 };
            Grid.SetColumnSpan(confirmDialog, 2);
            Root.Children.Add(confirmDialog);
        }
        return confirmDialog;
    }

    async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var label = selected.Label;
        var confirmed = await Confirm().AskAsync(
            $"Delete {label}?",
            $"This removes {label} and all of their progress. This cannot be undone.",
            "DELETE USER");
        if (!confirmed) return;
        var id = selected.Id;
        selected = null;
        await RunAsync(() => api.DeleteUserAsync(id), $"Deleted {label}.");
    }
}
