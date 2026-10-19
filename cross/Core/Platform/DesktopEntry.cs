using System.IO;

namespace MasteryTracker;

/// <summary>
/// Linux: puts MW4 Camo Tracker in the applications menu and the launcher's search, with its icon, by writing a
/// .desktop file in the user's own folder (nothing system-wide). macOS and Windows have their own ways and do not need this.
/// </summary>
public static class DesktopEntry
{
    public static void Ensure()
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // the icon, copied out of the program so the launcher can show it
            var iconDir = Path.Combine(home, ".local", "share", "icons", "hicolor", "128x128", "apps");
            var iconFile = Path.Combine(iconDir, "mw4-camo-tracker.png");
            if (!File.Exists(iconFile))
            {
                Directory.CreateDirectory(iconDir);
                using var src = Avalonia.Platform.AssetLoader.Open(new Uri("avares://MasteryTracker.Core/Assets/app.png"));
                using var dst = File.Create(iconFile);
                src.CopyTo(dst);
            }

            var dir = Path.Combine(home, ".local", "share", "applications");
            var file = Path.Combine(dir, "mw4-camo-tracker.desktop");
            var wanted =
                "[Desktop Entry]\nType=Application\nName=MW4 Camo Tracker\nComment=Camo progress tracker for Modern Warfare 4\n" +
                "Exec=\"" + exe + "\"\nIcon=mw4-camo-tracker\nTerminal=false\nCategories=Game;Utility;\nStartupWMClass=MasteryCamoTracker\n";
            if (File.Exists(file) && File.ReadAllText(file) == wanted) return;     // already right
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, wanted);
        }
        catch { /* purely a convenience */ }
    }
}
