using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace MasteryTracker;

/// <summary>
/// Gives memory back to Windows while the window sits in the background: collects garbage once, then lets
/// Windows reclaim pages the app is not using. They are paged back in on demand when the window is used again.
/// </summary>
static class MemoryTrim
{
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

    public static void Run()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            using var me = Process.GetCurrentProcess();
            EmptyWorkingSet(me.Handle);
        }
        catch { /* purely an optimisation */ }
    }
}
