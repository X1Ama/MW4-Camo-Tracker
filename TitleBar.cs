using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MasteryTracker;

/// <summary>Paints the Windows title bar dark to match the app (Windows 10 dark mode, Windows 11 exact colours).</summary>
public static class TitleBar
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int UseImmersiveDarkMode = 20;
    const int BorderColor = 34;
    const int CaptionColor = 35;
    const int TextColor = 36;

    // COLORREF is 0x00BBGGRR
    const int Background = 0x00100F0D; // #0D0F10
    const int Border = 0x0037332D;     // #2D3337
    const int Text = 0x00F1F0EE;       // #EEF0F1

    static void Set(IntPtr hwnd, int attr, int value) => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            try
            {
                Set(hwnd, UseImmersiveDarkMode, 1);
                Set(hwnd, CaptionColor, Background);
                Set(hwnd, TextColor, Text);
                Set(hwnd, BorderColor, Border);
            }
            catch { /* older Windows: keep the default title bar */ }
        };
    }
}