using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MasteryTracker;

/// <summary>
/// A notification-area (system tray) icon, used while the app is started hidden. Left click opens the window,
/// right click offers Open and Exit. Plain Windows shell calls, no extra libraries.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x0400 + 1;      // WM_USER + 1
    const int WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    const uint NIM_ADD = 0, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    readonly HwndSource host;
    IntPtr icon;
    readonly string tip;
    bool added;

    public event Action? Open;
    public event Action? Exit;

    public TrayIcon(string tip)
    {
        this.tip = tip;
        var p = new HwndSourceParameters("MW4CamoTrackerTray") { WindowStyle = 0, ParentWindow = new IntPtr(-3) };  // message-only window
        host = new HwndSource(p);
        host.AddHook(WndProc);

        // the small icon embedded in this exe (the yellow M)
        var exe = Environment.ProcessPath ?? "";
        ExtractIconExW(exe, 0, IntPtr.Zero, out icon, 1);
    }

    public void Show()
    {
        if (added) return;
        var d = Data();
        added = Shell_NotifyIcon(NIM_ADD, ref d);
        SessionStore.Log("tray icon added: " + added + " (icon handle " + (icon != IntPtr.Zero) + ")");
    }

    NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = host.Handle,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = CallbackMessage,
        hIcon = icon,
        szTip = tip,
        szInfo = "",
        szInfoTitle = "",
    };

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP || ev == WM_LBUTTONDBLCLK)
            {
                handled = true;
                Open?.Invoke();
            }
            else if (ev == WM_RBUTTONUP)
            {
                handled = true;
                ShowMenu();
            }
        }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, 0, (UIntPtr)1, "Open MW4 Camo Tracker");
            AppendMenuW(menu, 0, (UIntPtr)2, "Exit");
            GetCursorPos(out var pt);
            SetForegroundWindow(host.Handle);
            int cmd = TrackPopupMenuEx(menu, 0x0100 /*TPM_RETURNCMD*/ | 0x0002 /*TPM_RIGHTBUTTON*/, pt.X, pt.Y, host.Handle, IntPtr.Zero);
            if (cmd == 1) Open?.Invoke();
            else if (cmd == 2) Exit?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (added)
        {
            var d = Data();
            Shell_NotifyIcon(NIM_DELETE, ref d);
            added = false;
        }
        host.Dispose();
        if (icon != IntPtr.Zero) { DestroyIcon(icon); icon = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
}
