using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.WPF;
using VideoCapture;
using VideoCapture.Services;

internal static class SubtitleSpeechTests
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        CheckText();
        await Task.Run(() =>
        {
            using var speech = new SpeechSynthesizer();
            using var wave = new MemoryStream();
            speech.SetOutputToWaveStream(wave);
            speech.Speak("Offline subtitle reading test.");
            if (wave.Length < 1000) throw new Exception("Installed Windows voice produced no audio.");
            Console.WriteLine($"Offline Windows voice: {speech.Voice.Name}; synthesized {wave.Length} bytes without audio playback.");
        });
        string textFile = Path.Combine(output, "captions.srt"), movie = Path.Combine(output, "embedded captions.mkv");
        await File.WriteAllTextAsync(textFile, "1\n00:00:00,500 --> 00:00:04,000\nHello from embedded subtitles.\n\n2\n00:00:04,100 --> 00:00:09,000\nSecond embedded subtitle.\n");
        await FfmpegAsync(["-f", "lavfi", "-i", "color=c=black:s=640x360:r=15:d=12", "-i", textFile, "-map", "0:v", "-map", "1:s", "-c:v", "libx264", "-preset", "ultrafast", "-c:s", "srt", "-metadata:s:s:0", "language=eng", "-disposition:s:0", "default", movie]);
        var extractor = new EmbeddedSubtitles();
        var tracks = await extractor.ProbeAsync(movie, default);
        if (tracks.Count != 1 || EmbeddedSubtitles.Preferred(tracks) is not { IsText: true, Language: "eng" }) throw new Exception("MKV text-track detection failed.");
        var timeline = await extractor.ExtractAsync(movie, tracks[0].StreamIndex, default);
        if (timeline.Count != 2 || timeline.At(700)?.Text != "Hello from embedded subtitles.") throw new Exception("Actual MKV subtitle extraction failed.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await extractor.ProbeAsync(movie, cancelled.Token); throw new Exception("Cancelled probe completed."); }
            catch (OperationCanceledException) { }
        }
        try { await extractor.ProbeAsync(Path.Combine(output, "missing.mkv"), default); throw new Exception("Missing input accepted."); }
        catch (IOException) { }

        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 640, 360));
            draw.DrawText(new FormattedText("Visible subtitle fallback", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 28, Brushes.White, 1), new Point(85, 293));
        }
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        string picture = Path.Combine(output, "caption.png"), burned = Path.Combine(output, "burned captions.mp4");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(picture)) encoder.Save(file);
        await FfmpegAsync(["-loop", "1", "-i", picture, "-t", "12", "-r", "15", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", burned]);
        if ((await extractor.ProbeAsync(burned, default)).Count != 0) throw new Exception("Burned-in captions must have no embedded tracks.");
        await using (var ocr = new SubtitleOcr())
        {
            var read = await ocr.ReadAsync(bitmap, .35, default);
            if (!read.Text.Contains("Visible subtitle fallback") || read.Confidence < .6) throw new Exception($"Subtitle OCR failed: {read}");
        }
        Console.WriteLine("MKV extraction and region OCR passed; starting player integration.");

        var fake = new FakeSpeaker();
        var store = new MemoryPreferencesStore();
        var voiceReady = new TaskCompletionSource<ISubtitleVoiceSpeaker>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new MainWindow(store) { SubtitleSpeakerFactory = () => voiceReady.Task };
        window.Show();
        try
        {
            await WaitAsync(() => ((Button)window.FindName("OpenButton")).IsEnabled, "settings load");
            ((TabControl)window.FindName("LibraryTabs")).SelectedIndex = 2;
            await window.OpenFileAsync(movie);
            ((CheckBox)window.FindName("SpeechEnabled")).IsChecked = true;
            ((CheckBox)window.FindName("SpeechEnabled")).IsChecked = false;
            ((CheckBox)window.FindName("SpeechEnabled")).IsChecked = true;
            voiceReady.SetResult(fake);
            await WaitAsync(() => fake.Lines.Contains("Hello from embedded subtitles."), "embedded speech");
            if (fake.FailureObserverCount != 1) throw new Exception("Rapid enable/disable registered the voice more than once.");
            Console.WriteLine("Embedded player speech passed.");
            if (!((TextBlock)window.FindName("SpeechMode")).Text.StartsWith("Embedded text")) throw new Exception("Expected direct embedded mode.");
            await Task.Delay(300);
            if (fake.Lines.Count != 1) throw new Exception("Repeated playback ticks duplicated speech.");
            var player = ((VideoView)window.FindName("GpuVideo")).MediaPlayer!;
            ((Button)window.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fake.Paused, "pause speech");
            player.Time = 4500;
            ((Button)window.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fake.Lines.Contains("Second embedded subtitle."), "seek and resume");
            await window.OpenFileAsync(burned);
            await WaitAsync(() => fake.Lines.Any(l => l.Contains("Visible subtitle fallback")), "automatic OCR fallback");
            Console.WriteLine("Player OCR fallback passed.");
            if (!((TextBlock)window.FindName("SpeechMode")).Text.Contains("OCR")) throw new Exception("Expected OCR fallback mode.");
            await Task.Delay(1000);
            if (fake.Lines.Count(l => l.Contains("Visible subtitle fallback")) != 1) throw new Exception("OCR reread a stationary subtitle.");
            ((Slider)window.FindName("SpeechRate")).Value = 2;
            ((Slider)window.FindName("SpeechVolume")).Value = 61;
            ((ComboBox)window.FindName("SpeechRegion")).SelectedIndex = 1;
            fake.ReportFailure("Test voice disconnected");
            await WaitAsync(() => ((CheckBox)window.FindName("SpeechEnabled")).IsChecked == false, "voice error disables speech");
            int count = fake.Lines.Count;
            await Task.Delay(1100);
            if (fake.Lines.Count != count) throw new Exception("Disabled reader emitted stale OCR.");
            ((TabControl)window.FindName("LibraryTabs")).SelectedIndex = 2;
            window.UpdateLayout();
            var screenshot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); screenshot.Render(window);
            var screenshotEncoder = new PngBitmapEncoder(); screenshotEncoder.Frames.Add(BitmapFrame.Create(screenshot));
            using (var file = File.Create(Path.Combine(output, "speech-ui.png"))) screenshotEncoder.Save(file);
        }
        finally { await CloseAsync(window); }
        if (!fake.Disposed || store.Saved.SubtitleSpeechEnabled || store.Saved.SpeechRate != 2 || store.Saved.SpeechVolume != 61 || store.Saved.SpeechRegion != 1)
            throw new Exception("Speech shutdown or preference persistence failed.");
        Console.WriteLine("Subtitle checks passed: SRT/ASS cleanup, overlaps, duplicate filtering, real MKV extraction, cancellation, offline voice synthesis, embedded playback, pause/seek, burned-in OCR fallback, disabled-reader isolation, settings and disposal.");
    }
    private static void CheckText()
    {
        var cues = SubtitleText.ParseSrt("1\n00:00:01,000 --> 00:00:03,000\n{\\i1}<i>Hello</i> &amp; world\\Nagain\n\n2\n00:00:02,000 --> 00:00:04,000\nSecond\n\n3\n00:00:05,000 --> 00:00:04,000\nInvalid\n");
        var timeline = new SubtitleTimeline(cues);
        if (cues.Count != 2 || timeline.At(999) != null || timeline.At(1000)?.Text != "Hello & world again" || timeline.At(2000)?.Text != "Hello & world again Second" || timeline.At(3000)?.Text != "Second" || timeline.At(4000) != null)
            throw new Exception("Subtitle parsing or interval/overlap lookup is incorrect.");
        var fake = new FakeSpeaker(); var controller = new SubtitleSpeechController(fake);
        controller.ReadEmbedded(cues[0]); controller.ReadEmbedded(cues[0]); controller.ReadEmbedded(cues[0] with { StartMs = 5000, EndMs = 6000 });
        if (fake.Lines.Count != 2) throw new Exception("Embedded cue identity must include timing.");
        var filter = new SubtitleRepeatFilter();
        if (filter.Observe("Hello subtitle", .9f) != null || filter.Observe("Hello subtitle", .9f) != "Hello subtitle" || filter.Observe("Hello subtltle", .9f) != null)
            throw new Exception("OCR stability/repeat filtering failed.");
        for (int i = 0; i < 3; i++) filter.Observe("", .9f);
        filter.Observe("Hello subtitle", .9f);
        if (filter.Observe("Hello subtitle", .9f) == null || filter.Observe("Unrelated unreliable text", .1f) != null) throw new Exception("OCR blank reset/confidence failed.");
    }
    private static async Task FfmpegAsync(string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Tools", "Media", "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-y", "-hide_banner", "-loglevel", "error" }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception(await errors);
    }
    private static async Task WaitAsync(Func<bool> condition, string action)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(9)) throw new TimeoutException(action); await Task.Delay(25); }
    }
    private static async Task CloseAsync(Window window)
    {
        if (!window.IsVisible) return;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(); window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
    }
    private sealed class FakeSpeaker : ISubtitleVoiceSpeaker
    {
        public event Action<string>? Failed;
        public int FailureObserverCount => Failed?.GetInvocationList().Length ?? 0;
        public void ReportFailure(string message) => Failed?.Invoke(message);
        public IReadOnlyList<string> Voices => ["Test voice"];
        public List<string> Lines { get; } = [];
        public bool Paused { get; private set; }
        public bool Disposed { get; private set; }
        public void Configure(string? voice, int rate, int volume) { }
        public void Replace(string text) => Lines.Add(text);
        public void Stop() => Paused = false;
        public void SetPaused(bool paused) => Paused = paused;
        public void Dispose() => Disposed = true;
    }
}
