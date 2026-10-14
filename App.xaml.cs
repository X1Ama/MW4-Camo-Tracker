using System.Windows;

namespace MasteryTracker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // --demo        open with sample data and no server (nothing is saved)
        // --demo --admin   also open the admin panel straight away
        if (e.Args.Contains("--demo"))
        {
            var main = new MainWindow(new DemoApi(), DemoApi.Owner);
            main.Title += " (demo, nothing is saved)";
            main.Show();
            if (e.Args.Contains("--admin")) main.OpenAdminPanel();
            return;
        }

        new LoginWindow().Show();
    }
}
