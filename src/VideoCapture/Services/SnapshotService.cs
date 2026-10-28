using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace VideoCapture.Services;

public static class SnapshotService
{
    public static Task<BitmapSource> CaptureAsync(MediaPlayer player) => Task.Run<BitmapSource>(() =>
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"VideoCapture-{Guid.NewGuid():N}.png");
        try
        {
            if (!player.TakeSnapshot(0, temporary, 0, 0))
                throw new InvalidOperationException("No video picture is available to capture yet.");
            using var file = File.OpenRead(temporary);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = file;
            image.EndInit();
            image.Freeze();
            return image;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });

    public static async Task CopyImageAsync(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", png);
        await SetClipboardAsync(data);
    }

    public static Task CopyFileAsync(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The saved recording is no longer available.", path);
        var files = new StringCollection { Path.GetFullPath(path) };
        var data = new DataObject();
        data.SetFileDropList(files);
        return SetClipboardAsync(data);
    }

    public static Task CopyTextAsync(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        return SetClipboardAsync(data);
    }

    private static async Task SetClipboardAsync(DataObject data)
    {
        // Windows clipboard access is exclusive; retry short contention without blocking the UI thread.
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (ExternalException) when (attempt < 4) { await Task.Delay(75); }
        }
    }
}
