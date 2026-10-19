using System.IO;
using Microsoft.Win32;

namespace MasteryTracker;

/// <summary>"Launch when the computer starts": the standard per-user start-up entry for each system. Nothing system-wide.</summary>
public static class Autostart
{
    const string Id = "mw4-camo-tracker";

    /// <summary>Adds or removes the start-up entry so it matches the setting (and follows the program if it moved).</summary>
    public static void Apply(bool enabled)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            if (OperatingSystem.IsLinux())
            {
                var dir = Path.Combine(Home, ".config", "autostart");
                var file = Path.Combine(dir, Id + ".desktop");
                if (!enabled) { if (File.Exists(file)) File.Delete(file); return; }
                Directory.CreateDirectory(dir);
                File.WriteAllText(file,
                    "[Desktop Entry]\nType=Application\nName=MW4 Camo Tracker\nExec=\"" + exe + "\"\n" +
                    "X-GNOME-Autostart-enabled=true\nTerminal=false\n");
            }
            else if (OperatingSystem.IsMacOS())
            {
                var dir = Path.Combine(Home, "Library", "LaunchAgents");
                var file = Path.Combine(dir, "com.x1ama.mw4camotracker.plist");
                if (!enabled) { if (File.Exists(file)) File.Delete(file); return; }
                Directory.CreateDirectory(dir);
                File.WriteAllText(file,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                    "<plist version=\"1.0\"><dict>\n" +
                    "<key>Label</key><string>com.x1ama.mw4camotracker</string>\n" +
                    "<key>ProgramArguments</key><array><string>" + System.Security.SecurityElement.Escape(exe) + "</string></array>\n" +
                    "<key>RunAtLoad</key><true/>\n</dict></plist>\n");
            }
            else if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null) return;
                if (enabled) key.SetValue("MW4CamoTracker", "\"" + exe + "\"");
                else if (key.GetValue("MW4CamoTracker") != null) key.DeleteValue("MW4CamoTracker", false);
            }
        }
        catch { /* optional convenience */ }
    }

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
