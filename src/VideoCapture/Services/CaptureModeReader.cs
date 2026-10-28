using System.Runtime.InteropServices;

namespace VideoCapture.Services;

/// <summary>Reads the capture pin's advertised formats; does not start capture.</summary>
internal static class CaptureModeReader
{
    internal static IReadOnlyList<CaptureMode> Read(object captureFilter)
    {
        var builder = (ICaptureGraphBuilder2)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new("BF87B6E1-8C27-11D0-B3F0-00AA003761C5"), true)!)!;
        IAMStreamConfig? config = null;
        try
        {
            var category = new Guid("FB6C4281-0353-11D1-905F-0000C0CC16BA");
            var mediaType = new Guid("73646976-0000-0010-8000-00AA00389B71");
            var iid = typeof(IAMStreamConfig).GUID;
            Marshal.ThrowExceptionForHR(builder.FindInterface(ref category, ref mediaType, (IBaseFilter)captureFilter, ref iid, out config));
            Marshal.ThrowExceptionForHR(config.GetNumberOfCapabilities(out int count, out int size));
            if (count < 0 || count > 4096 || size < 0 || size > 65536)
                throw new InvalidOperationException("The device returned invalid capture capabilities.");
            var modes = new HashSet<CaptureMode>();
            var caps = Marshal.AllocCoTaskMem(Math.Max(size, 1));
            try
            {
                for (int index = 0; index < count; index++)
                {
                    IntPtr pointer = IntPtr.Zero;
                    try
                    {
                        if (config.GetStreamCaps(index, out pointer, caps) < 0 || pointer == IntPtr.Zero) continue;
                        var type = Marshal.PtrToStructure<MediaType>(pointer);
                        bool info2 = type.FormatType == new Guid("F72A76A0-EB0A-11D0-ACE4-0000C0CC16BA");
                        bool info1 = type.FormatType == new Guid("05589F80-C356-11CE-BF01-00AA0055595A");
                        int bitmapOffset = info2 ? 72 : 48;
                        if ((!info1 && !info2) || type.Format == IntPtr.Zero || type.FormatSize < bitmapOffset + 40) continue;
                        long interval = Marshal.ReadInt64(type.Format, 40);
                        int width = Marshal.ReadInt32(type.Format, bitmapOffset + 4);
                        int height = Math.Abs(Marshal.ReadInt32(type.Format, bitmapOffset + 8));
                        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || interval <= 0) continue;
                        string chroma = GetChroma(type.SubType);
                        // Offer the default, endpoints, and standard rates within the driver's supported interval range.
                        modes.Add(new(width, height, interval, chroma));
                        if (size >= 128)
                        {
                            long minimum = Marshal.ReadInt64(caps, 104);
                            long maximum = Marshal.ReadInt64(caps, 112);
                            if (minimum > 0 && maximum >= minimum)
                            {
                                modes.Add(new(width, height, minimum, chroma));
                                modes.Add(new(width, height, maximum, chroma));
                                foreach (double fps in new[] { 15d, 24, 25, 29.97, 30, 50, 59.94, 60, 120 })
                                {
                                    long intervalForRate = (long)Math.Round(10_000_000d / fps);
                                    if (intervalForRate >= minimum && intervalForRate <= maximum)
                                        modes.Add(new(width, height, intervalForRate, chroma));
                                }
                            }
                        }
                    }
                    finally { FreeMediaType(pointer); }
                }
            }
            finally { Marshal.FreeCoTaskMem(caps); }
            return modes.OrderByDescending(m => (long)m.Width * m.Height)
                .ThenByDescending(m => m.FramesPerSecond).ThenBy(m => m.Chroma).ToArray();
        }
        finally
        {
            if (config is not null) Marshal.ReleaseComObject(config);
            Marshal.ReleaseComObject(builder);
        }
    }

    private static string GetChroma(Guid subtype)
    {
        byte[] bytes = subtype.ToByteArray();
        if (bytes.Take(4).All(b => b >= 32 && b <= 126))
            return System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
        if (subtype == new Guid("E436EB7D-524F-11CE-9F53-0020AF0BA770")) return "RV24";
        if (subtype == new Guid("E436EB7E-524F-11CE-9F53-0020AF0BA770")) return "RV32";
        return "Auto";
    }

    private static void FreeMediaType(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return;
        var type = Marshal.PtrToStructure<MediaType>(pointer);
        if (type.Format != IntPtr.Zero) Marshal.FreeCoTaskMem(type.Format);
        if (type.Unknown != IntPtr.Zero) Marshal.Release(type.Unknown);
        Marshal.FreeCoTaskMem(pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MediaType
    {
        public Guid MajorType, SubType;
        [MarshalAs(UnmanagedType.Bool)] public bool FixedSize;
        [MarshalAs(UnmanagedType.Bool)] public bool TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr Format;
    }

    [ComImport, Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter { }
    [ComImport, Guid("93E5A4E0-2D50-11D2-ABFA-00A0C9C6E38D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureGraphBuilder2
    {
        [PreserveSig] int SetFiltergraph(IntPtr graph);
        [PreserveSig] int GetFiltergraph(out IntPtr graph);
        [PreserveSig] int SetOutputFileName(ref Guid type, [MarshalAs(UnmanagedType.LPWStr)] string file, out IntPtr filter, out IntPtr sink);
        [PreserveSig] int FindInterface(ref Guid category, ref Guid type, IBaseFilter filter, ref Guid iid, out IAMStreamConfig config);
    }
    [ComImport, Guid("C6E13340-30AC-11D0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr type);
        [PreserveSig] int GetFormat(out IntPtr type);
        [PreserveSig] int GetNumberOfCapabilities(out int count, out int size);
        [PreserveSig] int GetStreamCaps(int index, out IntPtr type, IntPtr caps);
    }
}
