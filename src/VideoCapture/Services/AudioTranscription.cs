using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using System.Text;

namespace VideoCapture.Services;

internal sealed record TranscriptionLanguage(string Id, string Name)
{ public override string ToString() => Name; }

internal sealed record MicrophoneFeedback(int Level, string Text, string Status);

internal static class AudioTranscription
{
    public const int MaximumSeconds = 120;
    public static Task<IReadOnlyList<TranscriptionLanguage>> LanguagesAsync() => Task.Run<IReadOnlyList<TranscriptionLanguage>>(() =>
        SpeechRecognitionEngine.InstalledRecognizers().OrderByDescending(r => r.Culture.TwoLetterISOLanguageName == "en")
            .ThenBy(r => r.Culture.DisplayName).Select(r => new TranscriptionLanguage(r.Id, r.Culture.DisplayName)).ToArray());

    public static async Task ExtractAsync(PlaybackFile source, long startMs, long endMs, string wave, CancellationToken token, int audioOrdinal = 0)
    {
        if (audioOrdinal is < 0 or > 127) throw new ArgumentOutOfRangeException(nameof(audioOrdinal));
        if (startMs < 0 || endMs - startMs < 250) throw new InvalidOperationException("Capture at least a moment of spoken audio before transcribing.");
        if (await Task.Run(() => PlaybackFile.Read(source.Path), token) != source) throw new IOException("The video file changed during audio capture.");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Tools", "Media", "ffmpeg.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        string Seconds(long value) => (value / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
        foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-protocol_whitelist", "file,pipe,crypto,data",
            "-ss", Seconds(startMs), "-i", source.Path, "-t", Seconds(Math.Min(MaximumSeconds * 1000L, endMs - startMs)),
            "-map", $"0:a:{audioOrdinal}", "-vn", "-sn", "-dn", "-c:a", "pcm_s16le", "-ar", "16000", "-ac", "1", wave }) start.ArgumentList.Add(arg);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = await Task.Run(() => Process.Start(start) ?? throw new IOException("Could not start the audio extractor."), token);
        var diagnostics = DrainAsync(process.StandardError);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }
            await diagnostics;
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("Audio extraction timed out.");
        }
        string errors = await diagnostics;
        if (process.ExitCode != 0) throw new IOException("Could not extract the video's audio. " + errors);
        if (await Task.Run(() => PlaybackFile.Read(source.Path), token) != source) throw new IOException("The video file changed during audio extraction.");
    }

    public static Task<string> RecognizeAsync(string wave, string recognizerId, CancellationToken token) =>
        RecognizeInputAsync(wave, recognizerId, CancellationToken.None, token);

    public static Task<string> RecognizeMicrophoneAsync(string recognizerId, CancellationToken stop, CancellationToken token, IProgress<MicrophoneFeedback>? feedback = null) =>
        RecognizeInputAsync(null, recognizerId, stop, token, feedback);

    // A graceful stop keeps recognized speech; cancellation discards the in-flight result.
    private static Task<string> RecognizeInputAsync(string? wave, string recognizerId, CancellationToken stop, CancellationToken token, IProgress<MicrophoneFeedback>? feedback = null) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        using var recognizer = new SpeechRecognitionEngine(recognizerId);
        recognizer.LoadGrammar(new DictationGrammar());
        if (wave is null) recognizer.SetInputToDefaultAudioDevice();
        else recognizer.SetInputToWaveFile(wave);
        var text = new List<string>();
        int level = 0;
        string hypothesis = "", status = "Listening to the default Windows microphone…";
        var feedbackClock = Stopwatch.StartNew();
        void Report(bool force = false)
        {
            if (feedback is null) return;
            lock (text)
            {
                if (!force && feedbackClock.ElapsedMilliseconds < 100) return;
                feedbackClock.Restart();
                string preview = string.Join(Environment.NewLine, text.Append(hypothesis).Where(t => !string.IsNullOrWhiteSpace(t)));
                feedback.Report(new(level, preview, status));
            }
        }
        recognizer.AudioLevelUpdated += (_, e) => { lock (text) level = e.AudioLevel; Report(); };
        recognizer.SpeechHypothesized += (_, e) => { lock (text) { hypothesis = e.Result.Text; status = "Speech detected · recognizing…"; } Report(true); };
        recognizer.SpeechRecognitionRejected += (_, _) => { lock (text) { status = "Speech was not understood. Check the selected language and microphone level."; hypothesis = ""; } Report(true); };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recognizer.SpeechRecognized += (_, e) =>
        {
            if ((wave is null || e.Result.Confidence >= .35f) && !string.IsNullOrWhiteSpace(e.Result.Text))
            {
                lock (text) { text.Add(e.Result.Text.Trim()); hypothesis = ""; status = "Speech recognized · keep speaking, or choose Stop & transcribe."; }
                Report(true);
            }
        };
        recognizer.RecognizeCompleted += (_, e) =>
        {
            if (e.Error is { } error) completed.TrySetException(error);
            else if (token.IsCancellationRequested || (e.Cancelled && !stop.IsCancellationRequested)) completed.TrySetCanceled();
            else completed.TrySetResult();
        };
        recognizer.RecognizeAsync(RecognizeMode.Multiple);
        using var registration = token.Register(() =>
        {
            try { recognizer.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { }
        });
        using var stopRegistration = stop.Register(() =>
        {
            try { recognizer.RecognizeAsyncStop(); }
            catch (InvalidOperationException) { }
        });
        try { await completed.Task.WaitAsync(TimeSpan.FromMinutes(3)); }
        catch (TimeoutException)
        {
            recognizer.RecognizeAsyncCancel();
            try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
            throw new TimeoutException("Windows speech recognition timed out.");
        }
        token.ThrowIfCancellationRequested();
        lock (text) return string.Join(Environment.NewLine, text);
    }, token);

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory());
            if (count == 0) return result.ToString();
            if (result.Length < 32768) result.Append(buffer, 0, Math.Min(count, 32768 - result.Length));
        }
    }
}
