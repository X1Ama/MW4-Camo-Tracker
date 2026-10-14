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
            .OrderBy(p => p.Role == "owner" ? 0 : p.Role == "admin" ? 1 : 2)
            .ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        UserList.ItemsSource = list;
        CountText.Text = $"PEOPLE ({people.Count})";
        if (keepId != null) UserList.SelectedItem = list.FirstOrDefault(p => p.Id == keepId);
        ShowDetail();
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
        DetailPass.Text = "";
        DetailRole.SelectedItem = DetailRole.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected.Role);
        loadingDetail = false;

        bool self = selected.Id == me.Id;
        bool canTouch = !self && (IsOwner || selected.Role == "client");
        DetailPanel.IsEnabled = true;
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
        await RunAsync(() => api.CreateUserAsync(user, pass, name, role), $"Created {user}. Give them the user name and password.");
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

    async void SetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var pass = DetailPass.Text;
        if (pass.Length < 6) { Say("Password must be at least 6 characters.", false); return; }
        var id = selected.Id;
        await RunAsync(() => api.SetPasswordAsync(id, pass), "Password changed.");
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
