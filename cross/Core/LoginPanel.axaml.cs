using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class LoginPanel : UserControl
{
    readonly ServerConfig cfg = ServerConfig.Load();
    Api? api;

    /// <summary>Raised with the signed-in connection and person; the main window takes over from here.</summary>
    public event Action<Api, Profile, bool>? SignedIn;

    public LoginPanel()
    {
        InitializeComponent();
        EmailBox.Text = cfg.LastEmail;
        RememberCheck.IsChecked = cfg.RememberMe;
        VersionText.Text = "By X1Ama  ·  v" + Updater.CurrentText;
        UrlBox.Text = cfg.Url;
        KeyBox.Text = cfg.AnonKey;
        // The server details are built in, so only a damaged copy ever shows these fields.
        ServerExpander.IsExpanded = !cfg.IsComplete;
        ServerExpander.IsVisible = !cfg.IsComplete;

        ShowButton.Click += ShowButton_Click;
        SignInButton.Click += SignIn_Click;
        PasswordBox.KeyDown += Password_KeyDown;
        EmailBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) PasswordBox.Focus(); };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(FocusFirst);
    }

    void FocusFirst() => ((string.IsNullOrEmpty(EmailBox.Text) ? EmailBox : PasswordBox) as Control).Focus();

    /// <summary>Called whenever the sign-in screen is shown (first start, and after signing out).</summary>
    public void Reset()
    {
        PasswordBox.Text = "";
        PasswordBox.RevealPassword = false;
        EyeSlash.IsVisible = false;
        ErrorText.IsVisible = false;
        SignInButton.Content = "SIGN IN";
        SignInButton.IsEnabled = true;
        EmailBox.Text = cfg.LastEmail;
        RememberCheck.IsChecked = cfg.RememberMe;
        var latest = ServerConfig.Load();          // settings may have changed while signed in
        cfg.StartMinimized = latest.StartMinimized;
        cfg.LaunchAtStartup = latest.LaunchAtStartup;
        cfg.MinimizeToTray = latest.MinimizeToTray;
        cfg.NotifyOnline = latest.NotifyOnline;
        cfg.NotifySound = latest.NotifySound;
        api = null;
        if (cfg.IsComplete)
        {
            api = new Api(cfg);
            _ = api.WarmUpAsync();   // the connection is ready by the time they press Sign in
        }
        Dispatcher.UIThread.Post(FocusFirst);
    }

    /// <summary>Hides the sign-in form while a remembered sign-in is being used, so it never flashes.</summary>
    public void HideForm() => FormScroll.IsVisible = false;

    public void SetBusy(string? message)
    {
        if (message == null) FormScroll.IsVisible = true;
        IsEnabled = message == null;
        SignInButton.Content = message ?? "SIGN IN";
    }

    void ShowButton_Click(object? sender, RoutedEventArgs e)
    {
        PasswordBox.RevealPassword = !PasswordBox.RevealPassword;
        EyeSlash.IsVisible = PasswordBox.RevealPassword;
        ToolTip.SetTip(ShowButton, PasswordBox.RevealPassword ? "Hide password" : "Show password");
        PasswordBox.Focus();
    }

    void Password_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SignIn_Click(sender, e);
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        cfg.Url = UrlBox.Text?.Trim() ?? "";
        cfg.AnonKey = KeyBox.Text?.Trim() ?? "";
        var user = EmailBox.Text?.Trim() ?? "";
        var pw = PasswordBox.Text ?? "";
        if (!cfg.IsComplete)
        {
            ServerExpander.IsExpanded = true;
            ShowError("Enter the server URL and public key first.");
            return;
        }
        if (user.Length == 0 || pw.Length == 0)
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
        Timing.Start("sign-in pressed");
        try
        {
            api ??= new Api(cfg);
            var me = await api.SignInAsync(user.ToLowerInvariant() + Catalog.UserDomain, pw);
            Timing.Mark("server accepted the sign-in (login + profile)");
            cfg.LastEmail = user;
            cfg.RememberMe = RememberCheck.IsChecked == true;
            cfg.Save();
            var session = api;
            api = null;                 // the main window owns this connection now
            SignedIn?.Invoke(session, me, cfg.RememberMe);
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
