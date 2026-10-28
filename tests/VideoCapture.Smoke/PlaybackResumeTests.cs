using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.WPF;
using VideoCapture;
using VideoCapture.Services;

internal static class PlaybackResumeTests
{
    public static async Task RunAsync(string input, string output)
    {
        Directory.CreateDirectory(output);
        await CheckHistoryAsync(output);
        string movie = Path.Combine(output, "long-video.mp4"), second = Path.Combine(output, "second-video.mp4");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Tools", "Media", "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-y", "-hide_banner", "-loglevel", "error", "-stream_loop", "4", "-i", input, "-t", "30", "-c", "copy", movie }) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!)
        {
            var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception(await errors);
        }
        File.Copy(movie, second, true);
        string historyPath = Path.Combine(output, "player-history.json");
        if (File.Exists(historyPath)) File.Delete(historyPath);
        var preferences = new MemoryPreferencesStore();
        await preferences.SaveAsync(new AppPreferences { Volume = 42, SpeechVolume = 61 });
        var speaker = new FakeVoice();
        var window = new MainWindow(preferences, new PlaybackHistoryStore(historyPath)) { SubtitleSpeakerFactory = () => Task.FromResult<ISubtitleVoiceSpeaker>(speaker) };
        window.Show();
        try
        {
            await ReadyAsync(window);
            await window.OpenFileAsync(movie);
            await PictureAsync(window);
            var player = ((VideoView)window.FindName("GpuVideo")).MediaPlayer!;
            await WaitAsync(() => player.Length >= 29000 && player.IsSeekable && player.Volume == 42, "native audio and timeline");
            ((CheckBox)window.FindName("SpeechEnabled")).IsChecked = true;
            await WaitAsync(() => speaker.ConfigureCount == 1, "voice initialization");
            Key(window, System.Windows.Input.Key.F11);
            await Task.Delay(100);
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Fullscreen controls must start hidden.");
            window.ToggleFullscreenToolbar();
            var toolbar = window.FloatingToolbar!;
            if (!toolbar.ProgressVisible || !((Slider)toolbar.FindName("MenuSeek")).IsEnabled) throw new Exception("Local video must offer fullscreen seeking.");
            int seekEvents = 0;
            toolbar.SeekRequested += (_, _) => seekEvents++;
            toolbar.SetProgress(200, "00:00:06 / 00:00:30", true);
            if (seekEvents != 0) throw new Exception("A progress update sought the movie.");
            PlayerTests.CheckPointSelection((Slider)toolbar.FindName("MenuVolume"), .23);
            await WaitAsync(() => player.Volume == 23, "native video volume change");
            int stopped = speaker.StopCount, configured = speaker.ConfigureCount;
            PlayerTests.CheckPointSelection((Slider)toolbar.FindName("MenuSpeechVolume"), .37);
            if (Math.Abs(((Slider)window.FindName("Volume")).Value - 23) > 1 || Math.Abs(((Slider)window.FindName("SpeechVolume")).Value - 37) > 1 || speaker.Volume != 37 || speaker.StopCount != stopped || speaker.ConfigureCount != configured)
                throw new Exception("Reading volume must be independent and must not restart speech.");
            ((CheckBox)window.FindName("SpeechEnabled")).IsChecked = false;
            PlayerTests.CheckPointSelection((Slider)toolbar.FindName("MenuSeek"), .55);
            await WaitAsync(() => player.Time >= 15500 && player.Time < 19500, "fullscreen seek");
            ((Button)toolbar.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !player.IsPlaying, "fullscreen pause");
            ((Button)toolbar.FindName("ProgressButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (toolbar.ProgressVisible) throw new Exception("Progress toggle did not hide the timeline.");
            ((Button)toolbar.FindName("ProgressButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            Capture(toolbar, Path.Combine(output, "fullscreen-controls.png"));
            ((Button)toolbar.FindName("ProgressButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Key(window, System.Windows.Input.Key.Escape);
            window.Width = 940;
            window.UpdateLayout();
            Capture(window, Path.Combine(output, "windowed-controls.png"));
        }
        finally { await CloseAsync(window); }
        if (preferences.Saved.ShowFullscreenProgress || !speaker.Disposed) throw new Exception("Timeline preference or voice disposal failed.");
        var saved = (await new PlaybackHistoryStore(historyPath).LoadAsync()).Entries.Single(e => e.File.Path == movie);
        if (saved.PositionMs < 15500 || saved.PositionMs > 19500) throw new Exception($"Close did not retain the played position: {saved.PositionMs}");

        var reopened = new MainWindow(preferences, new PlaybackHistoryStore(historyPath));
        reopened.Show();
        try
        {
            await ReadyAsync(reopened);
            if (((Button)reopened.FindName("StopButton")).IsEnabled) throw new Exception("History restoration must not autoplay a movie.");
            if (Math.Abs(((Slider)reopened.FindName("Volume")).Value - 23) > 1 || Math.Abs(((Slider)reopened.FindName("SpeechVolume")).Value - 37) > 1) throw new Exception("Independent volume levels did not restore.");
            await reopened.OpenFileAsync(movie);
            var player = ((VideoView)reopened.FindName("GpuVideo")).MediaPlayer!;
            await WaitAsync(() => player.Time >= saved.PositionMs - 500 && player.Time < saved.PositionMs + 3000, "resume after app restart");
            Key(reopened, System.Windows.Input.Key.F11); await Task.Delay(100); reopened.ToggleFullscreenToolbar();
            if (reopened.FloatingToolbar!.ProgressVisible) throw new Exception("Hidden timeline preference did not restore.");
            Key(reopened, System.Windows.Input.Key.Escape);
            await reopened.OpenFileAsync(second);
            await PictureAsync(reopened);
            ((Slider)reopened.FindName("Seek")).Value = 250;
            // Close without waiting for native seek acknowledgement; preserve the requested position.
        }
        finally { await CloseAsync(reopened); }
        saved = (await new PlaybackHistoryStore(historyPath).LoadAsync()).Entries.Single(e => e.File.Path == second);
        if (saved.PositionMs < 7000 || saved.PositionMs > 8500) throw new Exception($"Immediate-close seek was lost: {saved.PositionMs}");

        var final = new MainWindow(new MemoryPreferencesStore(), new PlaybackHistoryStore(historyPath));
        final.Show();
        try
        {
            await ReadyAsync(final); await final.OpenFileAsync(second);
            var player = ((VideoView)final.FindName("GpuVideo")).MediaPlayer!;
            await WaitAsync(() => player.Time >= 7000 && player.Time < 10500, "resume switched file");
            player.Position = .97f;
            await WaitAsync(() => !((Button)final.FindName("StopButton")).IsEnabled && ((Button)final.FindName("OpenButton")).IsEnabled, "completed file reset");
            if ((await new PlaybackHistoryStore(historyPath).LoadAsync()).Entries.Single(e => e.File.Path == second).PositionMs != 0) throw new Exception("Completed movie retained a resume position.");
            await final.OpenFileAsync(second); await PictureAsync(final);
            if (player.Time >= 2500) throw new Exception("Completed file did not restart from the beginning.");
            await Task.Delay(5300);
            var checkpoint = (await new PlaybackHistoryStore(historyPath).LoadAsync()).Entries.Single(e => e.File.Path == second);
            if (checkpoint.PositionMs <= 0) throw new Exception("Periodic checkpoint did not reach disk.");
        }
        finally { await CloseAsync(final); }
        Console.WriteLine("Resume checks passed: independent native/voice volumes, no speech restart on volume change, fullscreen seek/timeline toggle, saved levels, close/reopen and file-switch resume, immediate-close seek, completed-file reset and periodic disk checkpoint.");
    }
    private static async Task CheckHistoryAsync(string output)
    {
        var identity = new PlaybackFile(Path.Combine(output, "identity.mp4"), 1234, DateTime.UtcNow.Ticks);
        var history = new PlaybackHistory();
        history.Remember(identity, 15000, 120000);
        if (history.ResumePosition(identity) != 15000 || history.ResumePosition(identity with { Path = identity.Path.ToUpperInvariant() }) != 15000 ||
            history.ResumePosition(identity with { Size = 1235 }) != 0 || history.ResumePosition(identity with { LastWriteUtcTicks = identity.LastWriteUtcTicks + 1 }) != 0)
            throw new Exception("History identity or case-insensitive path matching failed.");
        history.Remember(identity, 119000, 120000);
        if (history.ResumePosition(identity) != 0) throw new Exception("Near-end bookmark must restart.");
        history.Remember(identity, 5000, 8000);
        if (history.ResumePosition(identity) != 5000) throw new Exception("Short videos must retain middle positions.");
        history.Restore([null!, new(identity with { Path = "relative.mp4" }, 1, 100, DateTimeOffset.UtcNow), new(identity with { Path = "C:\\bad\0.mp4" }, 1, 100, DateTimeOffset.UtcNow), new(identity, 50, 100, DateTimeOffset.UtcNow)]);
        if (history.Snapshot().Count != 1) throw new Exception("Invalid history records were accepted.");
        for (int i = 0; i < PlaybackHistory.MaximumEntries + 5; i++) history.Remember(identity with { Path = Path.Combine(output, $"entry-{i}.mp4") }, 5000, 120000);
        if (history.Snapshot().Count != PlaybackHistory.MaximumEntries) throw new Exception("History is not bounded.");
        string path = Path.Combine(output, "store-check.json");
        var store = new PlaybackHistoryStore(path);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.SaveAsync(history.Snapshot())));
        if ((await store.LoadAsync()).Entries.Count != PlaybackHistory.MaximumEntries || Directory.GetFiles(output, "store-check.json.*.tmp").Length != 0) throw new Exception("Atomic history save failed.");
        await File.WriteAllTextAsync(path, "{broken");
        if ((await store.LoadAsync()).Warning == null) throw new Exception("Corrupt history must use defaults.");
        await File.WriteAllTextAsync(path, new string(' ', PlaybackHistoryStore.MaximumFileBytes + 1));
        if ((await store.LoadAsync()).Warning == null) throw new Exception("Oversized history must use defaults.");
        Console.WriteLine("History identity, completion, normalization, size bounds and atomic-storage checks passed.");
    }
    private static Task ReadyAsync(MainWindow window) => WaitAsync(() => ((Button)window.FindName("OpenButton")).IsEnabled, "window readiness");
    private static Task PictureAsync(MainWindow window) => WaitAsync(() => window.PresentedFrameCount > 3 && ((VideoView)window.FindName("GpuVideo")).MediaPlayer!.IsSeekable, "video picture");
    private static async Task WaitAsync(Func<bool> condition, string action)
    { var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > 9000) throw new TimeoutException(action); await Task.Delay(25); } }
    private static void Key(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static async Task CloseAsync(Window window)
    { if (!window.IsVisible) return; var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); window.Closed += (_, _) => closed.TrySetResult(); window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
    private static void Capture(Window window, string path)
    { window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); png.Save(file); }
    private sealed class FakeVoice : ISubtitleVoiceSpeaker
    {
        public event Action<string>? Failed { add { } remove { } }
        public IReadOnlyList<string> Voices => ["Test voice"];
        public int Volume { get; private set; }
        public int StopCount { get; private set; }
        public int ConfigureCount { get; private set; }
        public bool Disposed { get; private set; }
        public void Configure(string? voice, int rate, int volume) { ConfigureCount++; Volume = volume; }
        public void SetVolume(int volume) => Volume = volume;
        public void Replace(string text) { }
        public void Stop() => StopCount++;
        public void SetPaused(bool paused) { }
        public void Dispose() => Disposed = true;
    }
}
