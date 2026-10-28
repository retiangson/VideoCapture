namespace VideoCapture.Services;

public sealed record VideoCrop(int Width, int Height, int X, int Y)
{
    public string Geometry => $"{Width}x{Height}+{X}+{Y}";
    public string BorderGeometry(VideoSourceInfo source)
    {
        int sourceWidth = (int)(source.IsTransposed ? source.Height : source.Width);
        int sourceHeight = (int)(source.IsTransposed ? source.Width : source.Height);
        // Use explicit margins: legacy VLC crop-window coordinates can interpret bottom/right as endpoints.
        return $"{X}+{Y}+{sourceWidth - X - Width}+{sourceHeight - Y - Height}";
    }
}

public static class VideoLayout
{
    public static VideoCrop CenterCrop(VideoSourceInfo source, double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight))
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        int width = checked((int)(source.IsTransposed ? source.Height : source.Width));
        int height = checked((int)(source.IsTransposed ? source.Width : source.Height));
        if (width < 2 || height < 2 || source.DisplayAspectRatio <= 0 || !double.IsFinite(source.DisplayAspectRatio))
            throw new ArgumentException("Invalid video geometry.", nameof(source));
        double targetAspect = viewportWidth / viewportHeight;
        int cropWidth = width, cropHeight = height;
        if (source.DisplayAspectRatio > targetAspect)
            cropWidth = Math.Clamp((int)Math.Round(width * targetAspect / source.DisplayAspectRatio), 2, width);
        else
            cropHeight = Math.Clamp((int)Math.Round(height * source.DisplayAspectRatio / targetAspect), 2, height);
        // Even crop coordinates are compatible with subsampled capture formats.
        cropWidth &= ~1;
        cropHeight &= ~1;
        return new(cropWidth, cropHeight, ((width - cropWidth) / 2) & ~1, ((height - cropHeight) / 2) & ~1);
    }
}
