using System.IO;

namespace MasteryTracker;

/// <summary>
/// "Remember me" on Linux and macOS: keeps the sign-in token (never the password) in a file that only this user
/// account can read (permissions 600, like an SSH key or the GitHub command line's token file).
/// Signing out deletes it. Same calls as the Windows app, which uses Windows' own encryption instead.
/// </summary>
public static class SessionStore
{
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasteryCamoTracker");

    static string FilePath => Path.Combine(Dir, "session.txt");

    static void Private(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* best effort */ }
    }

    public static void Save(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        try
        {
            Directory.CreateDirectory(Dir);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, refreshToken);
            Private(tmp);
            File.Move(tmp, FilePath, true);          // never leaves a half-written file behind
        }
        catch { /* remembering is optional */ }
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var text = File.ReadAllText(FilePath).Trim();
            return text.Length == 0 ? null : text;
        }
        catch { return null; }
    }

    /// <summary>One line per remembered-sign-in attempt, so a failure can be explained afterwards.</summary>
    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var p = Path.Combine(Dir, "remember.log");
            // keep the log small: start over once it passes 200 KB
            if (File.Exists(p) && new FileInfo(p).Length > 200_000) File.Delete(p);
            File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        catch { }
    }

    // Passwords an admin set for people, so they can be shown again later: a file only this user account can read.
    static string KnownPath => Path.Combine(Dir, "known.json");

    public static Dictionary<string, string> LoadKnown()
    {
        try
        {
            if (!File.Exists(KnownPath)) return new();
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(KnownPath)) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveKnown(Dictionary<string, string> known)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = KnownPath + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(known));
            Private(tmp);
            File.Move(tmp, KnownPath, true);
        }
        catch { }
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
        try { File.Delete(KnownPath); } catch { }
    }
}
