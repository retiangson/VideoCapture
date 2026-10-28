using System.Globalization;

namespace VideoCapture.Services;

public sealed record CaptureMode(int Width, int Height, long FrameInterval, string Chroma)
{
    public double FramesPerSecond => 10_000_000d / FrameInterval;
    public string Name => $"{Width} × {Height} · {FramesPerSecond:0.##} fps · {Chroma}";
    public string FpsOption => FramesPerSecond.ToString("0.########", CultureInfo.InvariantCulture);
}

public sealed record CaptureResolution(int Width, int Height)
{
    public string Name => $"{Width} × {Height}";
}

public sealed record CaptureFrameRate(double FramesPerSecond)
{
    public string Name => $"{FramesPerSecond:0.##} fps";
}
