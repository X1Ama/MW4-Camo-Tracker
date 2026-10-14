using Microsoft.Win32;

namespace MasteryTracker;

/// <summary>"Launch when Windows starts": one value under this user's Run key, nothing system-wide.</summary>
public static class StartupSettings
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "MW4CamoTracker";

    /// <summary>Adds or removes the start-up entry so it matches the setting (and follows the exe if it moved).</summary>
    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch { /* optional convenience */ }
    }
}
