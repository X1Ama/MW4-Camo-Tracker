using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MasteryTracker;

/// <summary>
/// "Remember me": keeps the sign-in token (never the password) on this PC, encrypted with the Windows account
/// of the person using it, so only that Windows user can read it. Signing out deletes it.
/// </summary>
public static class SessionStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasteryCamoTracker", "session.dat");

    public static void Save(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), null, DataProtectionScope.CurrentUser);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, sealedBytes);
            File.Move(tmp, FilePath, true);          // never leaves a half-written file behind
        }
        catch { /* remembering is optional */ }
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    /// <summary>One line per remembered-sign-in attempt, so a failure can be explained afterwards.</summary>
    public static void Log(string line)
    {
        try
        {
            var p = Path.Combine(Path.GetDirectoryName(FilePath)!, "remember.log");
            File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        catch { }
    }

    // Passwords an admin set for people, so they can be shown again later. Same protection as the sign-in token:
    // encrypted with the Windows account of this user, deleted when signing out.
    static string KnownPath => Path.Combine(Path.GetDirectoryName(FilePath)!, "known.dat");

    public static Dictionary<string, string> LoadKnown()
    {
        try
        {
            if (!File.Exists(KnownPath)) return new();
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(KnownPath), null, DataProtectionScope.CurrentUser);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveKnown(Dictionary<string, string> known)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KnownPath)!);
            var sealedBytes = ProtectedData.Protect(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(known), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(KnownPath, sealedBytes);
        }
        catch { }
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
        try { File.Delete(KnownPath); } catch { }
    }
}