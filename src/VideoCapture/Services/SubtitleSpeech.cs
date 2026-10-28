using System.Speech.Synthesis;

namespace VideoCapture.Services;

internal interface ISubtitleSpeaker : IDisposable
{
    void Replace(string text);
    void Stop();
    void SetPaused(bool paused);
}
internal interface ISubtitleVoiceSpeaker : ISubtitleSpeaker
{
    event Action<string>? Failed;
    IReadOnlyList<string> Voices { get; }
    void Configure(string? voice, int rate, int volume);
}

internal sealed class WindowsSubtitleSpeaker : ISubtitleVoiceSpeaker
{
    public event Action<string>? Failed;
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly string _defaultVoice;
    public IReadOnlyList<string> Voices { get; }
    public WindowsSubtitleSpeaker()
    {
        try
        {
            Voices = _synthesizer.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToArray();
            if (Voices.Count == 0) throw new InvalidOperationException("No Windows speech voice is installed.");
            _defaultVoice = _synthesizer.Voice.Name;
            _synthesizer.SetOutputToDefaultAudioDevice();
            _synthesizer.SpeakCompleted += (_, e) => { if (!e.Cancelled && e.Error is { } error) Failed?.Invoke(error.Message); };
        }
        catch { _synthesizer.Dispose(); throw; }
    }
    public void Configure(string? voice, int rate, int volume)
    {
        Stop();
        _synthesizer.SelectVoice(!string.IsNullOrWhiteSpace(voice) && Voices.Contains(voice) ? voice : _defaultVoice);
        _synthesizer.Rate = Math.Clamp(rate, -10, 10);
        _synthesizer.Volume = Math.Clamp(volume, 0, 100);
    }
    public void Replace(string text) { Stop(); _synthesizer.SpeakAsync(text.Length > 700 ? text[..700] : text); }
    public void Stop() { _synthesizer.SpeakAsyncCancelAll(); if (_synthesizer.State == SynthesizerState.Paused) _synthesizer.Resume(); }
    public void SetPaused(bool paused)
    {
        if (paused && _synthesizer.State == SynthesizerState.Speaking) _synthesizer.Pause();
        else if (!paused && _synthesizer.State == SynthesizerState.Paused) _synthesizer.Resume();
    }
    public void Dispose() { Stop(); _synthesizer.Dispose(); }
}

internal sealed class SubtitleSpeechController(ISubtitleSpeaker speaker)
{
    private readonly SubtitleRepeatFilter _ocr = new();
    private SubtitleCue? _lastCue;
    public void Reset() { speaker.Stop(); _lastCue = null; _ocr.Reset(); }
    public void SetPaused(bool paused) => speaker.SetPaused(paused);
    public void ReadEmbedded(SubtitleCue? cue)
    {
        if (cue is null) { _lastCue = null; return; }
        if (cue == _lastCue) return;
        _lastCue = cue;
        speaker.Replace(SubtitleText.Clean(cue.Text));
    }
    public void ReadOcr(string text, float confidence)
    { if (_ocr.Observe(text, confidence) is { } line) speaker.Replace(line); }
}
