using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MasteryTracker;

public partial class LoginPanel : UserControl
{
    readonly ServerConfig cfg = ServerConfig.Load();
    Api? api;

    /// <summary>Raised with the signed-in connection and person; the main window takes over from here.</summary>
    public event Action<Api, Profile>? SignedIn;

    public LoginPanel()
    {
        InitializeComponent();
        EmailBox.Text = cfg.LastEmail;
        VersionText.Text = "By X1Ama  ·  v" + Updater.CurrentText;
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

    /// <summary>Called whenever the sign-in screen is shown (first start, and after signing out).</summary>
    public void Reset()
    {
        PasswordBox.Password = "";
        PlainBox.Text = "";
        if (PlainBox.Visibility == Visibility.Visible)
        {
            PasswordBox.Visibility = Visibility.Visible;
            PlainBox.Visibility = Visibility.Collapsed;
            EyeSlash.Visibility = Visibility.Collapsed;
            ShowButton.ToolTip = "Show password";
        }
        ErrorText.Visibility = Visibility.Collapsed;
        SignInButton.Content = "SIGN IN";
        SignInButton.IsEnabled = !(pending?.Required ?? false);
        EmailBox.Text = cfg.LastEmail;
        api = null;
        if (cfg.IsComplete)
        {
            api = new Api(cfg);
            _ = api.WarmUpAsync();   // the connection is ready by the time they press Sign in
        }
        UpdateHints();
        Dispatcher.BeginInvoke(() => (EmailBox.Text.Length == 0 ? (UIElement)EmailBox : PasswordBox).Focus(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    UpdateInfo? pending;

    async Task CheckForUpdateAsync()
    {
        if (cfg.UpdateUrl.Length == 0) return;
        pending = await Updater.CheckAsync(cfg.UpdateUrl);
        if (pending == null) return;
        UpdateBanner.Visibility = Visibility.Visible;
        UpdateText.Text = $"Version {pending.Version.Major}.{pending.Version.Minor}.{pending.Version.Build} is available"
            + (pending.Required ? ". You need to update before signing in." : "");
        if (pending.Highlights.Count > 0)
        {
            UpdateNotes.Text = "What's new\n" + string.Join("\n", pending.Highlights.Select(h => "\u2022  " + h));
            UpdateNotes.Visibility = Visibility.Visible;
        }
        SignInButton.IsEnabled = !pending.Required;
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (pending == null) return;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "DOWNLOADING\u2026";
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
        SignInButton.Content = "SIGNING IN\u2026";
        Timing.Start("sign-in pressed");
        try
        {
            api ??= new Api(cfg);
            var me = await api.SignInAsync(user.ToLowerInvariant() + Catalog.UserDomain, Pw);
            Timing.Mark("server accepted the sign-in (login + profile)");
            cfg.LastEmail = user;
            cfg.Save();
            var session = api;
            api = null;                 // the main window owns this connection now
            SignedIn?.Invoke(session, me);
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
