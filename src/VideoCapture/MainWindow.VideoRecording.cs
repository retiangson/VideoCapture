using LibVLCSharp.Shared;
using System.Windows;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private sealed record LocalRecording(PlaybackFile Source, long StartMs, string Destination);
    private LocalRecording? _localRecording;
    private Task _clipExport = Task.CompletedTask;
    private bool IsClipSaving => !_clipExport.IsCompleted;
    private bool IsVideoRecording => _session?.IsRecording == true || _localRecording is not null;
    private string RecordingElapsed => FormatTime(_localRecording is { } clip && _session is { } session
        ? Math.Max(0, session.Player.Time - clip.StartMs) : _recordingClock.ElapsedMilliseconds);

    private async Task ToggleVideoRecordingCoreAsync()
    {
        if (_session is not { HasMedia: true } session) throw new InvalidOperationException("Start a video before recording.");
        if (!session.IsLive) { await ToggleLocalRecordingAsync(); return; }
        string? completed = session.RecordingPath;
        await StartCaptureAsync(completed is null);
        if (completed is not null)
        {
            RememberRecording(completed);
            ShowCaptureFeedback(RecordingStore.CompletionMessage(completed));
        }
        else ShowCaptureFeedback("Recording the video source and its audio; browser is excluded.");
    }
    private async Task ToggleLocalRecordingAsync()
    {
        if (_localRecording is not null) { await FinishLocalRecordingAsync(); return; }
        if (IsClipSaving) throw new InvalidOperationException("Wait for the previous video clip to finish saving.");
        if (_session is not { HasMedia: true, IsLive: false } session || _playbackFile is not { } file)
            throw new InvalidOperationException("Open a local video before recording a clip.");
        if (!session.Player.IsPlaying || _requestedPositionMs is not null || _resumePositionMs is not null)
            throw new InvalidOperationException("Wait for playback to start before recording a clip.");
        _localRecording = new(file, Math.Max(0, session.Player.Time), _recordings.CreateVideoPath());
        NowPlayingKind.Text = "LOCAL VIDEO · RECORDING";
        ShowCaptureFeedback("Recording the video segment. Stop recording to save it with source audio; browser is excluded.");
    }
    private Task FinishLocalRecordingAsync()
    {
        if (_localRecording is not { } clip || _session is not { } session) return Task.CompletedTask;
        long end = session.Player.State == VLCState.Ended ? session.Player.Length : session.Player.Time;
        _localRecording = null;
        NowPlayingKind.Text = "LOCAL VIDEO";
        ShowCaptureFeedback("Saving video clip… Playback stays at its current position.");
        _clipExport = SaveLocalClipAsync(clip, end);
        return Task.CompletedTask;
    }
    private async Task SaveLocalClipAsync(LocalRecording clip, long end)
    {
        try
        {
            await VideoClipExporter.ExportAsync(clip.Source, clip.StartMs, end, clip.Destination);
            RememberRecording(clip.Destination);
            ShowCaptureFeedback(RecordingStore.CompletionMessage(clip.Destination));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowCaptureFeedback("Could not save the video clip: " + ex.Message);
            if (_closing) MessageBox.Show(this, Status.Text, "VideoCapture");
        }
        finally { if (!_closing) _ = Dispatcher.BeginInvoke(UpdateControls); }
    }
}
