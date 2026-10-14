using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MasteryTracker;

/// <summary>An in-window "are you sure?" box that matches the rest of the app, used instead of the Windows pop-up.</summary>
public partial class ConfirmDialog : UserControl
{
    TaskCompletionSource<bool>? answer;

    public ConfirmDialog()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows the box and completes with true when the person confirms, false for cancel or Esc.</summary>
    public Task<bool> AskAsync(string title, string body, string confirmText)
    {
        answer?.TrySetResult(false);
        answer = new TaskCompletionSource<bool>();
        TitleText.Text = title;
        BodyText.Text = body;
        ConfirmButton.Content = confirmText;
        Visibility = Visibility.Visible;
        CancelButton.Focus();          // the safe choice is the default
        return answer.Task;
    }

    void Finish(bool result)
    {
        Visibility = Visibility.Collapsed;
        answer?.TrySetResult(result);
        answer = null;
    }

    void Confirm_Click(object sender, RoutedEventArgs e) => Finish(true);

    void Cancel_Click(object sender, RoutedEventArgs e) => Finish(false);

    void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Finish(false);
            e.Handled = true;
        }
    }
}
