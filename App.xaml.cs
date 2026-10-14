using System.Windows;

namespace MasteryTracker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppIdentity.Apply();
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // --demo        open with sample data and no server (nothing is saved)
        // --demo --admin   also open the admin panel straight away
        // --demo-update   pretend a newer version exists, to preview the update screens (combine with the others)
        if (e.Args.Contains("--demo-update")) Updater.Preview = true;

        // --demo-client   open as a signed-in client (Hoodie) with sample data
        if (e.Args.Contains("--demo-client"))
        {
            var clientWindow = new MainWindow(new DemoApi(), DemoApi.Client);
            clientWindow.Title += " (demo client, nothing is saved)";
            clientWindow.Show();
            return;
        }
        if (e.Args.Contains("--demo"))
        {
            var main = new MainWindow(new DemoApi(), DemoApi.Owner);
            main.Title += " (demo, nothing is saved)";
            main.Show();
            if (e.Args.Contains("--admin")) main.OpenAdminPanel();
            return;
        }

        var window = new MainWindow();
        if (ServerConfig.Load().StartMinimized) window.StartInTray();   // hidden: only a tray icon until opened
        else window.Show();
    }
}
