using Avalonia.Controls;
using Avalonia.Input;

namespace MasteryTracker;

/// <summary>An in-window "are you sure?" box that matches the rest of the app.</summary>
public partial class ConfirmDialog : UserControl
{
    TaskCompletionSource<bool>? answer;

    public ConfirmDialog()
    {
        InitializeComponent();
        IsVisible = false;
        ConfirmButton.Click += (_, _) => Finish(true);
        CancelButton.Click += (_, _) => Finish(false);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        };
    }

    /// <summary>Shows the box and completes with true when the person confirms, false for cancel or Esc.</summary>
    public Task<bool> AskAsync(string title, string body, string confirmText)
    {
        answer?.TrySetResult(false);
        answer = new TaskCompletionSource<bool>();
        TitleText.Text = title;
        BodyText.Text = body;
        ConfirmButton.Content = confirmText;
        IsVisible = true;
        CancelButton.Focus();          // the safe choice is the default
        return answer.Task;
    }

    void Finish(bool result)
    {
        IsVisible = false;
        answer?.TrySetResult(result);
        answer = null;
    }
}
