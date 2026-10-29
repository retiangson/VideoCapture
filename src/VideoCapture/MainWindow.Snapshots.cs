using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private string? _lastRecordingPath;
    private bool CanCaptureFrame => _session is { HasMedia: true } && _session.DisplayedFrames > 0;

    internal Task CopyFrameAsync() => RunAsync(async () =>
    {
        if (!CanCaptureFrame) throw new InvalidOperationException("Start video playback before copying a frame.");
        var image = await SnapshotService.CaptureAsync(_session!.Player);
        await SnapshotService.CopyImageAsync(image);
        ShowCaptureFeedback("Frame copied to clipboard — paste with Ctrl+V.");
    });

    private async void CopyFrame_Click(object sender, RoutedEventArgs e) => await CopyFrameAsync();

    private async Task SaveFrameAsync() => await RunAsync(async () =>
    {
        if (!CanCaptureFrame) throw new InvalidOperationException("Start video playback before saving a frame.");
        var image = await SnapshotService.CaptureAsync(_session!.Player);
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image|*.png", DefaultExt = ".png", AddExtension = true,
            FileName = $"VideoCapture-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dialog.ShowDialog(this) != true) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(dialog.FileName);
        encoder.Save(file);
        ShowCaptureFeedback("Frame saved → " + Path.GetFileName(dialog.FileName));
    });

    private void RememberRecording(string path)
    {
        if (RecordingStore.HasOutput(path)) _lastRecordingPath = path;
        UpdateFullscreenToolbar();
    }

    private Task CopyRecordingAsync() => RunAsync(async () =>
    {
        if (_lastRecordingPath is null) throw new InvalidOperationException("Finish a recording before copying its file.");
        await SnapshotService.CopyFileAsync(_lastRecordingPath);
        ShowCaptureFeedback("Recording file copied — paste into Explorer or an app that accepts video files.");
    });

    private void ShowCaptureFeedback(string message)
    {
        Status.Text = message;
        _fullscreenToolbar?.SetFeedback(message);
        _browser?.SetVideoFeedback(message);
    }
}
