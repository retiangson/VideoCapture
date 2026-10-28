using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private FloatingBrowser? _browser;
    internal Func<double, FloatingBrowser> BrowserFactory { get; set; } = opacity => new FloatingBrowser(opacity);
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
            _browser = BrowserFactory(_preferences.BrowserOpacity);
            _browser.RestoreTabs(_preferences.BrowserTabs, _preferences.BrowserSelectedTab);
            _browser.SessionChanged += (_, _) => QueuePreferencesSave();
            _browser.Owner = this;
            _browser.SetMicrophoneSource(_preferences.TranscriptionMicrophone);
            _browser.TranscriptSourceRequested += (_, _) =>
            {
                _preferences = _preferences with { TranscriptionMicrophone = _browser.MicrophoneSelected };
                QueuePreferencesSave();
                UpdateControls();
            };
            _browser.ScreenTextRequested += async (_, _) => await CopyScreenTextAsync();
            _browser.CopyVideoFrameRequested += async (_, _) => await CopyFrameAsync();
            _browser.CopyVideoTextRequested += async (_, _) => await CopyVideoTextAsync();
            _browser.RecordVideoRequested += async (_, _) => await RunAsync(ToggleVideoRecordingCoreAsync);
            _browser.AudioTextRequested += async (_, _) => await RunAsync(ToggleAudioCaptureAsync);
            _browser.CancelAudioRequested += async (_, _) => await RunAsync(CancelAudioCaptureAsync);
            _browser.CopyTranscriptRequested += async (_, _) => await CopyTranscriptAsync();
            _browser.TranscriptLanguageRequested += (_, _) =>
            {
                _preferences = _preferences with { TranscriptionLanguage = _browser.TranscriptLanguageId };
                QueuePreferencesSave();
            };
            _transcriptionLanguages = InitializeTranscriptionLanguagesAsync();
            _browser.OpacityPreferenceChanged += (_, _) => QueuePreferencesSave();
        }
        UpdateFullscreenToolbar();
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
        ToggleBrowser();
        e.Handled = true;
    }
    private CancellationTokenSource? _videoMenuHoldCancellation;
    private UIElement? _videoMenuPressTarget;
    private Point _videoMenuPressOrigin;
    private bool _videoMenuHoldShown;

    private void BeginVideoMenuPress(UIElement target, Point position)
    {
        CancelVideoMenuPress();
        if (!target.CaptureMouse()) return;
        _videoMenuPressTarget = target;
        _videoMenuPressOrigin = position;
        _videoMenuHoldCancellation = new();
        _ = ShowHeldVideoMenuAsync(_videoMenuHoldCancellation.Token);
    }
    private async Task ShowHeldVideoMenuAsync(CancellationToken token)
    {
        try { await Task.Delay(450, token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested || !_fullscreen || _closing || _videoMenuPressTarget is null) return;
        _videoMenuHoldShown = true;
        ShowFullscreenToolbar();
    }
    private void CancelVideoMenuPress()
    {
        _videoMenuHoldCancellation?.Cancel();
        _videoMenuHoldCancellation?.Dispose();
        _videoMenuHoldCancellation = null;
        var target = _videoMenuPressTarget;
        _videoMenuPressTarget = null;
        _videoMenuHoldShown = false;
        if (target?.IsMouseCaptured == true) target.ReleaseMouseCapture();
    }
    private void Video_MenuPressLostCapture(object sender, MouseEventArgs e)
    { if (_videoMenuPressTarget is not null) CancelVideoMenuPress(); }
    private void Video_MenuPressMove(object sender, MouseEventArgs e)
    {
        if (_videoMenuPressTarget is not { } target) return;
        var position = e.GetPosition(target);
        if (Math.Abs(position.X - _videoMenuPressOrigin.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(position.Y - _videoMenuPressOrigin.Y) > SystemParameters.MinimumVerticalDragDistance)
            CancelVideoMenuPress();
    }
    private void Video_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (!_fullscreen || e.ClickCount != 1) return;
        bool active = _videoMenuPressTarget is not null, held = _videoMenuHoldShown;
        CancelVideoMenuPress();
        if (active && !held) HideFullscreenToolbar();
        e.Handled = true;
    }
    private void Video_OpacityWheel(object sender, MouseWheelEventArgs e)
    {
        if (_browser is not { IsVisible: true })
        {
            if (_fullscreenToolbar?.IsVisible != true) return;
            MenuOpacity.Value = Math.Clamp(MenuOpacity.Value + e.Delta / 120d * .05, .35, 1);
        }
        else _browser.AdjustOpacity(e.Delta);
        e.Handled = true;
    }
}
