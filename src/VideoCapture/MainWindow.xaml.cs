using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using VideoCapture.Services;
using CaptureMode = VideoCapture.Services.CaptureMode;

namespace VideoCapture;

public partial class MainWindow : Window
{
    private readonly RecordingStore _recordings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Stopwatch _recordingClock = new();
    private readonly Stopwatch _sourceClock = new();
    private readonly Stopwatch _previewClock = new();
    private long _previewFrames;
    private VideoSourceInfo? _videoInfo;
    private bool _loadingModes;
    private long _presentedSoftwareFrames;
    internal long PresentedFrameCount => IsGpuPreview ? _session!.DisplayedFrames : _presentedSoftwareFrames;
    internal bool IsGpuPreview => _session is { UsesSoftwarePreview: false };
    internal VideoSourceInfo? SourceVideoInfo => _videoInfo;
    internal bool TakeVideoSnapshot(string path) => _session?.Player.TakeSnapshot(0, path, 0, 0) ?? false;
    private WriteableBitmap? _bitmap;
    private long _displayedFrame;
    private PlaybackSession? _session;
    private Task<PlaybackSession>? _sessionCreation;
    private bool _busy, _closing, _updatingSeek, _draggingSeek, _fullscreen;
    private WindowState _previousState;
    private Rect _windowedBounds;
    private double _windowedMinWidth, _windowedMinHeight;
    private ResizeMode _windowedResizeMode;
    private int _windowedPictureFit;

    public MainWindow() : this(new PreferencesStore(), new PlaybackHistoryStore()) { }

