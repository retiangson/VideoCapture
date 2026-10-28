using System.Windows;
using System.Windows.Input;

namespace VideoCapture;

public partial class FullscreenToolbar : Window
{
    public event EventHandler? RecordRequested, CopyFrameRequested, SaveFrameRequested, CopyRecordingRequested, ExitRequested, FitRequested, CecRequested;
    public event EventHandler? CopyTextRequested, ScreenTextRequested, BrowserRequested, HideRequested;
    public event EventHandler? PreviousRequested, NextRequested, PlayRequested, StopRequested, OpenRequested, PlaylistRequested, MenuOpacityRequested, VolumeRequested;
    public event EventHandler? SpeechRequested;
    public void SetSpeechEnabled(bool enabled) => SpeechButton.Content = enabled ? "Reading on" : "Read aloud";
    private void Speech_Click(object sender, RoutedEventArgs e) => SpeechRequested?.Invoke(this, EventArgs.Empty);
    private bool _updatingLevels;
    public double MenuOpacity => OpacitySlider.Value;
    public double VolumeLevel => MenuVolume.Value;

    public FullscreenToolbar() => InitializeComponent();

    public void Update(bool recordEnabled, bool recording, string elapsed, bool canCapture, bool canCopyRecording, bool actionsEnabled)
    {
        RecordButton.IsEnabled = recordEnabled;
        RecordLabel.Text = recording ? "Stop recording" : "Record";
        Elapsed.Text = elapsed;
        CopyFrameButton.IsEnabled = SaveFrameButton.IsEnabled = canCapture;
        CopyTextButton.IsEnabled = canCapture;
        ScreenTextButton.IsEnabled = actionsEnabled;
        CopyRecordingButton.IsEnabled = canCopyRecording;
    }

    public void SetFeedback(string message) => Feedback.Text = message;
    public void SetFillMode(bool fill) => FitButton.ToolTip = fill ? "Show whole frame" : "Fill screen";
    public void SetTransport(bool canPlay, bool playing, bool canStop, bool canPrevious, bool canNext)
    {
        PlayButton.IsEnabled = canPlay; PlayButton.Content = playing ? "\uE769" : "\uE768";
        StopButton.IsEnabled = canStop; PreviousButton.IsEnabled = canPrevious; NextButton.IsEnabled = canNext;
    }
    public void SetLevels(double opacity, double volume)
    {
        _updatingLevels = true;
        try { Opacity = Math.Clamp(opacity, 0.35, 1); OpacitySlider.Value = Opacity; MenuVolume.Value = volume; }
        finally { _updatingLevels = false; }
    }
    private void MenuOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { Opacity = Math.Clamp(e.NewValue, 0.35, 1); if (!_updatingLevels) MenuOpacityRequested?.Invoke(this, EventArgs.Empty); }
    private void MenuVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!_updatingLevels) VolumeRequested?.Invoke(this, EventArgs.Empty); }
    private void Previous_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);
    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void Play_Click(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void Open_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void Playlist_Click(object sender, RoutedEventArgs e) => PlaylistRequested?.Invoke(this, EventArgs.Empty);
    private void Fit_Click(object sender, RoutedEventArgs e) => FitRequested?.Invoke(this, EventArgs.Empty);
    private void Cec_Click(object sender, RoutedEventArgs e) => CecRequested?.Invoke(this, EventArgs.Empty);
    private void CopyText_Click(object sender, RoutedEventArgs e) => CopyTextRequested?.Invoke(this, EventArgs.Empty);
    private void ScreenText_Click(object sender, RoutedEventArgs e) => ScreenTextRequested?.Invoke(this, EventArgs.Empty);
    private void Browser_Click(object sender, RoutedEventArgs e) => BrowserRequested?.Invoke(this, EventArgs.Empty);
    private void Hide_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);
    private void Grip_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Record_Click(object sender, RoutedEventArgs e) => RecordRequested?.Invoke(this, EventArgs.Empty);
    private void CopyFrame_Click(object sender, RoutedEventArgs e) => CopyFrameRequested?.Invoke(this, EventArgs.Empty);
    private void SaveFrame_Click(object sender, RoutedEventArgs e) => SaveFrameRequested?.Invoke(this, EventArgs.Empty);
    private void CopyRecording_Click(object sender, RoutedEventArgs e) => CopyRecordingRequested?.Invoke(this, EventArgs.Empty);
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void Toolbar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F11) { ExitRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.C) { CopyFrameRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            if (e.Key == Key.R) { RecordRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            if (e.Key == Key.T) { CopyTextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            if (e.Key == Key.X) { ScreenTextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            if (e.Key == Key.B) { BrowserRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        }
    }
}
