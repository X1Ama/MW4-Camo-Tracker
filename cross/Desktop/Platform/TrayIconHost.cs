using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace MasteryTracker;

/// <summary>
/// The small tray / menu-bar icon shown while the app is hidden. Left click opens the window, the menu offers
/// Open and Exit. Avalonia provides it natively on Windows, macOS and (where the desktop supports it) Linux.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    readonly TrayIcon icon;

    public event Action? Open;
    public event Action? Exit;

    public TrayIconHost(string tip)
    {
        using var s = AssetLoader.Open(new Uri("avares://MasteryTracker.Core/Assets/app.png"));
        var menu = new NativeMenu();
        var open = new NativeMenuItem("Open MW4 Camo Tracker");
        open.Click += (_, _) => Open?.Invoke();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => Exit?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(exit);

        icon = new TrayIcon
        {
            Icon = new WindowIcon(s),
            ToolTipText = tip,
            Menu = menu,
            IsVisible = false,
        };
        icon.Clicked += (_, _) => Open?.Invoke();
    }

    public void Show()
    {
        var icons = TrayIcon.GetIcons(Application.Current!) ?? new TrayIcons();
        if (!icons.Contains(icon)) icons.Add(icon);
        TrayIcon.SetIcons(Application.Current!, icons);
        icon.IsVisible = true;
    }

    public void Dispose()
    {
        icon.IsVisible = false;
        var icons = TrayIcon.GetIcons(Application.Current!);
        icons?.Remove(icon);
        icon.Dispose();
    }
}