    internal MainWindow(IPreferencesStore preferencesStore, IPlaybackHistoryStore playbackHistoryStore, RecordingStore? recordings = null)
    {
        _preferencesStore = preferencesStore;
        _playbackHistoryStore = playbackHistoryStore;
        _recordings = recordings ?? new RecordingStore();
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.ApplyDarkCaption(this);
        InitializeCaptureRecovery();
        InitializePlaylist();
        InitializeSubtitleSpeech();
        Seek.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Seek_Down), true);
        Seek.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Seek_Up), true);
        Loaded += OnLoaded;
        Closing += OnClosing;
        _timer.Tick += async (_, _) => await UpdateProgressAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateControls();
        // Let WPF paint before device discovery. Native playback is initialized
        // only when a source is requested, never on the first-render path.
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        if (_closing) return;
        await RestorePreferencesAsync();
        await RestorePlaybackHistoryAsync();
        if (_closing) return;
        UpdateControls();
        RestoreSubtitleSpeechPreferences();
        _deviceTimer.Start();
        _timer.Start();
        CompositionTarget.Rendering += RenderFrame;
        await RefreshDevicesAsync();
    }

    private void RenderFrame(object? sender, EventArgs e) => PresentFrame();

    private async Task CreateSessionAsync()
    {
        bool software = PreviewRenderer.SelectedIndex == 1;
        _sessionCreation = Task.Run(() => new PlaybackSession(software));
        PlaybackSession created;
        try { created = await _sessionCreation; }
        finally { _sessionCreation = null; }
        if (_closing)
        {
            await Task.Run(created.Dispose);
            throw new OperationCanceledException("The app is closing.");
        }
        _session = created;
        _session.Player.Volume = (int)Volume.Value;
        GpuVideo.MediaPlayer = software ? null : _session.Player;
        VideoScale.Visibility = software ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void PreviewRenderer_Changed(object sender, SelectionChangedEventArgs e)
    {
        QueuePreferencesSave();
        if (_session is null) return;
        await RunAsync(async () =>
        {
            GpuVideo.MediaPlayer = null;
            GpuVideo.Visibility = Visibility.Collapsed;
            var previous = _session;
            _session = null;
            await Task.Run(previous.Dispose);
            await CreateSessionAsync();
        });
    }

    private void PresentFrame()
    {
        if (_busy || _closing || _session is null || !_session.UsesSoftwarePreview) return;
        _session.Frames.TryRead(_displayedFrame, (pixels, width, height, pitch, version) =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                Video.Source = _bitmap;
            }
            // Do not stall the UI while WPF's render thread owns the bitmap. Drop this presentation instead.
            if (!_bitmap.TryLock(new Duration(TimeSpan.Zero))) return;
            try
            {
                int targetPitch = _bitmap.BackBufferStride;
                if (targetPitch == pitch) CopyMemory(_bitmap.BackBuffer, pixels, (nuint)(pitch * height));
                else for (int row = 0; row < height; row++)
                    CopyMemory(_bitmap.BackBuffer + row * targetPitch, pixels + row * pitch, (nuint)(width * 4));
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally { _bitmap.Unlock(); }
            _displayedFrame = version;
            _presentedSoftwareFrames++;
            VideoPlaceholder.Visibility = Visibility.Collapsed;
            _sourceClock.Reset();
        });
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, nuint count);

    private void PictureFit_Changed(object sender, SelectionChangedEventArgs e)
    {
        QueuePreferencesSave();
        if (VideoScale is not null) VideoScale.Stretch = PictureFit.SelectedIndex == 1 ? Stretch.UniformToFill : Stretch.Uniform;
        ApplyGpuFit();
    }

    private void Video_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyGpuFit();
    private void ApplyGpuFit()
    {
        if (_session is not { UsesSoftwarePreview: false } session || _busy) return;
        session.Player.Scale = 0;
        var source = _videoInfo ?? session.GetVideoInfo();
        if (session.IsLive && source is not null)
            session.Player.AspectRatio = source.IsTransposed ? $"{source.Height}:{source.Width}" : $"{source.Width}:{source.Height}";
        session.Player.CropGeometry = PictureFit.SelectedIndex == 1 && source is not null && VideoBorder.ActualWidth > 0 && VideoBorder.ActualHeight > 0
            ? VideoLayout.CenterCrop(source, VideoBorder.ActualWidth, VideoBorder.ActualHeight).BorderGeometry(source) : "";
    }

    private async Task RunAsync(Func<Task> action, bool showError = true)
    {
        if (_busy || _closing) return;
        _busy = true;
        UpdateControls();
        try { await action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!_closing)
            {
                ShowCaptureFeedback(ex.Message);
                if (showError) MessageBox.Show(this, ex.Message, "VideoCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally { _busy = false; UpdateControls(); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async Task StartCaptureAsync(bool record)
    {
        if (_session is null) await CreateSessionAsync();
        if (_session is null) throw new InvalidOperationException("The media engine is unavailable.");
        if (VideoDevices.SelectedItem is not CaptureDevice video)
            throw new InvalidOperationException("Select your HDMI video capture device first.");
        var audio = _audioDevicePresent && AudioDevices.SelectedIndex > 0 ? (AudioDevices.SelectedItem as CaptureDevice)?.Name : null;
        string? path = record ? _recordings.CreatePath() : null;
        var session = _session;
        var mode = GetSelectedCaptureMode();
        await SaveAndClearPlaybackHistoryAsync();
        await PrepareSubtitleSourceAsync(null, true);
        ResetPreview("Connecting to " + video.Name + "…");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Run(() => session.OpenCapture(video.Name, audio, path, mode));
        _sourceClock.Restart();
        _lastLiveFrames = 0;
        _liveFrameClock.Restart();
        _recordingClock.Reset();
        if (record) _recordingClock.Start();
        Status.Text = record ? "Recording → " + Path.GetFileName(path) : "Live preview — waiting for capture signal.";
        NowPlayingTitle.Text = video.Name;
        Title = video.Name + " · VideoCapture";
        NowPlayingKind.Text = record ? "LIVE CAPTURE · RECORDING" : "LIVE CAPTURE";
        ApplyGpuFitAfterStart();
    }

    private async void Record_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_localRecording is not null) { await ToggleLocalRecordingAsync(); return; }
        _wantLivePreview = true;
        if (_session?.IsRecording == true)
        {
            string path = _session.RecordingPath!;
            await StartCaptureAsync(false);
            ShowCaptureFeedback(RecordingStore.CompletionMessage(path));
            RememberRecording(path);
        }
        else await StartCaptureAsync(true);
    });

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = VideoFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) await RunPlaylistActionAsync(() => AddPlaylistFilesAsync(dialog.FileNames, true));
    }

    internal Task OpenFileAsync(string path) => RunAsync(() => OpenFileCoreAsync(path));
    private async Task OpenFileCoreAsync(string path)
    {
        if (IsVideoRecording) throw new InvalidOperationException("Stop recording before opening a movie.");
        var file = await Task.Run(() => PlaybackFile.Read(path));
        path = file.Path;
        var item = _playlist.Add(path) ?? throw new InvalidOperationException("The playlist is full. Remove a video before adding more.");
        CancelAutomaticPreview();
        if (_session is null)
        {
            Status.Text = "Preparing playback…";
            await CreateSessionAsync();
        }
        if (_session is null) throw new InvalidOperationException("The media engine is unavailable.");
        var session = _session;
        await SaveAndClearPlaybackHistoryAsync();
        await PrepareSubtitleSourceAsync(path, false);
        ResetPreview("Opening movie…");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Task.Run(() => session.OpenFile(path));
        BeginPlaybackHistory(file);
        _sourceClock.Restart();
        Status.Text = Path.GetFileName(path);
        _playlist.SetCurrent(item);
        PlaylistList.SelectedItem = item;
        LibraryTabs.SelectedItem = PlaylistTab;
        NowPlayingTitle.Text = Path.GetFileNameWithoutExtension(path);
        Title = Path.GetFileName(path) + " · VideoCapture";
        NowPlayingKind.Text = "LOCAL VIDEO";
        UpdatePlaylistControls();
        ApplyGpuFitAfterStart();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        CancelAutomaticPreview();
        await RunAsync(StopAsync);
    }
    private async Task StopAsync()
    {
        await FinishLocalRecordingAsync();
        await SaveAndClearPlaybackHistoryAsync();
        await PrepareSubtitleSourceAsync(null, false);
        var session = _session;
        string? path = session?.RecordingPath;
        bool released = true;
        if (session is { IsLive: true }) released = await RecreateCaptureSessionAsync();
        else if (session is not null) await Task.Run(session.Stop);
        _recordingClock.Reset();
        _sourceClock.Reset();
        GpuVideo.Visibility = Visibility.Collapsed;
        if (Video.Source is null)
        {
            VideoPlaceholder.Text = "Stopped. Open a movie or select a video device.";
            VideoPlaceholder.Visibility = Visibility.Visible;
        }
        ShowCaptureFeedback(!released ? "Stopped; waiting for the capture driver to release. Check any interrupted recording."
            : path is null ? "Stopped." : RecordingStore.CompletionMessage(path));
        if (path is not null && released) RememberRecording(path);
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _closing) return;
        if (_session is { IsLive: false, HasMedia: true } session)
        {
            session.Player.Pause();
            RememberPlaybackPosition();
            _historyWrite = SavePlaybackHistoryAsync();
        }
        else if (_session?.HasMedia != true && PlaylistList.SelectedItem is PlaylistEntry selected) await OpenFileAsync(selected.Path);
    }

    private void UpdateControls()
    {
        bool ready = _preferencesLoaded && !_busy && !_closing;
        bool recording = IsVideoRecording;
        OpenButton.IsEnabled = ready && !recording;
        VideoDevices.IsEnabled = AudioDevices.IsEnabled = RefreshButton.IsEnabled = ready && !recording;
        SetCaptureControlsEnabled(ready && !recording && !_loadingModes);
        PreviewRenderer.IsEnabled = ready && _session?.HasMedia != true;
        RecordButton.IsEnabled = ready && !_loadingModes && (recording ||
            _devicePresent && VideoDevices.SelectedItem is CaptureDevice && (_session is null || _session is { HasMedia: false } || _session is { IsLive: true }));
        RecordLabel.Text = recording ? "Stop recording" : "Record live";
        PauseButton.Content = _session?.Player.IsPlaying == true ? "\uE769" : "\uE768";
        PauseButton.IsEnabled = ready && (_session is { IsLive: false, HasMedia: true } ||
            _session?.HasMedia != true && PlaylistList.SelectedItem is PlaylistEntry);
        StopButton.IsEnabled = !_busy && !_closing && (_session?.HasMedia == true || _wantLivePreview);
        Seek.IsEnabled = ready && _localRecording is null && _session is { IsLive: false } && _session.Player.IsSeekable;
        CopyFrameButton.IsEnabled = ready && CanCaptureFrame;
        CopyTextButton.IsEnabled = ready && CanCaptureFrame;
        ScreenTextButton.IsEnabled = !_busy && !_closing;
        UpdatePlaylistControls();
        UpdateFullscreenToolbar();
    }

    private async Task UpdateProgressAsync()
    {
        if (_busy || _closing) return;
        UpdateFullscreenToolbar();
        if (_session is null) return;
        UpdatePlaybackResume();
        int actualVolume = _session.Player.Volume;
        if (actualVolume >= 0 && actualVolume != (int)Volume.Value) _session.Player.Volume = (int)Volume.Value;
        if (_session.IsLive && _wantLivePreview)
        {
            long frames = _session.DisplayedFrames;
            if (frames != _lastLiveFrames)
            {
                _lastLiveFrames = frames;
                _liveFrameClock.Restart();
            }
            if (_liveFrameClock.Elapsed.TotalSeconds > 8 || _session.Player.State is VLCState.Error or VLCState.Ended)
            {
                await RecoverCaptureAsync("Capture interrupted. Waiting to reconnect…");
                return;
            }
        }
        if (IsGpuPreview && _session.DisplayedFrames > 0)
        {
            _sourceClock.Reset();
            VideoPlaceholder.Visibility = Visibility.Collapsed;
        }
        if (_sourceClock.Elapsed.TotalSeconds > 10)
        {
            await RunAsync(async () =>
            {
                await StopAsync();
                VideoPlaceholder.Text = "No video frames received. Check the device, source signal, and Windows camera permissions.";
                VideoPlaceholder.Visibility = Visibility.Visible;
                Status.Text = VideoPlaceholder.Text;
            });
            return;
        }
        // Poll native state on the UI thread rather than stopping from a VLC callback (which can deadlock).
        if (_session.Player.State == VLCState.Error)
        {
            await RunAsync(async () =>
            {
                await StopAsync();
                Status.Text = "Playback failed. Check devices, signal, Windows permissions, and HDCP. A recording may be incomplete.";
            });
            return;
        }
        if (_session.Player.State == VLCState.Ended)
        {
            if (!_session.IsLive && _playlist.Next is not null)
            {
                await AdvancePlaylistAfterEndAsync();
                return;
            }
            bool noFrames = _sourceClock.IsRunning;
            await RunAsync(async () =>
            {
                await StopAsync();
                if (noFrames)
                {
                    VideoPlaceholder.Text = "The input closed without delivering video. Check the selected device and Windows camera permissions.";
                    VideoPlaceholder.Visibility = Visibility.Visible;
                    Status.Text = VideoPlaceholder.Text;
                }
            });
            return;
        }
        if (_session.IsRecording && _recordingClock.Elapsed.TotalSeconds > 10 &&
            !RecordingStore.HasOutput(_session.RecordingPath!))
        {
            await RunAsync(async () =>
            {
                await StopAsync();
                Status.Text = "Recording failed: no output received. Check the capture signal and available disk space.";
            });
            return;
        }
        UpdateControls();
        if (_videoInfo is null && _session.GetVideoInfo() is { } newInfo)
        {
            _videoInfo = newInfo;
            ApplyGpuFit();
        }
        if (_videoInfo is { } info)
        {
            Video.Width = info.DisplayAspectRatio;
            Video.Height = 1;
            if (_previewClock.Elapsed.TotalSeconds >= 1)
            {
                long frames = IsGpuPreview ? _session.DisplayedFrames : PresentedFrameCount;
                double fps = (frames - _previewFrames) / _previewClock.Elapsed.TotalSeconds;
                SourceDetails.Text = $"Source: {info.Width} × {info.Height} · {info.FramesPerSecond:0.##} fps\nPreview: {Math.Max(0, fps):0.0} fps · {(IsGpuPreview ? "GPU" : "CPU")}";
                _previewFrames = frames;
                _previewClock.Restart();
            }
        }
        RecordingTime.Text = IsVideoRecording ? "● " + RecordingElapsed : "";
        UpdateFullscreenToolbar();
        if (!_session.IsLive)
        {
            PositionText.Text = $"{FormatTime(_session.Player.Time)} / {FormatTime(_session.Player.Length)}";
            if (!_draggingSeek)
            {
                _updatingSeek = true;
                long displayPosition = _requestedPositionMs ?? _resumePositionMs ?? _session.Player.Time;
                Seek.Value = _session.Player.Length > 0 ? Math.Clamp(1000d * displayPosition / _session.Player.Length, 0, 1000) : 0;
                _updatingSeek = false;
            }
        }
        else PositionText.Text = "LIVE";
    }

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromTicks(Math.Clamp(milliseconds, 0, TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond) * TimeSpan.TicksPerMillisecond);
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
    private void ResetPreview(string message)
    {
        Video.Source = null;
        _bitmap = null;
        _videoInfo = null;
        _previewFrames = IsGpuPreview ? 0 : PresentedFrameCount;
        _previewClock.Restart();
        _displayedFrame = _session?.Frames.FrameCount ?? 0;
        GpuVideo.Visibility = IsGpuPreview ? Visibility.Visible : Visibility.Collapsed;
        VideoPlaceholder.Text = message;
        VideoPlaceholder.Visibility = Visibility.Visible;
    }
    private void ApplyGpuFitAfterStart()
    {
        // Apply once the asynchronous lifecycle operation has released the UI state guard.
        Dispatcher.BeginInvoke(ApplyGpuFit, DispatcherPriority.Background);
    }
    private void Seek_Down(object sender, MouseButtonEventArgs e) => _draggingSeek = true;
    private void Seek_Up(object sender, MouseButtonEventArgs e) { _draggingSeek = false; ApplySeek(); }
    private void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updatingSeek && !_draggingSeek) ApplySeek(); }
    private void ApplySeek()
    {
        if (!_busy && _session is { IsLive: false } session && session.Player.IsSeekable)
        {
            SeekPlaybackTo((long)(session.Player.Length * Seek.Value / 1000));
        }
    }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        QueuePreferencesSave();
        if (!_busy && _session is not null) _session.Player.Volume = (int)e.NewValue;
        if (SpeechVolume is not null) UpdateFullscreenToolbar();
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_recordings.DirectoryPath);
            Process.Start(new ProcessStartInfo(_recordings.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        { MessageBox.Show(this, ex.Message, "Cannot open recordings folder"); }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);
    private void SetFullscreen(bool enabled)
    {
        if (enabled == _fullscreen) return;
        var monitor = enabled ? FullscreenDisplay.MonitorBounds(this) : Rect.Empty;
        if (enabled)
        {
            _previousState = WindowState;
            _windowedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _windowedMinWidth = MinWidth;
            _windowedMinHeight = MinHeight;
            _windowedResizeMode = ResizeMode;
            _windowedPictureFit = PictureFit.SelectedIndex;
        }
        _fullscreen = enabled;
        HeaderPanel.Visibility = DevicePanel.Visibility = PlaybackControls.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        RootLayout.Margin = enabled ? new Thickness(0) : new Thickness(24);
        VideoBorder.Margin = enabled ? new Thickness(0) : new Thickness(0, 0, 16, 0);
        VideoBorder.BorderThickness = enabled ? new Thickness(0) : new Thickness(1);
        VideoBorder.CornerRadius = enabled ? new CornerRadius(0) : new CornerRadius(12);
        PlayerLayout.ColumnDefinitions[1].Width = enabled ? new GridLength(0) : new GridLength(300);
        Grid.SetRow(PlayerLayout, enabled ? 0 : 1);
        Grid.SetRowSpan(PlayerLayout, enabled ? 3 : 1);
        if (enabled)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = MinHeight = 0;
            PictureFit.SelectedIndex = 1;
            FullscreenDisplay.CoverMonitor(this, monitor);
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = _windowedResizeMode;
            MinWidth = _windowedMinWidth;
            MinHeight = _windowedMinHeight;
            Left = _windowedBounds.Left;
            Top = _windowedBounds.Top;
            Width = _windowedBounds.Width;
            Height = _windowedBounds.Height;
            WindowState = _previousState;
            PictureFit.SelectedIndex = _windowedPictureFit;
        }
        if (enabled) Dispatcher.BeginInvoke(() =>
        {
            if (!_fullscreen || _closing) return;
            // Reapply after DPI/layout transitions, using the full monitor rather than its work area.
            FullscreenDisplay.CoverMonitor(this, monitor);
            UpdateLayout();
            ApplyGpuFit();
            HideFullscreenToolbar();
        }, DispatcherPriority.Loaded);
        else HideFullscreenToolbar();
    }

    private void Video_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { SetFullscreen(!_fullscreen); e.Handled = true; }
        Focus();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.C) { CopyFrame_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.R) { if (RecordButton.IsEnabled) Record_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.T) { CopyText_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.X) { ScreenText_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.B) { ToggleBrowser(); e.Handled = true; return; }
        }
        if (e.Key == Key.F11) { SetFullscreen(!_fullscreen); e.Handled = true; }
        else if (e.Key == Key.Escape && _fullscreen) { SetFullscreen(false); e.Handled = true; }
        else if (e.Key == Key.Space && e.OriginalSource is not Control)
        { Pause_Click(sender, e); e.Handled = true; }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!_preferencesLoaded || IsVideoRecording || _busy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await RunPlaylistActionAsync(() => AddPlaylistFilesAsync(files, true));
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_busy && _sessionCreation is null) { Status.Text = "Please wait for the current operation to finish, then close again."; return; }
        _closing = true;
        CloseFullscreenToolbar();
        _browser?.Shutdown();
        _timer.Stop();
        _deviceTimer.Stop();
        _previewDebounce.Stop();
        CompositionTarget.Rendering -= RenderFrame;
        UpdateControls();
        Video.Source = null;
        GpuVideo.MediaPlayer = null;
        try
        {
            if (_session is { HasMedia: true, IsLive: false } closingPlayback && closingPlayback.Player.IsPlaying) closingPlayback.Player.SetPause(true);
            try { await FinishLocalRecordingAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { MessageBox.Show(this, "Could not save the video clip: " + ex.Message, "VideoCapture"); }
            RememberPlaybackPosition();
            await FlushPlaybackHistoryAsync();
            await FlushPreferencesAsync();
            await ShutdownSubtitleSpeechAsync();
            if (_cecRemote is not null) await _cecRemote.ShutdownAsync();
            if (_session is { } session) await Task.Run(session.Dispose);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { MessageBox.Show(this, "Could not finalize the media session: " + ex.Message, "VideoCapture"); }
        finally { await _clipExport; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
}
