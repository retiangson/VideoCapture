using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private readonly IPlaybackHistoryStore _playbackHistoryStore;
    private readonly PlaybackHistory _playbackHistory = new();
    private readonly Stopwatch _historyCheckpoint = Stopwatch.StartNew(), _resumeClock = new();
    private PlaybackFile? _playbackFile;
    private long? _resumePositionMs, _requestedPositionMs;
    private Task _historyWrite = Task.CompletedTask;
    private bool _historyLoaded;
    private bool _resumingSavedPosition;

    private async Task RestorePlaybackHistoryAsync()
    {
        var result = await _playbackHistoryStore.LoadAsync();
        if (_closing) return;
        _playbackHistory.Restore(result.Entries);
        _historyLoaded = true;
        if (result.Warning is not null) Status.Text = result.Warning;
    }
    private void BeginPlaybackHistory(PlaybackFile file)
    {
        _playbackFile = file;
        long saved = _playbackHistory.ResumePosition(file);
        _resumePositionMs = saved > 0 ? saved : null;
        _requestedPositionMs = null;
        _resumingSavedPosition = false;
        _resumeClock.Restart();
        _historyCheckpoint.Restart();
    }
    private void UpdatePlaybackResume()
    {
        if (_session is not { HasMedia: true, IsLive: false } session || _playbackFile is null) return;
        if (_resumePositionMs is { } saved && session.Player.Length > 0 && session.Player.IsSeekable)
        {
            _resumePositionMs = null;
            long target = Math.Min(saved, Math.Max(0, session.Player.Length - 1000));
            SeekPlaybackTo(target);
            _resumingSavedPosition = true;
            Status.Text = "Resuming at " + FormatTime(target) + "…";
        }
        if (_resumePositionMs is not null && _resumeClock.ElapsedMilliseconds > 10000)
        {
            _resumePositionMs = null;
            Status.Text = "This video does not support restoring its saved position.";
        }
        if (_requestedPositionMs is { } requested)
        {
            bool acknowledged = Math.Abs(session.Player.Time - requested) < 1500;
            if (acknowledged || _resumeClock.ElapsedMilliseconds > 4000)
            {
                if (_resumingSavedPosition) Status.Text = acknowledged
                    ? "Resumed at " + FormatTime(requested) + " · " + Path.GetFileName(_playbackFile.Path)
                    : "The player could not restore the saved position; playback continues at its current position.";
                _requestedPositionMs = null;
                _resumingSavedPosition = false;
            }
        }
        if (_historyCheckpoint.ElapsedMilliseconds >= 5000)
        {
            RememberPlaybackPosition();
            _historyWrite = SavePlaybackHistoryAsync();
            _historyCheckpoint.Restart();
        }
    }
    private void SeekPlaybackTo(long positionMs)
    {
        if (_busy || _closing || _localRecording is not null || _session is not { HasMedia: true, IsLive: false } session || !session.Player.IsSeekable) return;
        _resumePositionMs = null;
        _resumingSavedPosition = false;
        _requestedPositionMs = Math.Clamp(positionMs, 0, Math.Max(0, session.Player.Length));
        _resumeClock.Restart();
        InvalidateSubtitleSpeech();
        session.Player.Time = _requestedPositionMs.Value;
    }
    private void RememberPlaybackPosition()
    {
        if (_playbackFile is null || _session is not { HasMedia: true, IsLive: false } session) return;
        bool completed = session.Player.State == VLCState.Ended;
        long position = _requestedPositionMs ?? _resumePositionMs ?? session.Player.Time;
        _playbackHistory.Remember(_playbackFile, position, session.Player.Length, completed);
    }
    private async Task SaveAndClearPlaybackHistoryAsync()
    {
        if (_playbackFile is null) return;
        RememberPlaybackPosition();
        _playbackFile = null; _resumePositionMs = _requestedPositionMs = null;
        await FlushPlaybackHistoryAsync();
    }
    private async Task SavePlaybackHistoryAsync()
    {
        if (!_historyLoaded) return;
        try { await _playbackHistoryStore.SaveAsync(_playbackHistory.Snapshot()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = "Could not save playback history: " + ex.Message; }
    }
    private async Task FlushPlaybackHistoryAsync()
    {
        await _historyWrite;
        _historyWrite = SavePlaybackHistoryAsync();
        await _historyWrite;
    }
}
