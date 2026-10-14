using System.Windows;
using System.Windows.Controls;

namespace MasteryTracker;

/// <summary>Shows what changed in a new version and lets the person update now or later.</summary>
public partial class UpdatePanel : UserControl
{
    UpdateInfo? info;

    public event Action? CloseRequested;

    public UpdatePanel()
    {
        InitializeComponent();
    }

    public void Show(UpdateInfo update)
    {
        info = update;
        VersionText.Text = $"Version {Updater.CurrentText}  →  {update.Version.Major}.{update.Version.Minor}.{update.Version.Build}"
            + (update.Required ? "   ·   required" : "");
        NotesList.ItemsSource = update.Highlights;
        NoNotesText.Visibility = update.Highlights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LaterButton.Visibility = update.Required ? Visibility.Collapsed : Visibility.Visible;
        UpdateButton.IsEnabled = true;
        UpdateButton.Content = "UPDATE NOW";
        Progress.Visibility = Visibility.Collapsed;
        MessageText.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    void Later_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (info == null) return;
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        UpdateButton.Content = "DOWNLOADING…";
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed;
        try
        {
            await Updater.DownloadAndRestartAsync(info, new Progress<double>(v => Progress.Value = v));
        }
        catch (Exception ex)
        {
            MessageText.Text = "Update failed: " + ex.Message;
            MessageText.Visibility = Visibility.Visible;
            UpdateButton.Content = "TRY AGAIN";
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }
}
