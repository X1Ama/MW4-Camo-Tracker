using System.IO;
using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace MasteryTracker;

public partial class App : Application
{
    // held for the life of the process so the "already running" check stays valid
    static FileStream? instanceLock;
    MainWindow? mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var args = desktop.Args ?? Array.Empty<string>();

            // --demo        open with sample data and no server (nothing is saved)
            // --demo-client open as a signed-in client with sample data
            // --demo-update pretend a newer version exists, to preview the update screens
            if (args.Contains("--demo-update")) Updater.Preview = true;
            if (args.Contains("--demo-client"))
            {
                mainWindow = new MainWindow(new DemoApi(), DemoApi.Client) { Title = "MW4 Camo Tracker (demo client, nothing is saved)" };
                desktop.MainWindow = mainWindow;
                mainWindow.Closed += (_, _) => desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (args.Contains("--demo"))
            {
                mainWindow = new MainWindow(new DemoApi(), DemoApi.Owner) { Title = "MW4 Camo Tracker (demo, nothing is saved)" };
                desktop.MainWindow = mainWindow;
                mainWindow.Closed += (_, _) => desktop.Shutdown();
                if (args.Contains("--admin")) mainWindow.OpenAdminPanel();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // Only one copy at a time: starting the app again brings the running one to the front instead.
            if (!TakeInstanceLock())
            {
                TellRunningCopyToShow();
                desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            mainWindow = new MainWindow();
            mainWindow.Closed += (_, _) => desktop.Shutdown();
            ListenForSecondLaunch();
            if (ServerConfig.Load().StartMinimized)
                mainWindow.StartInTray();                 // hidden: only a tray icon until opened
            else
                desktop.MainWindow = mainWindow;          // shown at start-up
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ---- single instance -------------------------------------------------------------

    static string LockDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasteryCamoTracker");

    const string PipeName = "MW4CamoTracker.Show";

    static bool TakeInstanceLock()
    {
        try
        {
            Directory.CreateDirectory(LockDir);
            // an exclusive lock on a small file: the system drops it when the program ends, even after a crash
            instanceLock = new FileStream(Path.Combine(LockDir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch { return true; }          // cannot tell: better to open than to refuse
    }

    static void TellRunningCopyToShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            client.WriteByte(1);
        }
        catch { /* the first copy is still starting: nothing to wake yet */ }
    }

    void ListenForSecondLaunch()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    server.ReadByte();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (mainWindow == null) return;
                        mainWindow.BringToFront();
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow == null) d.MainWindow = mainWindow;
                    });
                }
                catch { await Task.Delay(500); }
            }
        });
    }
}
