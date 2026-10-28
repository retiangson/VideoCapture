using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoCapture.Services;

internal static class WindowAppearance
{
    public static void ApplyDarkCaption(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        int enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int));
        int caption = 0x0019100B, text = 0x00F7EEE8;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 36, ref text, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
