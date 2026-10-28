using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace VideoCapture.Services;

/// <summary>One aligned RV32 picture shared with VLC. UI reads never wait for a decoder.</summary>
public sealed class VideoFrameBuffer : IDisposable
{
    private readonly Func<VideoSourceInfo?> _readSourceInfo;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IntPtr _allocation, _pixels;
    private int _width, _height, _pitch;
    private long _version;
    private volatile bool _hasFrame;
    public long FrameCount => Interlocked.Read(ref _version);

    public VideoFrameBuffer(Func<VideoSourceInfo?> readSourceInfo) => _readSourceInfo = readSourceInfo;

    public void Attach(MediaPlayer player)
    {
        player.SetVideoFormatCallbacks(Configure, null);
        player.SetVideoCallbacks(Lock, Unlock, Display);
    }

    private uint Configure(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        // VLC may provide coded dimensions (e.g. 1920x1088) rather than visible dimensions.
        // Negotiate the visible raster so alignment padding never becomes part of the picture.
        try
        {
            if (_readSourceInfo() is { } source)
            {
                width = source.IsTransposed ? source.Height : source.Width;
                height = source.IsTransposed ? source.Width : source.Height;
            }
        }
        catch (Exception ex) when (ex is not AccessViolationException) { return 0; }
        // Reject unreasonable or corrupt decoder dimensions before allocating memory.
        if (width == 0 || height == 0 || width > 8192 || height > 8192) return 0;
        _gate.Wait();
        try
        {
            _hasFrame = false;
            if (_allocation != IntPtr.Zero) Marshal.FreeHGlobal(_allocation);
            _allocation = _pixels = IntPtr.Zero;
            _width = (int)width;
            _height = (int)height;
            _pitch = (_width * 4 + 31) & ~31;
            int alignedHeight = (_height + 31) & ~31;
            _allocation = Marshal.AllocHGlobal(checked(_pitch * alignedHeight + 31));
            _pixels = new IntPtr((_allocation.ToInt64() + 31) & ~31L);
            Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
            pitches = (uint)_pitch;
            lines = (uint)alignedHeight;
            return 1;
        }
        catch (OutOfMemoryException) { return 0; } // Exceptions must never cross a native callback.
        finally { _gate.Release(); }
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        _gate.Wait();
        Marshal.WriteIntPtr(planes, _pixels);
        return IntPtr.Zero;
    }

    private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) => _gate.Release();
    private void Display(IntPtr opaque, IntPtr picture)
    {
        _hasFrame = true;
        Interlocked.Increment(ref _version);
    }

    /// <summary>The reader must consume the pointer synchronously; it cannot retain it.</summary>
    public bool TryRead(long previousVersion, Action<IntPtr, int, int, int, long> reader)
    {
        if (FrameCount == previousVersion || !_gate.Wait(0)) return false;
        try
        {
            long version = FrameCount;
            if (_pixels == IntPtr.Zero || !_hasFrame || version == 0) return false;
            reader(_pixels, _width, _height, _pitch, version);
            return true;
        }
        finally { _gate.Release(); }
    }

    // The owner must stop/dispose the media player before releasing callback storage.
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_allocation != IntPtr.Zero) Marshal.FreeHGlobal(_allocation);
            _allocation = _pixels = IntPtr.Zero;
        }
        finally { _gate.Release(); }
        _gate.Dispose();
    }
}
