using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using VideoCapture;
using VideoCapture.Services;

internal static class TranscriptionTests
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        string speech = Path.Combine(output, "speech.wav"), movie = Path.Combine(output, "spoken video.mp4"), extracted = Path.Combine(output, "extracted.wav");
        var languages = await AudioTranscription.LanguagesAsync();
        var english = languages.FirstOrDefault(l => l.Id.Contains("1033")) ?? throw new Exception("The generated English fixture requires an installed English Windows recognizer.");
        await Task.Run(() =>
        {
            using var voice = new SpeechSynthesizer();
            voice.SetOutputToWaveFile(speech);
            voice.Speak("The quick brown fox jumps over the lazy dog. Please copy this video transcript.");
        });
        await ToolAsync("ffmpeg.exe", ["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=navy:s=640x360:r=15:d=30", "-stream_loop", "5", "-i", speech, "-t", "30", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", movie]);
        if (File.Exists(extracted)) File.Delete(extracted);
        await AudioTranscription.ExtractAsync(PlaybackFile.Read(movie), 0, 9000, extracted, default);
        string text = await AudioTranscription.RecognizeAsync(extracted, english.Id, default);
        if (!text.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase) || !text.Contains("copy this video transcript", StringComparison.OrdinalIgnoreCase)) throw new Exception("Real offline source-audio recognition failed: " + text);
        string multiTrack = Path.Combine(output, "multiple audio tracks.mp4");
        await ToolAsync("ffmpeg.exe", ["-y", "-v", "error", "-i", movie, "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "30", "-map", "0:v:0", "-map", "1:a:0", "-map", "0:a:0", "-c:v", "copy", "-c:a", "aac", "-metadata:s:a:0", "language=fra", "-metadata:s:a:1", "language=eng", "-disposition:a:0", "0", "-disposition:a:1", "default", multiTrack]);
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await AudioTranscription.RecognizeAsync(extracted, english.Id, cancel.Token); throw new Exception("Cancelled dictation completed."); }
            catch (OperationCanceledException) { }
        }
        string silence = Path.Combine(output, "silence.wav");
        string cancellable = Path.Combine(output, "cancellable.wav");
        await ToolAsync("ffmpeg.exe", ["-y", "-v", "error", "-stream_loop", "20", "-i", extracted, "-t", "120", "-c:a", "pcm_s16le", cancellable]);
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            try { await AudioTranscription.RecognizeAsync(cancellable, english.Id, cancel.Token); throw new Exception("In-flight recognition ignored cancellation."); }
            catch (OperationCanceledException) { }
        }
        using (File.Open(cancellable, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        await ToolAsync("ffmpeg.exe", ["-y", "-v", "error", "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "2", "-c:a", "pcm_s16le", silence]);
        if (!string.IsNullOrWhiteSpace(await AudioTranscription.RecognizeAsync(silence, english.Id, default))) throw new Exception("Silence produced a transcript.");
        Console.WriteLine("Source-audio extraction, real offline dictation, silence and cancellation checks passed: " + text.Replace(Environment.NewLine, " "));

        string tapped = Path.Combine(output, "native audio only.wav");
        if (File.Exists(tapped)) File.Delete(tapped);
        using (var session = new PlaybackSession(true))
        {
            session.Player.Volume = 0;
            session.OpenFile(movie, audioCapturePath: tapped);
            await WaitAsync(() => session.DisplayedFrames > 3, "native audio-only tap video preview");
            await Task.Delay(6200);
            session.Stop();
        }
        if (new FileInfo(tapped).Length < 100000) throw new Exception("Native audio-only tap did not capture PCM audio.");
        string tappedText = await AudioTranscription.RecognizeAsync(tapped, english.Id, default);
        if (!tappedText.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase)) throw new Exception("Native source audio tap dictation failed: " + tappedText);
        Console.WriteLine("Native source-audio tap passed while decoded video frames continued.");

        var preferences = new MemoryPreferencesStore();
        await preferences.SaveAsync(new AppPreferences { Volume = 0, TranscriptionLanguage = english.Id });
        var window = new MainWindow(preferences, new MemoryPlaybackHistoryStore())
        { BrowserFactory = opacity => new FloatingBrowser(opacity, "about:blank", Path.Combine(output, "BrowserProfile")) };
        window.Show();
        try
        {
            await WaitAsync(() => ((Button)window.FindName("OpenButton")).IsEnabled, "window ready");
            window.ToggleBrowser();
            var browser = window.BrowserWindow!;
            await browser.Initialization.WaitAsync(TimeSpan.FromSeconds(20));
            await WaitAsync(() => ((ComboBox)browser.FindName("TranscriptLanguage")).Items.Count > 0, "speech languages");
            browser.UpdateLayout();
            var tabs = (TabControl)browser.FindName("Tabs");
            if (((TabItem)tabs.Items[0]).ActualHeight > 28 || ((Button)browser.FindName("NewTabButton")).ActualHeight > 25) throw new Exception("Browser tab header is not compact.");
            Press(browser, "AudioTextButton");
            if (!((Expander)browser.FindName("TranscriptPanel")).IsVisible) throw new Exception("Audio to text must reveal the transcript panel.");
            Press(browser, "AudioTextButton");
            if (((Expander)browser.FindName("TranscriptPanel")).IsVisible) throw new Exception("Audio to text must hide the transcript panel.");
            Press(browser, "AudioTextButton");
            await window.OpenFileAsync(multiTrack);
            await WaitAsync(() => ((Button)browser.FindName("StartAudioButton")).IsEnabled, "source audio action");
            Press(browser, "StartAudioButton");
            if (((Slider)window.FindName("Seek")).IsEnabled || ((Button)window.FindName("OpenButton")).IsEnabled || ((Button)browser.FindName("RecordVideoButton")).IsEnabled)
                throw new Exception("Audio capture must lock the source and conflicting recording actions.");
            await Task.Delay(6400);
            Press(window, "PauseButton");
            var player = ((VideoView)window.FindName("GpuVideo")).MediaPlayer!;
            await WaitAsync(() => player.State == VLCState.Paused, "pause during source audio capture");
            long pausedAt = player.Time;
            Press(browser, "StartAudioButton");
            await WaitAsync(() => browser.Transcript.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase), "UI audio transcript");
            if (Math.Abs(player.Time - pausedAt) > 1000 || player.State != VLCState.Paused) throw new Exception("Transcription moved or resumed the video.");
            var transcript = (TextBox)browser.FindName("TranscriptText");
            transcript.Text += Environment.NewLine + "Edited transcript.";
            if (!((Button)browser.FindName("CopyTranscriptButton")).IsEnabled || transcript.IsReadOnly) throw new Exception("Transcript must be editable and copyable after completion.");
            if (Environment.GetEnvironmentVariable("VIDEOCAPTURE_TEST_CLIPBOARD") == "1")
            {
                var previous = Clipboard.GetDataObject();
                try
                {
                    Press(browser, "CopyTranscriptButton");
                    await WaitAsync(() => Clipboard.ContainsText() && Clipboard.GetText() == transcript.Text, "transcript clipboard");
                }
                finally { if (previous is null) Clipboard.Clear(); else Clipboard.SetDataObject(previous, true); }
            }
            string previousTranscript = browser.Transcript;
            Press(window, "PauseButton");
            await WaitAsync(() => ((Button)browser.FindName("StartAudioButton")).IsEnabled, "resume before cancel test");
            Press(browser, "StartAudioButton");
            Press(browser, "CancelAudioButton");
            await WaitAsync(() => ((Button)window.FindName("OpenButton")).IsEnabled && !((Button)browser.FindName("CancelAudioButton")).IsEnabled, "cancel capture");
            if (browser.Transcript != previousTranscript) throw new Exception("Cancel lost the existing transcript.");
            if (Environment.GetEnvironmentVariable("VIDEOCAPTURE_TEST_MICROPHONE") == "1")
            {
                Press(window, "StopButton");
                browser.SetMicrophoneSource(true);
                await WaitAsync(() => ((Button)browser.FindName("StartAudioButton")).IsEnabled, "microphone available without video");
                Press(browser, "StartAudioButton");
                await Task.Delay(750);
                if (!((Button)browser.FindName("CancelAudioButton")).IsEnabled || ((ComboBox)browser.FindName("TranscriptSource")).IsEnabled)
                    throw new Exception("Microphone capture failed or did not lock its source selector: " + ((TextBlock)browser.FindName("TranscriptStatus")).Text);
                Press(browser, "AudioTextButton");
                if (((Expander)browser.FindName("TranscriptPanel")).IsVisible || !((Button)browser.FindName("CancelAudioButton")).IsEnabled)
                    throw new Exception("Hiding the transcript must leave capture active.");
                Press(browser, "AudioTextButton");
                Press(browser, "CancelAudioButton");
                await WaitAsync(() => ((ComboBox)browser.FindName("TranscriptSource")).IsEnabled, "microphone cancellation and release");
                if (browser.Transcript != previousTranscript) throw new Exception("Microphone cancellation changed the previous transcript.");
                using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                int feedbackEvents = 0;
                var microphoneFeedback = new Progress<MicrophoneFeedback>(update =>
                {
                    if (update.Level is < 0 or > 100) throw new Exception("Microphone level is out of range.");
                    feedbackEvents++;
                });
                await AudioTranscription.RecognizeMicrophoneAsync(english.Id, stop.Token, CancellationToken.None, microphoneFeedback).WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(100);
                if (feedbackEvents == 0) throw new Exception("The microphone opened but Windows delivered no audio level or speech events.");
                Console.WriteLine("Default microphone checks passed: input opened, graceful stop, cancellation/release, no-video capture, hidden-panel capture, source lock and previous transcript preservation.");
            }
            browser.UpdateLayout();
            var image = new RenderTargetBitmap((int)browser.ActualWidth, (int)browser.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(browser);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(Path.Combine(output, "audio-transcript-panel.png"))) png.Save(file);
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult(); window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        if (preferences.Saved.TranscriptionLanguage != english.Id) throw new Exception("Speech language preference was lost.");
        Console.WriteLine("Transcription UI checks passed: compact tabs, video-only audio, language selection, capture locks, pause preservation, editable transcript, copy action, cancellation and retained language.");
    }
    private static void Press(Window window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task WaitAsync(Func<bool> condition, string action)
    { var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(action); await Task.Delay(25); } }
    private static async Task<string> ToolAsync(string tool, string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Tools", "Media", tool)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception(await error); return await output;
    }
}
