using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MasteryTracker;

public partial class AdminPanel : UserControl
{
    readonly Api api;
    readonly Profile me;
    List<Profile> people = new();
    List<(string AdminId, string ClientId)> links = new();
    Profile? selected;
    bool loadingDetail;

    /// <summary>Raised after anything changed so the main window can refresh its client list.</summary>
    public event Action? Changed;

    /// <summary>Raised the moment the server accepted a change, before the lists are re-read, so others can refresh at once.</summary>
    public event Action? Saved;

    /// <summary>Raised when the Back button is pressed so the main window can show the tracker again.</summary>
    public event Action? CloseRequested;

    void Back_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

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
        RoleRow.Visibility = IsOwner ? Visibility.Visible : Visibility.Collapsed;

        Loaded += async (_, _) => await ReloadAsync();
    }

    void Say(string text, bool ok = true)
    {
        MessageText.Text = text;
        MessageText.Foreground = (Brush)Application.Current.Resources[ok ? "Accent" : "Warn"];
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
        var q = UserSearch.Text.Trim();
        var keepId = selected?.Id;
        var list = people
            .Where(p => q.Length == 0 || p.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        list = Sorted(list);
        UserList.ItemsSource = list;
        CountText.Text = $"PEOPLE ({people.Count})";
        if (keepId != null) UserList.SelectedItem = list.FirstOrDefault(p => p.Id == keepId);
        ShowDetail();
    }

    /// <summary>Names in the order you dragged them to; anyone not placed yet comes after, owners first, then by name.</summary>
    readonly List<string> customOrder = PeopleOrder.Load();

    List<Profile> Sorted(IEnumerable<Profile> list) => list
        .OrderBy(p => { int i = customOrder.IndexOf(p.Id); return i >= 0 ? i : int.MaxValue; })
        .ThenBy(p => p.Role == "owner" ? 0 : p.Role == "admin" ? 1 : 2)
        .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // ---- drag a name to reorder ----------------------------------------------------------
    Point dragStart;
    Profile? dragging;
    ListBoxItem? dropMark;

    static T? Up<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    void UserList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(null);
        dragging = Up<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Profile;
    }

    void UserList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (dragging == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var who = dragging;
        dragging = null;
        DragDrop.DoDragDrop(UserList, new DataObject("person", who), DragDropEffects.Move);
        ClearDropMark();
    }

    // the name's row has a border inside its template: its top edge becomes the yellow "drop here" line
    static Border? RowBorder(ListBoxItem item) => item.Template?.FindName("B", item) as Border;

    void ClearDropMark()
    {
        if (dropMark != null && RowBorder(dropMark) is Border b)
        {
            b.BorderThickness = new Thickness(3, 0, 0, 0);
            b.ClearValue(Border.BorderBrushProperty);
            b.BorderBrush = dropMark.IsSelected ? (Brush)Application.Current.Resources["Accent"] : Brushes.Transparent;
        }
        dropMark = null;
    }

    void UserList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("person") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        var item = Up<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == dropMark) return;
        ClearDropMark();
        if (item != null && RowBorder(item) is Border b)
        {
            // a yellow line above the name it will be placed before
            b.BorderBrush = (Brush)Application.Current.Resources["Accent"];
            b.BorderThickness = new Thickness(3, 2, 0, 0);
            dropMark = item;
        }
    }

    void UserList_DragLeave(object sender, DragEventArgs e) => ClearDropMark();

    void UserList_Drop(object sender, DragEventArgs e)
    {
        ClearDropMark();
        if (e.Data.GetData("person") is not Profile moved) return;
        var targetItem = Up<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = targetItem?.DataContext as Profile;
        if (target?.Id == moved.Id) return;
        // the whole list in its current order, with the moved name taken out and put before the one it was dropped on
        var ids = Sorted(people).Select(p => p.Id).ToList();
        ids.Remove(moved.Id);
        int at = target == null ? ids.Count : ids.IndexOf(target.Id);
        ids.Insert(at < 0 ? ids.Count : at, moved.Id);
        customOrder.Clear();
        customOrder.AddRange(ids);
        PeopleOrder.Save(ids);
        ApplyFilter();
        e.Handled = true;
    }

    void UserSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    void NewUser_Click(object sender, RoutedEventArgs e)
    {
        selected = null;
        UserList.SelectedItem = null;
        ShowDetail();
        NewUser.Focus();
    }

    void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selected = UserList.SelectedItem as Profile;
        ShowDetail();
    }

    void ShowDetail()
    {
        var p = UserList.SelectedItem as Profile ?? selected;
        if (p == null || !people.Any(x => x.Id == p.Id))
        {
            selected = null;
            CreatePanel.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }
        selected = people.First(x => x.Id == p.Id);
        DetailPanel.Visibility = Visibility.Visible;
        CreatePanel.Visibility = Visibility.Collapsed;
        DetailTitle.Text = selected.Label;
        DetailSub.Text = $"{selected.UserName}  \u00B7  {Catalog.RoleLabel(selected.Role)}";
        loadingDetail = true;
        DetailName.Text = selected.DisplayName;
        DetailPass.Text = knownPass.GetValueOrDefault(selected.UserName, "");
        DetailRole.SelectedItem = DetailRole.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected.Role);
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
        AssignPanel.Visibility = showAssign ? Visibility.Visible : Visibility.Collapsed;
        if (showAssign)
        {
            var clients = people.Where(c => c.Role == "client")
                .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();
            if (clients.Count == 0)
                AssignList.Children.Add(new TextBlock { Text = "No clients yet.", Foreground = (Brush)Application.Current.Resources["Muted"] });
            var adminId = selected.Id;
            foreach (var c in clients)
            {
                var box = new CheckBox
                {
                    Style = (Style)FindResource("Pill"),
                    Content = c.Label,
                    IsChecked = links.Any(l => l.AdminId == adminId && l.ClientId == c.Id),
                    Width = 220,
                    Margin = new Thickness(0, 0, 10, 10),
                };
                var cid = c.Id;
                box.Click += async (_, _) =>
                {
                    bool on = box.IsChecked == true;
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

    async void Create_Click(object sender, RoutedEventArgs e)
    {
        var user = NewUser.Text.Trim();
        var pass = NewPass.Text;
        var name = NewName.Text.Trim();
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
        SessionStore.SaveKnown(knownPass);
        if (MessageText.Foreground == (Brush)Application.Current.Resources["Accent"])
        {
            NewUser.Text = ""; NewPass.Text = ""; NewName.Text = "";
        }
        CreateButton.IsEnabled = true;
    }

    async void SaveName_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null || loadingDetail) return;
        var id = selected.Id;
        await RunAsync(() => api.SetDisplayNameAsync(id, DetailName.Text.Trim()), "Display name saved.");
    }

    // passwords cannot be read back from the server, so this keeps the ones set in this session to show them again
    readonly Dictionary<string, string> knownPass = SessionStore.LoadKnown();

    void ShowPass_Click(object sender, RoutedEventArgs e)
    {
        bool show = DetailPass.Visibility != Visibility.Visible;
        DetailPass.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DetailPassHidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        EyeSlash.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void DetailPassHidden_Changed(object sender, RoutedEventArgs e)
    {
        if (DetailPass.Text != DetailPassHidden.Password) DetailPass.Text = DetailPassHidden.Password;
    }

    void DetailPass_Changed(object sender, TextChangedEventArgs e)
    {
        if (DetailPassHidden.Password != DetailPass.Text) DetailPassHidden.Password = DetailPass.Text;
    }

    async void SetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var pass = DetailPass.Text;
        if (pass.Length < 6) { Say("Password must be at least 6 characters.", false); return; }
        var id = selected.Id;
        var userName = selected.UserName;
        knownPass[userName] = pass;
        SessionStore.SaveKnown(knownPass);
        await RunAsync(() => api.SetPasswordAsync(id, pass), $"Password set for {userName}. Press SHOW to see it.");
    }

    async void SetRole_Click(object sender, RoutedEventArgs e)
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
            confirmDialog = new ConfirmDialog();
            Grid.SetColumnSpan(confirmDialog, 2);
            System.Windows.Controls.Panel.SetZIndex(confirmDialog, 50);
            ((Grid)Content).Children.Add(confirmDialog);
        }
        return confirmDialog;
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
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
