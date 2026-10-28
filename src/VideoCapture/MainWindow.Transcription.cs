using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private sealed record AudioCapture(PlaybackFile? File, long StartMs, string? LiveWave, string Language, int AudioOrdinal);
    private AudioCapture? _audioCapture;
    private readonly Stopwatch _audioCaptureClock = new();
    private readonly HashSet<string> _audioFiles = [];
    private CancellationTokenSource? _transcriptionCancellation;
    private Task _transcriptionTask = Task.CompletedTask, _transcriptionLanguages = Task.CompletedTask;
    private bool _transcribing, _transcriptionLanguagesReady;
    private CancellationTokenSource? _microphoneStop;
    private bool IsMicrophoneListening => _microphoneStop is { IsCancellationRequested: false };
    private bool IsAudioCapturing => _audioCapture is not null;
    private bool IsSourceLocked => IsVideoRecording || IsAudioCapturing;

    private async Task InitializeTranscriptionLanguagesAsync()
    {
        try
        {
            var languages = await AudioTranscription.LanguagesAsync();
            if (_closing || _browser is null) return;
            _browser.SetTranscriptLanguages(languages, _preferences.TranscriptionLanguage);
            _transcriptionLanguagesReady = languages.Count > 0;
            if (languages.Count == 0) _browser.SetTranscriptStatus("Install a Windows desktop speech-recognition language to enable offline dictation.");
            UpdateFullscreenToolbar();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing) _browser?.SetTranscriptStatus("Windows speech recognition is unavailable: " + ex.Message); }
    }
    private bool CanStartSelectedAudio => _browser?.MicrophoneSelected == true
        ? !_closing && _transcriptionLanguagesReady && !_transcribing && !IsAudioCapturing
        : CanStartAudioCapture;
    private bool CanStartAudioCapture => !_closing && _transcriptionLanguagesReady && !_transcribing && !IsVideoRecording &&
        _session is { HasMedia: true } session && session.Player.IsPlaying && session.Player.AudioTrack >= 0 && session.Player.AudioTrackDescription.Any(t => t.Id >= 0) &&
        _requestedPositionMs is null && _resumePositionMs is null;
    private string NewAudioWave()
    {
        string wave = Path.Combine(Path.GetTempPath(), "VideoCapture-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        _audioFiles.Add(wave);
        return wave;
    }
    private async Task ToggleAudioCaptureAsync()
    {
        if (IsMicrophoneListening) { _microphoneStop!.Cancel(); return; }
        if (_browser?.MicrophoneSelected == true)
        {
            if (!CanStartSelectedAudio || _browser.TranscriptLanguageId is not { } microphoneLanguage)
                throw new InvalidOperationException("Choose an installed Windows speech language before starting the microphone.");
            _transcriptionCancellation?.Dispose();
            _transcriptionCancellation = new();
            _microphoneStop = new(TimeSpan.FromSeconds(AudioTranscription.MaximumSeconds));
            _transcribing = true;
            _audioCaptureClock.Restart();
            _browser.SetTranscriptStatus("Listening to the default Windows microphone. Click Stop & transcribe when finished (maximum 2 minutes).");
            _transcriptionTask = TranscribeMicrophoneAsync(microphoneLanguage, _microphoneStop.Token, _transcriptionCancellation.Token);
            return;
        }
        if (_audioCapture is not null) { await FinishAudioCaptureAsync(true); return; }
        if (!CanStartAudioCapture || _browser?.TranscriptLanguageId is not { } language || _session is not { } session)
            throw new InvalidOperationException("Play a video with an audio track, choose a Windows speech language, and stop video recording before capturing audio.");
        string? wave = session.IsLive ? NewAudioWave() : null;
        var audioTracks = session.Player.AudioTrackDescription.Where(t => t.Id >= 0).ToArray();
        int audioOrdinal = Math.Max(0, Array.FindIndex(audioTracks, t => t.Id == session.Player.AudioTrack));
        _audioCapture = new(session.IsLive ? null : _playbackFile, Math.Max(0, session.Player.Time), wave, language, audioOrdinal);
        try { if (wave is not null) await StartCaptureAsync(false, wave); }
        catch { _audioCapture = null; DeleteUnusedAudioFiles(); throw; }
        _audioCaptureClock.Restart();
        _browser.SetTranscriptStatus("Capturing video audio only. Click Stop & transcribe when finished (maximum 2 minutes).");
    }
    private async Task TranscribeMicrophoneAsync(string language, CancellationToken stop, CancellationToken token)
    {
        try
        {
            string text = await AudioTranscription.RecognizeMicrophoneAsync(language, stop, token);
            if (_closing || token.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text)) _browser?.SetTranscriptStatus("No clear microphone speech was recognized. Check the default Windows input device and microphone privacy permissions. Previous transcript was kept.");
            else { _browser?.SetTranscript(text); _browser?.SetTranscriptStatus("Microphone transcript ready. Review it, then choose Copy transcript."); }
        }
        catch (OperationCanceledException) { if (!_closing) _browser?.SetTranscriptStatus("Microphone transcription cancelled. Previous transcript was kept."); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing) _browser?.SetTranscriptStatus("Could not transcribe the microphone. Check Windows input settings and microphone permissions. " + ex.Message); }
        finally
        {
            _transcribing = false;
            _microphoneStop?.Dispose(); _microphoneStop = null;
            if (!_closing) _ = Dispatcher.BeginInvoke(UpdateControls);
        }
    }
    private async Task FinishAudioCaptureAsync(bool restartLive)
    {
        if (_audioCapture is not { } capture || _session is not { } session) return;
        long end = session.Player.State == VLCState.Ended ? session.Player.Length : session.Player.Time;
        _audioCapture = null;
        if (capture.LiveWave is not null)
        {
            try
            {
                if (restartLive) await StartCaptureAsync(false);
                else await Task.Run(session.Stop);
            }
            catch
            {
                _browser?.SetTranscriptStatus("The capture device could not finish the audio capture. Reconnect the device and try again.");
                DeleteUnusedAudioFiles();
                throw;
            }
        }
        string wave = capture.LiveWave ?? NewAudioWave();
        _transcriptionCancellation?.Dispose();
        _transcriptionCancellation = new();
        _transcribing = true;
        _browser?.SetTranscriptStatus("Converting captured video audio to text on this PC…");
        _transcriptionTask = TranscribeAudioAsync(capture, end, wave, _transcriptionCancellation.Token);
    }
    private async Task TranscribeAudioAsync(AudioCapture capture, long end, string wave, CancellationToken token)
    {
        try
        {
            if (capture.File is { } file) await AudioTranscription.ExtractAsync(file, capture.StartMs, end, wave, token, capture.AudioOrdinal);
            token.ThrowIfCancellationRequested();
            string text = await AudioTranscription.RecognizeAsync(wave, capture.Language, token);
            if (_closing || token.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text)) _browser?.SetTranscriptStatus("No clear speech was recognized. Previous transcript and clipboard were kept.");
            else
            {
                _browser?.SetTranscript(text);
                _browser?.SetTranscriptStatus("Review the transcript, correct any recognition errors, then choose Copy transcript.");
            }
        }
        catch (OperationCanceledException) { if (!_closing) _browser?.SetTranscriptStatus("Transcription cancelled. Previous transcript and clipboard were kept."); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_closing) _browser?.SetTranscriptStatus("Could not transcribe audio: " + ex.Message); }
        finally
        {
            _transcribing = false;
            DeleteAudioFile(wave);
            if (!_closing) _ = Dispatcher.BeginInvoke(UpdateControls);
        }
    }
    private async Task CancelAudioCaptureAsync()
    {
        if (_audioCapture is { } capture)
        {
            _audioCapture = null;
            if (capture.LiveWave is not null) await StartCaptureAsync(false);
            DeleteUnusedAudioFiles();
            _browser?.SetTranscriptStatus("Audio capture cancelled. Previous transcript and clipboard were kept.");
        }
        else _transcriptionCancellation?.Cancel();
    }
    private void InterruptAudioCapture()
    {
        if (_audioCapture is null) return;
        _audioCapture = null;
        _browser?.SetTranscriptStatus("Audio capture was interrupted by a device/source change. Please try again.");
    }
    private async Task TickAudioCaptureAsync()
    {
        if (IsMicrophoneListening && _audioCaptureClock.Elapsed.TotalSeconds >= AudioTranscription.MaximumSeconds) _microphoneStop!.Cancel();
        if (IsAudioCapturing && _audioCaptureClock.Elapsed.TotalSeconds >= AudioTranscription.MaximumSeconds)
            await RunAsync(() => FinishAudioCaptureAsync(true), showError: false);
    }
    private async Task CopyTranscriptAsync()
    {
        if (_browser is null || string.IsNullOrWhiteSpace(_browser.Transcript)) return;
        try { await SnapshotService.CopyTextAsync(_browser.Transcript); _browser.SetTranscriptStatus("Transcript copied — paste with Ctrl+V."); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _browser.SetTranscriptStatus("Could not copy transcript: " + ex.Message); }
    }
    private void DeleteAudioFile(string wave)
    {
        try { if (File.Exists(wave)) File.Delete(wave); _audioFiles.Remove(wave); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private void DeleteUnusedAudioFiles()
    { if (!_transcribing && !IsAudioCapturing) foreach (string wave in _audioFiles.ToArray()) DeleteAudioFile(wave); }
    private async Task ShutdownTranscriptionAsync()
    {
        _audioCapture = null;
        _transcriptionCancellation?.Cancel();
        await _transcriptionTask;
        await _transcriptionLanguages;
        _transcriptionCancellation?.Dispose();
        DeleteUnusedAudioFiles();
    }
}
