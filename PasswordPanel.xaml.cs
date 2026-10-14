using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MasteryTracker;

/// <summary>Lets the signed-in person change their own password, inside the main window.</summary>
public partial class PasswordPanel : UserControl
{
    readonly Api api;
    readonly Profile me;

    public event Action? CloseRequested;

    public PasswordPanel(Api api, Profile me)
    {
        this.api = api;
        this.me = me;
        InitializeComponent();
        WhoText.Text = me.Label;
    }

    /// <summary>Clears the boxes each time the panel is opened.</summary>
    public void Reset()
    {
        CurrentBox.Password = NewBox.Password = RepeatBox.Password = "";
        MessageText.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = true;
        CancelButton.Content = "CLOSE";
        CurrentBox.Focus();
    }

    void Say(string text, bool ok)
    {
        MessageText.Text = text;
        MessageText.Foreground = (Brush)Application.Current.Resources[ok ? "Accent" : "Warn"];
        MessageText.Visibility = Visibility.Visible;
    }

    void Changed(object sender, RoutedEventArgs e) => MessageText.Visibility = Visibility.Collapsed;

    void Cancel_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    void RepeatBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Save_Click(sender, e);
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentBox.Password;
        var next = NewBox.Password;
        if (current.Length == 0) { Say("Enter your current password.", false); return; }
        if (next.Length < 6) { Say("The new password must be at least 6 characters.", false); return; }
        if (next != RepeatBox.Password) { Say("The two new passwords do not match.", false); return; }
        if (next == current) { Say("The new password is the same as the current one.", false); return; }

        SaveButton.IsEnabled = false;
        try
        {
            await api.ChangePasswordAsync(me.Email, current, next);
            CurrentBox.Password = NewBox.Password = RepeatBox.Password = "";
            Say("Password changed. Use the new one next time you sign in.", true);
            CancelButton.Content = "DONE";
        }
        catch (ApiException ex) when (ex.Status is 400 or 401)
        {
            Say(ex.Message.Contains("same", StringComparison.OrdinalIgnoreCase)
                ? "The new password must be different from the old one."
                : "Your current password is wrong.", false);
        }
        catch (Exception ex)
        {
            Say("Could not change the password: " + ex.Message, false);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
