using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private FloatingBrowser? _browser;
    internal FloatingBrowser? BrowserWindow => _browser;
    internal Task CopyVideoTextAsync() => RunAsync(async () =>
    {
        if (!CanCaptureFrame) throw new InvalidOperationException("Start video playback before reading text.");
        ShowCaptureFeedback("Reading text from the video…");
        await RecognizeAndCopyAsync(await SnapshotService.CaptureAsync(_session!.Player));
    });
    private async Task RecognizeAndCopyAsync(BitmapSource image)
    {
        string text = await TextRecognition.ReadAsync(image);
        if (string.IsNullOrWhiteSpace(text)) { ShowCaptureFeedback("No readable English text found. Clipboard unchanged."); return; }
        await SnapshotService.CopyTextAsync(text);
        ShowCaptureFeedback("Text copied — paste with Ctrl+V. OCR may need corrections.");
    }
    private async void CopyText_Click(object sender, RoutedEventArgs e) => await CopyVideoTextAsync();
    private async void ScreenText_Click(object sender, RoutedEventArgs e) => await CopyScreenTextAsync();
    private Task CopyScreenTextAsync() => RunAsync(async () =>
    {
        bool toolbarVisible = _fullscreenToolbar?.IsVisible == true;
        _fullscreenToolbar?.Hide();
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(150);
            var snapshot = DesktopSnapshot.Capture();
            var selection = new ScreenTextSelection(snapshot.Image, snapshot.Bounds) { Owner = this };
            if (selection.ShowDialog() == true && selection.Selection is { } image)
            {
                ShowCaptureFeedback("Reading selected screen text…");
                await RecognizeAndCopyAsync(image);
            }
            else ShowCaptureFeedback("Screen text selection cancelled. Clipboard unchanged.");
        }
        finally
        {
            if (toolbarVisible && _fullscreen && !_closing) _fullscreenToolbar?.Show();
        }
    });
    private void Browser_Click(object sender, RoutedEventArgs e) => ToggleBrowser();
    internal void ToggleBrowser()
    {
        if (_closing || !_preferencesLoaded) return;
        if (_browser is null)
        {
            _browser = new FloatingBrowser(_preferences.BrowserOpacity) { Owner = this };
            _browser.ScreenTextRequested += async (_, _) => await CopyScreenTextAsync();
            _browser.OpacityPreferenceChanged += (_, _) => QueuePreferencesSave();
        }
        if (_browser.IsVisible) _browser.Hide();
        else { _browser.Topmost = _fullscreen; _browser.Show(); _browser.Activate(); }
    }
    internal void ToggleFullscreenToolbar()
    {
        if (!_fullscreen || _closing) return;
        if (_fullscreenToolbar?.IsVisible == true) HideFullscreenToolbar();
        else ShowFullscreenToolbar();
    }
    private void Video_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (!_fullscreen) return;
        ToggleFullscreenToolbar();
        e.Handled = true;
    }
}
