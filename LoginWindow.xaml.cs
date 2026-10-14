using System.Windows;
using System.Windows.Input;

namespace MasteryTracker;

public partial class LoginWindow : Window
{
    readonly ServerConfig cfg = ServerConfig.Load();

    public LoginWindow()
    {
        TitleBar.Apply(this);
        InitializeComponent();
        EmailBox.Text = cfg.LastEmail;
        VersionText.Text = "v" + Updater.CurrentText;
        UrlBox.Text = cfg.Url;
        KeyBox.Text = cfg.AnonKey;
        // Clients get a server.json beside the exe, so only an unconfigured copy shows these fields.
        ServerExpander.IsExpanded = !cfg.IsComplete;
        // Once server.json supplies the server, clients never see these fields.
        ServerExpander.Visibility = cfg.IsComplete ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => (EmailBox.Text.Length == 0 ? (UIElement)EmailBox : PasswordBox).Focus();
        Loaded += async (_, _) => await CheckForUpdateAsync();
        GotKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(UpdateHints, System.Windows.Threading.DispatcherPriority.Input);
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(UpdateHints, System.Windows.Threading.DispatcherPriority.Input);
    }

    UpdateInfo? pending;

    async Task CheckForUpdateAsync()
    {
        if (cfg.UpdateUrl.Length == 0) return;
        pending = await Updater.CheckAsync(cfg.UpdateUrl);
        if (pending == null) return;
        UpdateBanner.Visibility = Visibility.Visible;
        UpdateText.Text = $"Version {pending.Version.Major}.{pending.Version.Minor}.{pending.Version.Build} is available"
            + (pending.Notes.Length > 0 ? ": " + pending.Notes : ".")
            + (pending.Required ? " You need to update before signing in." : "");
        SignInButton.IsEnabled = !pending.Required;
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (pending == null) return;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "DOWNLOADING…";
        UpdateProgress.Visibility = Visibility.Visible;
        try
        {
            await Updater.DownloadAndRestartAsync(pending, new Progress<double>(v => UpdateProgress.Value = v));
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Update failed: " + ex.Message;
            UpdateButton.Content = "TRY AGAIN";
            UpdateButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    // The hints show only while a box is empty and not focused, so they never sit under the typing cursor.
    void UpdateHints()
    {
        bool pwFocused = PasswordBox.IsKeyboardFocused || PlainBox.IsKeyboardFocused;
        PwHint.Visibility = Pw.Length == 0 && !pwFocused ? Visibility.Visible : Visibility.Collapsed;
        UserHint.Visibility = EmailBox.Text.Length == 0 && !EmailBox.IsKeyboardFocused
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void Pw_Changed(object sender, RoutedEventArgs e) => UpdateHints();

    void User_Changed(object sender, RoutedEventArgs e) => UpdateHints();

    string Pw => PlainBox.Visibility == Visibility.Visible ? PlainBox.Text : PasswordBox.Password;

    void ShowButton_Click(object sender, RoutedEventArgs e)
    {
        bool show = PlainBox.Visibility != Visibility.Visible;
        if (show)
        {
            PlainBox.Text = PasswordBox.Password;
            PlainBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            EyeSlash.Visibility = Visibility.Visible;
            ShowButton.ToolTip = "Hide password";
            PlainBox.Focus();
            PlainBox.CaretIndex = PlainBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PlainBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PlainBox.Visibility = Visibility.Collapsed;
            PlainBox.Text = "";
            EyeSlash.Visibility = Visibility.Collapsed;
            ShowButton.ToolTip = "Show password";
            PasswordBox.Focus();
        }
    }
    void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SignIn_Click(sender, e);
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        cfg.Url = UrlBox.Text.Trim();
        cfg.AnonKey = KeyBox.Text.Trim();
        var user = EmailBox.Text.Trim();
        if (!cfg.IsComplete)
        {
            ServerExpander.IsExpanded = true;
            ShowError("Enter the server URL and public key first.");
            return;
        }
        if (user.Length == 0 || Pw.Length == 0)
        {
            ShowError("Enter your user name and password.");
            return;
        }

        if (!user.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
        {
            ShowError("User names use only letters, numbers, dots, dashes and underscores.");
            return;
        }

        SignInButton.IsEnabled = false;
        SignInButton.Content = "SIGNING IN…";
        try
        {
            var api = new Api(cfg);
            var me = await api.SignInAsync(user.ToLowerInvariant() + Catalog.UserDomain, Pw);
            cfg.LastEmail = user;
            cfg.Save();
            var main = new MainWindow(api, me);
            main.Show();
            Close();
        }
        catch (ApiException ex) when (ex.Status is 400 or 401)
        {
            ShowError("Wrong user name or password.");
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError("Could not reach the server. Check your connection and the server URL.\n" + ex.Message);
        }
        finally
        {
            SignInButton.IsEnabled = true;
            SignInButton.Content = "SIGN IN";
        }
    }
}
