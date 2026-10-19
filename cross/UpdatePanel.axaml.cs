using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace MasteryTracker;

/// <summary>Shows what changed in a new version and lets the person update now or later.</summary>
public partial class UpdatePanel : UserControl
{
    UpdateInfo? info;

    public event Action? CloseRequested;

    public UpdatePanel()
    {
        InitializeComponent();
        NotesList.ItemTemplate = new FuncDataTemplate<string>((text, _) =>
        {
            var row = new Grid { Margin = new Thickness(0, 3), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(new Border
            {
                Width = 6, Height = 6, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 7, 10, 0),
                VerticalAlignment = VerticalAlignment.Top, Background = MainWindow.Res("Accent"),
                RenderTransform = new RotateTransform(45),
            });
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            return row;
        });
        LaterButton.Click += (_, _) => CloseRequested?.Invoke();
        UpdateButton.Click += Update_Click;
    }

    public void Show(UpdateInfo update)
    {
        info = update;
        VersionText.Text = $"Version {Updater.CurrentText}  →  {update.Version.Major}.{update.Version.Minor}.{update.Version.Build}"
            + (update.Required ? "   ·   required" : "");
        NotesList.ItemsSource = update.Highlights;
        NoNotesText.IsVisible = update.Highlights.Count == 0;
        LaterButton.IsVisible = !update.Required;
        UpdateButton.IsEnabled = true;
        UpdateButton.Content = "UPDATE NOW";
        Progress.IsVisible = false;
        MessageText.IsVisible = false;
        IsVisible = true;
    }

    async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (info == null) return;
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        UpdateButton.Content = "DOWNLOADING…";
        Progress.Value = 0;
        Progress.IsVisible = true;
        MessageText.IsVisible = false;
        try
        {
            await Updater.DownloadAndRestartAsync(info, new Progress<double>(v => Dispatcher.UIThread.Post(() => Progress.Value = v)));
        }
        catch (Exception ex)
        {
            MessageText.Text = "Update failed: " + ex.Message;
            MessageText.IsVisible = true;
            UpdateButton.Content = "TRY AGAIN";
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            Progress.IsVisible = false;
        }
    }
}
