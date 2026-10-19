using System.IO;
using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace MasteryTracker;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            App.DesktopStartup = Startup;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            SessionStore.Log("crashed on start: " + ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // ---- what the desktop version does when it starts ---------------------------------------------

    static MainWindow? mainWindow;
    static FileStream? instanceLock;
    const string PipeName = "MW4CamoTracker.Show";

    static void Startup(App app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var args = desktop.Args ?? Array.Empty<string>();

        // --demo        open with sample data and no server (nothing is saved)
        // --demo-client open as a signed-in client with sample data
        // --demo-update pretend a newer version exists, to preview the update screens
        // --phone       open in a phone-sized window, to preview the phone layout
        if (args.Contains("--demo-update")) Updater.Preview = true;
        if (args.Contains("--demo-client"))
        {
            mainWindow = new MainWindow(new MainView(new DemoApi(), DemoApi.Client)) { Title = "MW4 Camo Tracker (demo client, nothing is saved)" };
            PhoneSize(mainWindow);
            desktop.MainWindow = mainWindow;
            mainWindow.Closed += (_, _) => desktop.Shutdown();
            return;
        }
        if (args.Contains("--demo"))
        {
            var view = new MainView(new DemoApi(), DemoApi.Owner);
            mainWindow = new MainWindow(view) { Title = "MW4 Camo Tracker (demo, nothing is saved)" };
            PhoneSize(mainWindow);
            desktop.MainWindow = mainWindow;
            mainWindow.Closed += (_, _) => desktop.Shutdown();
            if (args.Contains("--admin")) view.OpenAdminPanel();
            return;
        }

        void PhoneSize(Window w) { if (args.Contains("--phone")) { w.Width = 390; w.Height = 844; w.WindowStartupLocation = WindowStartupLocation.Manual; w.Position = new PixelPoint(80, 40); } }
        // Only one copy at a time: starting the app again brings the running one to the front instead.
        if (!TakeInstanceLock())
        {
            TellRunningCopyToShow();
            desktop.Shutdown();
            return;
        }

        mainWindow = new MainWindow(new MainView());
        mainWindow.Closed += (_, _) => desktop.Shutdown();
        ListenForSecondLaunch(desktop);
        if (ServerConfig.Load().StartMinimized)
            mainWindow.StartInTray();                 // hidden: only a tray icon until opened
        else
            desktop.MainWindow = mainWindow;          // shown at start-up
    }

    // ---- single instance -------------------------------------------------------------

    static string LockDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasteryCamoTracker");

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

    static void ListenForSecondLaunch(IClassicDesktopStyleApplicationLifetime desktop)
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
                        if (desktop.MainWindow == null) desktop.MainWindow = mainWindow;
                    });
                }
                catch { await Task.Delay(500); }
            }
        });
    }
}
