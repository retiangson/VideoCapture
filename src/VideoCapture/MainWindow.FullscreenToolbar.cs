using System.IO;
using System.Windows;
using System.Runtime.InteropServices;

namespace VideoCapture;

public partial class MainWindow
{
    private FullscreenToolbar? _fullscreenToolbar;
    internal FullscreenToolbar? FloatingToolbar => _fullscreenToolbar;

    private void ShowFullscreenToolbar()
    {
        if (!_fullscreen || _closing) return;
        if (_fullscreenToolbar is null)
        {
            _fullscreenToolbar = new FullscreenToolbar { Owner = this, Topmost = true };
            _fullscreenToolbar.RecordRequested += (_, _) => { if (RecordButton.IsEnabled) Record_Click(this, new RoutedEventArgs()); };
            _fullscreenToolbar.CopyFrameRequested += async (_, _) => await CopyFrameAsync();
            _fullscreenToolbar.SaveFrameRequested += async (_, _) => await SaveFrameAsync();
            _fullscreenToolbar.CopyRecordingRequested += async (_, _) => await CopyRecordingAsync();
            _fullscreenToolbar.ExitRequested += (_, _) => SetFullscreen(false);
            _fullscreenToolbar.FitRequested += (_, _) => PictureFit.SelectedIndex = PictureFit.SelectedIndex == 1 ? 0 : 1;
            _fullscreenToolbar.CecRequested += (_, _) => Cec_Click(this, new RoutedEventArgs());
            _fullscreenToolbar.CopyTextRequested += async (_, _) => await CopyVideoTextAsync();
            _fullscreenToolbar.ScreenTextRequested += async (_, _) => await CopyScreenTextAsync();
            _fullscreenToolbar.BrowserRequested += (_, _) => ToggleBrowser();
            _fullscreenToolbar.HideRequested += (_, _) => HideFullscreenToolbar();
            _fullscreenToolbar.PreviousRequested += (_, _) => Previous_Click(this, new RoutedEventArgs());
            _fullscreenToolbar.NextRequested += (_, _) => Next_Click(this, new RoutedEventArgs());
            _fullscreenToolbar.PlayRequested += (_, _) => Pause_Click(this, new RoutedEventArgs());
            _fullscreenToolbar.StopRequested += (_, _) => Stop_Click(this, new RoutedEventArgs());
            _fullscreenToolbar.OpenRequested += (_, _) => { if (OpenButton.IsEnabled) Open_Click(this, new RoutedEventArgs()); };
            _fullscreenToolbar.PlaylistRequested += (_, _) => { SetFullscreen(false); LibraryTabs.SelectedItem = PlaylistTab; };
            _fullscreenToolbar.MenuOpacityRequested += (_, _) => MenuOpacity.Value = _fullscreenToolbar.MenuOpacity;
            _fullscreenToolbar.VolumeRequested += (_, _) => Volume.Value = _fullscreenToolbar.VolumeLevel;
            _fullscreenToolbar.SpeechRequested += ToggleSubtitleSpeech;
            // The toolbar's close button/Alt+F4 leaves fullscreen rather than closing the recording application.
            _fullscreenToolbar.Closing += (_, e) =>
            {
                if (_closing) return;
                e.Cancel = true;
                SetFullscreen(false);
            };
        }
        UpdateFullscreenToolbar();
        _fullscreenToolbar.Show();
        _fullscreenToolbar.UpdateLayout();
        var pixels = PointToScreen(new Point(0, 0));
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var origin = transform?.Transform(pixels) ?? pixels;
        _fullscreenToolbar.Left = origin.X + Math.Max(0, (ActualWidth - _fullscreenToolbar.ActualWidth) / 2);
        _fullscreenToolbar.Top = origin.Y + Math.Max(12, ActualHeight - _fullscreenToolbar.ActualHeight - 24);
    }

    private void UpdateFullscreenToolbar()
    {
        _fullscreenToolbar?.SetFillMode(PictureFit.SelectedIndex == 1);
        _fullscreenToolbar?.SetSpeechEnabled(SpeechEnabled.IsChecked == true);
        _fullscreenToolbar?.SetLevels(MenuOpacity.Value, Volume.Value);
        _fullscreenToolbar?.SetTransport(PauseButton.IsEnabled, _session?.Player.IsPlaying == true, StopButton.IsEnabled, PreviousButton.IsEnabled, NextButton.IsEnabled);
        if (_fullscreenToolbar is not null && _fullscreen)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
            // Stay above the native GPU overlay, but never pin this toolbar over other applications.
            _fullscreenToolbar.Topmost = processId == (uint)Environment.ProcessId;
        }
        if (_browser is not null)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
            _browser.Topmost = _fullscreen && processId == (uint)Environment.ProcessId;
        }
        _fullscreenToolbar?.Update(RecordButton.IsEnabled, _session?.IsRecording == true,
            FormatTime(_recordingClock.ElapsedMilliseconds), !_busy && !_closing && CanCaptureFrame,
            !_busy && !_closing && _lastRecordingPath is not null && File.Exists(_lastRecordingPath), !_busy && !_closing);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private void HideFullscreenToolbar() => _fullscreenToolbar?.Hide();

    private void CloseFullscreenToolbar()
    {
        _fullscreenToolbar?.Close();
        _fullscreenToolbar = null;
    }
}
