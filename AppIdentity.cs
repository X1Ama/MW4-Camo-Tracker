using System.Runtime.InteropServices;

namespace MasteryTracker;

/// <summary>
/// Gives the app a fixed Windows identity. The taskbar and Start menu group a program's windows and pick its icon
/// by this name; without one Windows guesses from the exe path and can keep showing an old cached icon.
/// </summary>
public static class AppIdentity
{
    public const string Id = "X1Ama.MW4CamoTracker";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void Apply()
    {
        try { SetCurrentProcessExplicitAppUserModelID(Id); }
        catch { /* purely cosmetic: the app works the same without it */ }
    }
}
