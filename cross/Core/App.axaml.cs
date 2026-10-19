using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MasteryTracker;

public partial class App : Application
{
    /// <summary>Set by the desktop program: builds its window (tray, single copy, command-line switches).</summary>
    public static Action<App, IClassicDesktopStyleApplicationLifetime>? DesktopStartup { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            DesktopStartup?.Invoke(this, desktop);
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new MainView();          // phones: the tracker fills the screen

        base.OnFrameworkInitializationCompleted();
    }
}
