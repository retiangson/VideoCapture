using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private enum SubtitleMode { None, Loading, Embedded, Ocr }
    private sealed record SpeechTrackChoice(string Name, int? Stream = null, bool ForceOcr = false)
    { public override string ToString() => Name; }
    private sealed record VoiceChoice(string Name, string? Voice = null)
    { public override string ToString() => Name; }
    private readonly DispatcherTimer _subtitleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly EmbeddedSubtitles _embeddedSubtitles = new();
    private readonly SubtitleOcr _subtitleOcr = new();
    private readonly Stopwatch _ocrInterval = Stopwatch.StartNew(), _subtitlePositionClock = Stopwatch.StartNew();
    private ISubtitleVoiceSpeaker? _subtitleSpeaker;
    private Task<ISubtitleVoiceSpeaker>? _speakerCreation;
    internal Func<Task<ISubtitleVoiceSpeaker>> SubtitleSpeakerFactory { get; set; } = () => Task.Run<ISubtitleVoiceSpeaker>(() => new WindowsSubtitleSpeaker());
    private SubtitleSpeechController? _subtitleSpeech;
    private SubtitleTimeline? _subtitleTimeline;
    private IReadOnlyList<EmbeddedSubtitleTrack>? _embeddedTracks;
    private CancellationTokenSource? _subtitleLoading;
    private Task _subtitleLoadTask = Task.CompletedTask;
    private readonly List<Task> _retiredSubtitleLoads = [];
    private CancellationTokenSource _subtitleFrames = new();
    private Task? _subtitleFrameTask;
    private Task<System.Windows.Media.Imaging.BitmapSource>? _subtitleSnapshot;
    private string? _subtitleFile;
    private bool _subtitleLive, _speechUiUpdating;
    private int _subtitleVersion, _subtitleEpoch, _visibleSubtitleOrdinal = -1, _appliedSubtitleOrdinal = -1;
    private long _lastSubtitlePosition = -1;
    private SubtitleMode _subtitleMode;

    private void InitializeSubtitleSpeech()
    {
        SetSpeechTrackChoices();
        SpeechVoice.ItemsSource = new[] { new VoiceChoice("Windows default voice") };
        SpeechVoice.SelectedIndex = 0;
        _subtitleTimer.Tick += async (_, _) => await TickSubtitleSpeechAsync();
        _subtitleTimer.Start();
    }
    private void SetSpeechTrackChoices()
    {
        var previous = SpeechTrack.SelectedItem as SpeechTrackChoice;
        _speechUiUpdating = true;
        try
        {
            var choices = new List<SpeechTrackChoice> { new("Auto · embedded text, then OCR"), new("OCR · visible subtitles", ForceOcr: true) };
            if (_embeddedTracks is not null) choices.AddRange(_embeddedTracks.Select(t => new SpeechTrackChoice(t.Name, t.StreamIndex)));
            SpeechTrack.ItemsSource = choices;
            SpeechTrack.SelectedItem = choices.FirstOrDefault(c => previous is not null && c.Stream == previous.Stream && c.ForceOcr == previous.ForceOcr) ?? choices[0];
        }
        finally { _speechUiUpdating = false; }
    }
    private async Task EnsureSubtitleSpeakerAsync()
    {
        if (_subtitleSpeaker is not null) return;
        _speakerCreation ??= SubtitleSpeakerFactory();
        var speaker = await _speakerCreation;
        if (_closing || _subtitleSpeaker is not null) return;
        _subtitleSpeaker = speaker;
        speaker.Failed += OnSubtitleSpeechFailed;
        _subtitleSpeech = new(speaker);
        _speechUiUpdating = true;
        try
        {
            var voices = new[] { new VoiceChoice("Windows default voice") }.Concat(speaker.Voices.Select(v => new VoiceChoice(v, v))).ToArray();
            SpeechVoice.ItemsSource = voices;
            SpeechVoice.SelectedItem = voices.FirstOrDefault(v => v.Voice == _preferences.SpeechVoice) ?? voices[0];
            SpeechVoice.IsEnabled = true;
        }
        finally { _speechUiUpdating = false; }
        ConfigureSubtitleVoice();
    }
    private void OnSubtitleSpeechFailed(string message) => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        SpeechEnabled.IsChecked = false;
        SpeechMode.Text = "Speech stopped: " + message;
    });
    private void ConfigureSubtitleVoice()
    {
        _subtitleSpeaker?.Configure((SpeechVoice.SelectedItem as VoiceChoice)?.Voice, (int)Math.Round(SpeechRate.Value), (int)SpeechVolume.Value);
        _subtitleSpeech?.Reset();
    }
    private async void SpeechToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_preferencesLoaded || _speechUiUpdating || _closing) return;
        QueuePreferencesSave();
        if (SpeechEnabled.IsChecked != true)
        {
            CancelSubtitleLoad();
            InvalidateSubtitleSpeech();
            SpeechMode.Text = "Subtitle reading is off.";
            return;
        }
        SpeechMode.Text = "Preparing Windows voice…";
        try
        {
            await EnsureSubtitleSpeakerAsync();
            if (!_closing && SpeechEnabled.IsChecked == true) BeginSubtitleLoad();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_closing) return;
            SpeechEnabled.IsChecked = false;
            SpeechMode.Text = "Speech unavailable: " + ex.Message;
        }
    }
    private void SpeechTrack_Changed(object sender, SelectionChangedEventArgs e)
    { if (_preferencesLoaded && !_speechUiUpdating && !_closing && SpeechEnabled.IsChecked == true && _subtitleSpeaker is not null) BeginSubtitleLoad(); }
    private void SpeechVoice_Changed(object sender, SelectionChangedEventArgs e) => SpeechConfigurationChanged();
    private void SpeechSetting_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SpeechConfigurationChanged();
    private void SpeechConfigurationChanged()
    {
        if (!_preferencesLoaded || _speechUiUpdating || _closing) return;
        InvalidateSubtitleSpeech();
        try { ConfigureSubtitleVoice(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { SpeechMode.Text = "Voice setting failed: " + ex.Message; }
        QueuePreferencesSave();
    }
    private void InvalidateSubtitleSpeech()
    {
        ++_subtitleEpoch;
        _subtitleFrames.Cancel(); _subtitleFrames.Dispose(); _subtitleFrames = new();
        _subtitleSpeech?.Reset();
        _lastSubtitlePosition = -1;
    }
    private void CancelSubtitleLoad()
    {
        ++_subtitleVersion;
        if (_subtitleLoading is not { } old) return;
        _subtitleLoading = null;
        old.Cancel();
        _retiredSubtitleLoads.RemoveAll(task => task.IsCompleted);
        _retiredSubtitleLoads.Add(RetireSubtitleLoadAsync(_subtitleLoadTask, old));
    }
    private static async Task RetireSubtitleLoadAsync(Task task, CancellationTokenSource cancellation)
    { try { await task; } catch (OperationCanceledException) { } finally { cancellation.Dispose(); } }
    private async Task PrepareSubtitleSourceAsync(string? file, bool live)
    {
        CancelSubtitleLoad();
        InvalidateSubtitleSpeech();
        _subtitleFile = file; _subtitleLive = live;
        _subtitleMode = SubtitleMode.None; _subtitleTimeline = null; _embeddedTracks = null;
        _visibleSubtitleOrdinal = _appliedSubtitleOrdinal = -1;
        SetSpeechTrackChoices();
        if (_subtitleSnapshot is { } snapshot)
            try { await snapshot; } catch (Exception ex) when (ex is not OutOfMemoryException) { }
        if (!_closing && SpeechEnabled.IsChecked == true && _subtitleSpeaker is not null) BeginSubtitleLoad();
    }
    private void BeginSubtitleLoad()
    {
        CancelSubtitleLoad(); InvalidateSubtitleSpeech();
        _subtitleTimeline = null; _subtitleMode = SubtitleMode.Loading;
        _visibleSubtitleOrdinal = _appliedSubtitleOrdinal = -1;
        _subtitleLoading = new();
        _subtitleLoadTask = LoadSubtitleSourceAsync(_subtitleVersion, _subtitleFile, _subtitleLoading.Token);
    }
    private async Task LoadSubtitleSourceAsync(int version, string? path, CancellationToken token)
    {
        try
        {
            var choice = SpeechTrack.SelectedItem as SpeechTrackChoice;
            if (_subtitleLive || choice?.ForceOcr == true) { UseSubtitleOcr("OCR · visible subtitle region"); return; }
            if (path is null) { _subtitleMode = SubtitleMode.None; SpeechMode.Text = "Start a video to read subtitles."; return; }
            SpeechMode.Text = "Finding embedded subtitle tracks…";
            if (_embeddedTracks is null)
            {
                var tracks = await _embeddedSubtitles.ProbeAsync(path, token);
                if (_closing || version != _subtitleVersion) return;
                _embeddedTracks = tracks;
                SetSpeechTrackChoices();
            }
            var track = choice?.Stream is { } index ? _embeddedTracks.FirstOrDefault(t => t.StreamIndex == index)
                : EmbeddedSubtitles.Preferred(_embeddedTracks) ?? _embeddedTracks.OrderByDescending(t => t.IsDefault).FirstOrDefault();
            _visibleSubtitleOrdinal = track?.Ordinal ?? -1;
            if (track?.IsText != true) { UseSubtitleOcr(track is null ? "No embedded text · OCR" : "Image subtitles · OCR"); return; }
            SpeechMode.Text = "Loading embedded text · " + track.Name;
            var timeline = await _embeddedSubtitles.ExtractAsync(path, track.StreamIndex, token);
            if (_closing || version != _subtitleVersion) return;
            if (timeline.Count == 0) { UseSubtitleOcr("No readable subtitle cues · OCR"); return; }
            _subtitleTimeline = timeline; _subtitleMode = SubtitleMode.Embedded;
            SpeechMode.Text = "Embedded text · " + track.Name;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing && version == _subtitleVersion) UseSubtitleOcr("OCR fallback · " + ex.Message); }
    }
    private void UseSubtitleOcr(string message)
    { _subtitleMode = SubtitleMode.Ocr; SpeechMode.Text = message; _ocrInterval.Restart(); }

    private async Task TickSubtitleSpeechAsync()
    {
        if (_closing || _busy || SpeechEnabled.IsChecked != true || _subtitleSpeech is null || _session is not { HasMedia: true } session) return;
        try
        {
            bool playing = session.Player.IsPlaying;
            _subtitleSpeech.SetPaused(!playing);
            if (!playing) return;
            long position = session.Player.Time;
            double elapsed = _subtitlePositionClock.Elapsed.TotalMilliseconds;
            _subtitlePositionClock.Restart();
            if (!session.IsLive && _lastSubtitlePosition >= 0 && (position < _lastSubtitlePosition - 300 || position - _lastSubtitlePosition > Math.Max(1500, elapsed + 700)))
                InvalidateSubtitleSpeech();
            _lastSubtitlePosition = position;
            if (_visibleSubtitleOrdinal >= 0 && _appliedSubtitleOrdinal != _visibleSubtitleOrdinal)
            {
                var descriptions = session.Player.SpuDescription.Where(t => t.Id >= 0).ToArray();
                if (_visibleSubtitleOrdinal < descriptions.Length && session.Player.SetSpu(descriptions[_visibleSubtitleOrdinal].Id)) _appliedSubtitleOrdinal = _visibleSubtitleOrdinal;
            }
            if (_subtitleMode == SubtitleMode.Embedded)
            {
                var cue = _subtitleTimeline?.At(Math.Max(0, position - session.Player.SpuDelay / 1000));
                _subtitleSpeech.ReadEmbedded(cue);
                SpeechCurrent.Text = cue?.Text ?? "Waiting for the next subtitle…";
            }
            else if (_subtitleMode == SubtitleMode.Ocr && session.DisplayedFrames > 0 && _ocrInterval.ElapsedMilliseconds >= 850 && _subtitleFrameTask?.IsCompleted != false)
            {
                _ocrInterval.Restart();
                _subtitleFrameTask = ReadSubtitleFrameAsync(session, _subtitleVersion, _subtitleEpoch, position, _subtitleFrames.Token);
                await _subtitleFrameTask;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing) SpeechMode.Text = "Subtitle reader: " + ex.Message; }
    }
    private async Task ReadSubtitleFrameAsync(PlaybackSession session, int version, int epoch, long position, CancellationToken token)
    {
        try
        {
            var snapshot = SnapshotService.CaptureAsync(session.Player);
            _subtitleSnapshot = snapshot;
            System.Windows.Media.Imaging.BitmapSource image;
            try { image = await snapshot; }
            finally { if (ReferenceEquals(_subtitleSnapshot, snapshot)) _subtitleSnapshot = null; }
            token.ThrowIfCancellationRequested();
            double fraction = SpeechRegion.SelectedIndex == 1 ? 0.5 : SpeechRegion.SelectedIndex == 2 ? 1 : 0.35;
            var result = await _subtitleOcr.ReadAsync(image, fraction, token);
            if (_closing || version != _subtitleVersion || epoch != _subtitleEpoch || !ReferenceEquals(_session, session) || !session.Player.IsPlaying ||
                !session.IsLive && Math.Abs(session.Player.Time - position) > 2000) return;
            SpeechCurrent.Text = string.IsNullOrWhiteSpace(result.Text) ? "No readable subtitle in the selected region." : result.Text;
            _subtitleSpeech?.ReadOcr(result.Text, result.Confidence);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing && version == _subtitleVersion) SpeechMode.Text = "OCR reader: " + ex.Message; }
    }
    private async Task ShutdownSubtitleSpeechAsync()
    {
        _subtitleTimer.Stop(); CancelSubtitleLoad(); InvalidateSubtitleSpeech();
        if (_subtitleFrameTask is { } frame) await frame;
        await _subtitleLoadTask;
        await Task.WhenAll(_retiredSubtitleLoads);
        await _subtitleOcr.DisposeAsync();
        if (_speakerCreation is { } creation)
        {
            try { var speaker = await creation; speaker.Failed -= OnSubtitleSpeechFailed; speaker.Dispose(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        _subtitleFrames.Dispose();
    }
    private void ToggleSubtitleSpeech(object? sender, EventArgs e) => SpeechEnabled.IsChecked = SpeechEnabled.IsChecked != true;
    private void RestoreSubtitleSpeechPreferences()
    {
        _speechUiUpdating = true;
        try { SpeechRate.Value = _preferences.SpeechRate; SpeechVolume.Value = _preferences.SpeechVolume; SpeechRegion.SelectedIndex = _preferences.SpeechRegion; SpeechEnabled.IsChecked = _preferences.SubtitleSpeechEnabled; }
        finally { _speechUiUpdating = false; }
        if (SpeechEnabled.IsChecked == true) SpeechToggle_Changed(this, new RoutedEventArgs());
    }
}
