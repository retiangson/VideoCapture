using System.Windows.Threading;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private readonly IPreferencesStore _preferencesStore;
    private AppPreferences _preferences = new();
    private bool _preferencesLoaded, _restoringPreferences;
    private readonly DispatcherTimer _preferencesDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Task _preferencesWrite = Task.CompletedTask;

    private async Task RestorePreferencesAsync()
    {
        var loaded = await _preferencesStore.LoadAsync();
        if (_closing) return;
        _preferences = loaded.Preferences.Normalize();
        if (_preferences.TranscriptionMicrophone && _preferences.MonitorCaptureAudio && _preferences.AudioDevice is { } savedAudio)
        {
            try
            {
                if (string.Equals(savedAudio, await MicrophoneRecording.DefaultDeviceNameAsync(), StringComparison.OrdinalIgnoreCase))
                    _preferences = _preferences with { MonitorCaptureAudio = false };
            }
            catch (System.Runtime.InteropServices.COMException) { /* An absent default input must not prevent startup. */ }
        }
        if (_closing) return;
        _restoringPreferences = true;
        try
        {
            PreviewRenderer.SelectedIndex = _preferences.SoftwarePreview ? 1 : 0;
            PictureFit.SelectedIndex = _preferences.PictureFit;
            Volume.Value = _preferences.Volume;
            MonitorCaptureAudio.IsChecked = _preferences.MonitorCaptureAudio;
            MenuOpacity.Value = _preferences.MenuOpacity;
            foreach (string path in _preferences.Playlist) _playlist.Add(path);
            PlaylistList.SelectedIndex = Math.Min(_preferences.PlaylistSelectedIndex, _playlist.Items.Count - 1);
            _selectedVideoName = _preferences.VideoDevice;
            _selectedAudioName = _preferences.AudioDevice;
            _reloadCaptureModes = _wantLivePreview = _selectedVideoName is not null;
            _loadingModes = _reloadCaptureModes;
            _devicePresent = false;
            if (_selectedVideoName is not null)
            {
                Status.Text = "Restoring " + _selectedVideoName + "…";
                VideoPlaceholder.Text = "Waiting for saved device: " + _selectedVideoName;
            }
            if (loaded.Warning is not null) Status.Text = loaded.Warning;
        }
        finally { _restoringPreferences = false; _preferencesLoaded = true; }
        _preferencesDebounce.Tick += async (_, _) =>
        {
            _preferencesDebounce.Stop();
            _preferencesWrite = SavePreferencesAsync();
            await _preferencesWrite;
        };
    }

    private void QueuePreferencesSave()
    {
        if (!_preferencesLoaded || _restoringPreferences || _closing) return;
        _preferencesDebounce.Stop();
        _preferencesDebounce.Start();
    }

    private void RememberCapturePreference()
    {
        if (!_preferencesLoaded || _restoringPreferences || _loadingModes || _selectedVideoName is null) return;
        if (GetSelectedCaptureMode() is { } mode) _preferences.CaptureModes[_selectedVideoName] = mode;
    }

    private async Task SavePreferencesAsync()
    {
        if (!_preferencesLoaded) return;
        var snapshot = _preferences with
        {
            VideoDevice = _selectedVideoName,
            AudioDevice = _selectedAudioName,
            SoftwarePreview = PreviewRenderer.SelectedIndex == 1,
            PictureFit = _fullscreen ? _windowedPictureFit : PictureFit.SelectedIndex,
            Volume = Volume.Value,
            MonitorCaptureAudio = MonitorCaptureAudio.IsChecked == true,
            BrowserTabs = _browser?.SessionReady == true ? _browser.SessionTabs : _preferences.BrowserTabs,
            BrowserSelectedTab = _browser?.SessionReady == true ? _browser.SelectedTabIndex : _preferences.BrowserSelectedTab,
            BrowserOpacity = _browser?.Opacity ?? _preferences.BrowserOpacity,
            MenuOpacity = MenuOpacity.Value,
            Playlist = _playlist.Items.Select(item => item.Path).ToList(),
            PlaylistSelectedIndex = PlaylistList.SelectedIndex,
            SubtitleSpeechEnabled = SpeechEnabled.IsChecked == true,
            SpeechVoice = _subtitleSpeaker is null ? _preferences.SpeechVoice : (SpeechVoice.SelectedItem as VoiceChoice)?.Voice,
            SpeechRate = (int)Math.Round(SpeechRate.Value),
            SpeechVolume = (int)SpeechVolume.Value,
            SpeechRegion = SpeechRegion.SelectedIndex,
            ShowFullscreenProgress = _fullscreenToolbar?.ProgressVisible ?? _preferences.ShowFullscreenProgress,
            CaptureModes = new(_preferences.CaptureModes, StringComparer.Ordinal)
        };
        try { await _preferencesStore.SaveAsync(snapshot); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { Status.Text = "Could not save settings: " + ex.Message; }
    }

    private async Task FlushPreferencesAsync()
    {
        _preferencesDebounce.Stop();
        await _preferencesWrite;
        await SavePreferencesAsync();
    }
}
