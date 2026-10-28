using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace VideoCapture.Services;

public sealed record CaptureDevice(string Name);

/// <summary>Enumerates DirectShow devices without opening a camera or microphone.</summary>
public static class CaptureDevices
{
    public static IReadOnlyList<CaptureDevice> Video() => Enumerate(new("860BB310-5D01-11D0-BD3B-00A0C911CE86"));
    public static IReadOnlyList<CaptureDevice> Audio() => Enumerate(new("33D9A762-90C8-11D0-BD43-00A0C911CE86"));
    public static IReadOnlyList<CaptureMode> Modes(string deviceName)
    {
        IReadOnlyList<CaptureMode>? modes = null;
        Visit(new("860BB310-5D01-11D0-BD3B-00A0C911CE86"), moniker =>
        {
            if (modes is not null || ReadName(moniker) != deviceName) return;
            var iid = new Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770");
            moniker.BindToObject(null!, null!, ref iid, out var filter);
            try { modes = CaptureModeReader.Read(filter); }
            finally { Marshal.ReleaseComObject(filter); }
        });
        return modes ?? throw new InvalidOperationException("The selected video device is no longer connected.");
    }

    private static IReadOnlyList<CaptureDevice> Enumerate(Guid category)
    {
        var devices = new List<CaptureDevice>();
        Visit(category, moniker => { if (ReadName(moniker) is string name) devices.Add(new(name)); });
        return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string? ReadName(IMoniker moniker)
    {
        var iid = typeof(IPropertyBag).GUID;
        moniker.BindToStorage(null!, null!, ref iid, out var bag);
        try { return ((IPropertyBag)bag).Read("FriendlyName", out var value, IntPtr.Zero) == 0 ? value as string : null; }
        finally { Marshal.ReleaseComObject(bag); }
    }

    private static void Visit(Guid category, Action<IMoniker> visitor)
    {
        var enumerator = (ICreateDevEnum)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"), true)!)!;
        IEnumMoniker? monikers = null;
        try
        {
            int result = enumerator.CreateClassEnumerator(ref category, out monikers, 0);
            if (result == 1) return;
            Marshal.ThrowExceptionForHR(result);
            if (monikers is null) return;
            var entry = new IMoniker[1];
            while (monikers.Next(1, entry, IntPtr.Zero) == 0)
            {
                try
                {
                    visitor(entry[0]);
                }
                finally
                {
                    Marshal.ReleaseComObject(entry[0]);
                }
            }
        }
        finally
        {
            if (monikers is not null) Marshal.ReleaseComObject(monikers);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator(ref Guid category, out IEnumMoniker? enumerator, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr errorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
