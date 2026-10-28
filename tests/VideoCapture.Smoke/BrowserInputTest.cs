using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using VideoCapture;

internal static class BrowserInputTest
{
    internal static async Task RunAsync(FloatingBrowser window, WebView2CompositionControl web)
    {
        GetCursorPos(out var originalCursor);
        try
        {
            window.Activate();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            uint currentThread = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
            try { SetForegroundWindow(handle); }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            await Task.Delay(200);
            await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('text').value=''; document.title='ready';");
            await ClickAsync("text");
            foreach (char letter in "native typing")
            {
                RequireForeground();
                Send(new Input { Type = 1, Data = new InputData { Keyboard = new KeyInput { Scan = letter, Flags = 4 } } },
                    new Input { Type = 1, Data = new InputData { Keyboard = new KeyInput { Scan = letter, Flags = 6 } } });
            }
            await ClickAsync("button");
            if (await web.CoreWebView2.ExecuteScriptAsync("document.title") != "\"clicked\"" ||
                await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('text').value") != "\"native typing\"")
                throw new Exception("Native browser mouse/keyboard input failed at reduced opacity.");
            Console.WriteLine("Native mouse clicking and Unicode keyboard typing passed at 55% opacity.");
        }
        finally { SetCursorPos(originalCursor.X, originalCursor.Y); }

        void RequireForeground()
        {
            if (GetForegroundWindow() != new WindowInteropHelper(window).Handle)
            {
                IntPtr foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out uint pid);
                throw new Exception($"Browser input test lost foreground focus; refusing input. foreground={foreground}, target={new WindowInteropHelper(window).Handle}, foregroundProcess={pid}, testProcess={Environment.ProcessId}, active={window.IsActive}");
            }
        }
        async Task ClickAsync(string id)
        {
            string result = await web.CoreWebView2.ExecuteScriptAsync($"(()=>{{let r=document.getElementById('{id}').getBoundingClientRect();return [r.x+r.width/2,r.y+r.height/2]}})()");
            var coords = JsonSerializer.Deserialize<double[]>(result)!;
            Point screen = web.PointToScreen(new Point(coords[0], coords[1]));
            RequireForeground();
            SetCursorPos((int)screen.X, (int)screen.Y);
            Send(new Input { Data = new InputData { Mouse = new MouseInput { Flags = 2 } } },
                new Input { Data = new InputData { Mouse = new MouseInput { Flags = 4 } } });
            await Task.Delay(200);
        }
    }
    private static void Send(params Input[] inputs)
    { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new Exception("SendInput failed."); }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
