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
            File.WriteAllBytes(FilePath, sealedBytes);
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

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
    }
}