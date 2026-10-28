using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Stopwatch _liveFrameClock = new();
    private long _lastLiveFrames;
    private bool _refreshingDevices, _wantLivePreview, _reloadCaptureModes, _devicePresent = true;
    private string? _selectedVideoName, _selectedAudioName;
    private Task? _retiringSession;
    private long _retryNotBefore;
    private bool _enumeratingDevices;
    private bool _audioDevicePresent = true;
    internal Func<IReadOnlyList<CaptureDevice>> ReadVideoDevices { get; set; } = CaptureDevices.Video;
    internal bool IsWaitingForCapture => _wantLivePreview && _session is not { IsLive: true };

    private void InitializeCaptureRecovery()
    {
        _deviceTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        _previewDebounce.Tick += async (_, _) => await ApplyAutomaticPreviewAsync();
    }

    private void VideoDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDevices || _closing) return;
        _selectedVideoName = (VideoDevices.SelectedItem as CaptureDevice)?.Name;
        _devicePresent = _selectedVideoName is not null;
        _reloadCaptureModes = true;
        ClearCaptureOptions();
        _loadingModes = _selectedVideoName is not null;
        _wantLivePreview = _selectedVideoName is not null;
        _retryNotBefore = 0;
        QueuePreferencesSave();
        QueueAutomaticPreview();
        UpdateControls();
    }

    private void CaptureSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _refreshingDevices || _updatingCaptureOptions || _loadingModes || _closing) return;
        _selectedAudioName = (AudioDevices.SelectedItem as CaptureDevice)?.Name;
        if (!ReferenceEquals(sender, AudioDevices)) RememberCapturePreference();
        QueuePreferencesSave();
        if (_selectedVideoName is null || IsVideoRecording) return;
        // An explicit mode/audio change also resumes a manually stopped preview.
        _wantLivePreview = true;
        _retryNotBefore = 0;
        QueueAutomaticPreview();
    }

    private void QueueAutomaticPreview()
    {
        _previewDebounce.Stop();
        if (_wantLivePreview && !_closing) _previewDebounce.Start();
    }

    private void CancelAutomaticPreview()
    {
        _wantLivePreview = false;
        _previewDebounce.Stop();
    }

    private async Task ApplyAutomaticPreviewAsync()
    {
        if (_busy || _closing || Environment.TickCount64 < _retryNotBefore) return;
        _previewDebounce.Stop();
        if (!_wantLivePreview || !_devicePresent || IsVideoRecording) return;
        await RunAsync(async () =>
        {
            _retryNotBefore = Environment.TickCount64 + 3_000;
            if (!await RecreateCaptureSessionAsync()) { QueueAutomaticPreview(); return; }
            if (_reloadCaptureModes && _selectedVideoName is { } name)
            {
                _loadingModes = true;
                SourceDetails.Text = "Reading available capture modes…";
                try
                {
                    var modes = await Task.Run(() => CaptureDevices.Modes(name));
                    SetCaptureModes(modes);
                    _reloadCaptureModes = false;
                }
                finally { _loadingModes = false; }
            }
            if (_wantLivePreview && !_closing) await StartCaptureAsync(false);
        }, showError: false);
    }

    // Native driver teardown must never block WPF. A driver that is still releasing
    // its old graph is not opened concurrently; polling resumes after teardown.
    private async Task<bool> RecreateCaptureSessionAsync()
    {
        await SaveAndClearPlaybackHistoryAsync();
        await PrepareSubtitleSourceAsync(null, false);
        if (_session is { } previous)
        {
            GpuVideo.MediaPlayer = null;
            _session = null;
            _retiringSession = Task.Run(previous.Dispose);
        }
        if (_retiringSession is { } retiring)
        {
            try { await retiring.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException)
            {
                Status.Text = "Waiting for the disconnected capture driver to release…";
                return false;
            }
            finally { if (retiring.IsCompleted) _retiringSession = null; }
        }
        if (_closing) return false;
        await CreateSessionAsync();
        return true;
    }

    private async Task RecoverCaptureAsync(string message) => await RunAsync(async () =>
    {
        string? recording = _session?.RecordingPath;
        Video.Source = null;
        _bitmap = null;
        _videoInfo = null;
        GpuVideo.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Text = message;
        VideoPlaceholder.Visibility = Visibility.Visible;
        _recordingClock.Reset();
        _sourceClock.Reset();
        _liveFrameClock.Reset();
        bool released = await RecreateCaptureSessionAsync();
        if (recording is not null)
        {
            // Never silently resume recording after input loss or overwrite the interrupted file.
            if (released) RememberRecording(recording);
            message += " Recording interrupted; check the saved file.";
        }
        Status.Text = message;
        _retryNotBefore = Environment.TickCount64 + 3_000;
        QueueAutomaticPreview();
    }, showError: false);

    private async Task RefreshDevicesAsync()
    {
        if (_busy || _closing || _enumeratingDevices) return;
        _enumeratingDevices = true;
        bool recover = false;
        try
        {
            var devices = await Task.Run(() => (Video: ReadVideoDevices(), Audio: CaptureDevices.Audio()));
            if (_closing || _busy) return;
            bool wasPresent = _devicePresent;
            bool audioWasPresent = _audioDevicePresent;
            _audioDevicePresent = _selectedAudioName is null or "No audio" || devices.Audio.Any(d => d.Name == _selectedAudioName);
            bool present = _selectedVideoName is not null && devices.Video.Any(d => d.Name == _selectedVideoName);
            _devicePresent = present;
            _refreshingDevices = true;
            try
            {
                // Retain the user's missing selection so the same card can reconnect.
                var video = devices.Video.ToList();
                if (_selectedVideoName is not null && !present) video.Add(new(_selectedVideoName));
                if (!VideoDevices.Items.Cast<CaptureDevice>().SequenceEqual(video))
                {
                    VideoDevices.ItemsSource = video;
                    VideoDevices.SelectedItem = video.FirstOrDefault(d => d.Name == _selectedVideoName);
                }
                var audio = new[] { new CaptureDevice("No audio") }.Concat(devices.Audio).ToList();
                if (_selectedAudioName is not null && audio.All(d => d.Name != _selectedAudioName)) audio.Add(new(_selectedAudioName));
                if (!AudioDevices.Items.Cast<CaptureDevice>().SequenceEqual(audio))
                {
                    AudioDevices.ItemsSource = audio;
                    AudioDevices.SelectedItem = audio.FirstOrDefault(d => d.Name == _selectedAudioName) ?? audio[0];
                }
            }
            finally { _refreshingDevices = false; }
            if (_selectedVideoName is null && _session is not { HasMedia: true })
                Status.Text = "Select a video device to start preview automatically, or open a movie.";
            if (_wantLivePreview && !present && _session is { IsLive: true })
            {
                recover = true;
            }
            else if (_wantLivePreview && present && (!wasPresent || _session is not { IsLive: true }))
                QueueAutomaticPreview();
            else if (_wantLivePreview && present && audioWasPresent != _audioDevicePresent && !IsVideoRecording)
                QueueAutomaticPreview();
            if (recover && _wantLivePreview && !_devicePresent && _session is { IsLive: true })
                await RecoverCaptureAsync("Capture device disconnected. Waiting for " + _selectedVideoName + "…");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing) Status.Text = "Could not refresh capture devices: " + ex.Message; }
        finally { _enumeratingDevices = false; }
        if (!_closing && !_busy && _session is null && _retiringSession is { IsCompleted: true })
            await RunAsync(async () => { await RecreateCaptureSessionAsync(); }, showError: false);
    }
}
