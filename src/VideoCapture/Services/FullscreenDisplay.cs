using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoCapture.Services;

public static class FullscreenDisplay
{
    public static Rect MonitorBounds(Window window)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    public static void CoverMonitor(Window window, Rect pixelBounds)
    {
        if (!SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)pixelBounds.X, (int)pixelBounds.Y,
            (int)pixelBounds.Width, (int)pixelBounds.Height, 0x0020 | 0x0010 | 0x0200))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static Rect WindowBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    public static Size ClientSize(IntPtr handle)
    {
        if (!GetClientRect(handle, out var bounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);
}
