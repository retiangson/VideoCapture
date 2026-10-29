using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using VideoCapture;
using VideoCapture.Services;

internal static class BrowserPanelTests
{
    public static async Task RunAsync(string input, string output)
    {
        Directory.CreateDirectory(output);
        string source = Path.Combine(output, "source.mp4");
        await ToolAsync("ffmpeg.exe", ["-y", "-hide_banner", "-loglevel", "error", "-stream_loop", "4", "-i", input, "-t", "30", "-c", "copy", source]);
        string clips = Path.Combine(output, "Clips-" + Guid.NewGuid().ToString("N"));
        var preferences = new MemoryPreferencesStore();
        await preferences.SaveAsync(new AppPreferences { Volume = 0 });
        var window = new MainWindow(preferences, new MemoryPlaybackHistoryStore(), new RecordingStore(clips))
        {
            BrowserFactory = opacity => new FloatingBrowser(opacity, "about:blank", Path.Combine(output, "BrowserProfile"))
        };
        window.Show();
        try
        {
            await WaitAsync(() => ((Button)window.FindName("OpenButton")).IsEnabled, "settings ready");
            SendKey(window, Key.F11); await Task.Delay(100);
            var overlay = (UIElement)window.FindName("VideoBorder");
            Click(overlay, MouseButton.Left);
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Quick click must keep the hidden menu hidden.");
            Down(overlay);
            await WaitAsync(() => window.FloatingToolbar?.IsVisible == true, "held left-click shows menu");
            Up(overlay);
            if (window.FloatingToolbar?.IsVisible != true) throw new Exception("Releasing a long press must leave the menu visible.");
            var menuWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            window.FloatingToolbar.RaiseEvent(menuWheel);
            if (Math.Abs(window.FloatingToolbar.Opacity - .91) > .001 || Math.Abs(((Slider)window.FindName("MenuOpacity")).Value - .91) > .001)
                throw new Exception("Menu wheel must update the whole menu and saved opacity control.");
            Console.WriteLine("Held/quick menu gestures and direct menu opacity checks passed.");
            Click(overlay, MouseButton.Right);
            var browser = window.BrowserWindow ?? throw new Exception("Right-click must create the browser.");
            if (!browser.IsVisible || window.FloatingToolbar?.IsVisible != true) throw new Exception("Browser and player toggles must be independent.");
            if (((Button)browser.FindName("CopyVideoFrameButton")).IsEnabled || ((Button)browser.FindName("RecordVideoButton")).IsEnabled)
                throw new Exception("Video actions must be disabled without video.");
            await browser.Initialization.WaitAsync(TimeSpan.FromSeconds(25));
            if (browser.ActiveBrowser?.CoreWebView2 is null) throw new Exception("Browser initialization failed.");
            var outsideWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            overlay.RaiseEvent(outsideWheel);
            if (!outsideWheel.Handled || Math.Abs(browser.Opacity - .95) > .001) throw new Exception("Scrolling video outside the browser must adjust browser opacity.");
            ((Slider)browser.FindName("Transparency")).Value = 1;
            foreach (string name in new[] { "VideoTools", "Address" })
            {
                ((UIElement)browser.FindName(name)).RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            }
            if (Math.Abs(browser.Opacity - .9) > .001) throw new Exception("Capture actions and address bar must both adjust browser opacity.");
            ((UIElement)browser.FindName("BrowserHost")).RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            if (Math.Abs(browser.Opacity - .9) > .001) throw new Exception("Web-page wheel must not change opacity.");
            ((UIElement)window.FindName("DevicePanel")).RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            if (Math.Abs(browser.Opacity - .85) > .001) throw new Exception("Capture settings wheel must adjust the visible browser opacity.");
            ((Slider)browser.FindName("Transparency")).Value = 1;
            Console.WriteLine("Browser initialized; outside opacity routing passed.");
            var titleBar = (UIElement)browser.FindName("BrowserTitleBar");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            titleBar.RaiseEvent(wheel);
            if (!wheel.Handled || Math.Abs(browser.Opacity - .95) > .001) throw new Exception("Title-bar scrolling must adjust the complete browser opacity.");
            for (int i = 0; i < 30; i++) titleBar.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            if (Math.Abs(browser.Opacity - .3) > .001) throw new Exception("Opacity wheel must clamp at the clickable minimum.");
            ((Slider)browser.FindName("Transparency")).Value = 1;
            var windowed = new Rect(browser.Left, browser.Top, browser.Width, browser.Height);
            Press(browser, "MaximizeButton");
            if (browser.WindowState != WindowState.Maximized) throw new Exception("Browser maximize failed.");
            Press(browser, "MaximizeButton");
            if (browser.WindowState != WindowState.Normal) throw new Exception("Browser restore failed.");
            Press(browser, "BrowserFullscreenButton");
            if (!browser.IsBrowserFullscreen || FullscreenDisplay.WindowBounds(browser) != FullscreenDisplay.MonitorBounds(browser)) throw new Exception("Browser fullscreen must cover its monitor.");
            browser.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            if (Math.Abs(browser.Opacity - .95) > .001) throw new Exception("Empty fullscreen browser chrome must adjust opacity.");
            ((Slider)browser.FindName("Transparency")).Value = 1;
            SendKey(browser, Key.Escape);
            if (browser.IsBrowserFullscreen || Math.Abs(browser.Width - windowed.Width) > 2 || Math.Abs(browser.Height - windowed.Height) > 2) throw new Exception("Fullscreen exit must restore browser size.");
            Press(browser, "MinimizeButton");
            if (browser.WindowState != WindowState.Minimized) throw new Exception("Browser minimize failed.");
            browser.WindowState = WindowState.Normal;
            browser.UpdateLayout();
            var navigation = (Expander)browser.FindName("NavigationPanel");
            var transcriptPanel = (Expander)browser.FindName("TranscriptPanel");
            if (navigation.IsExpanded || transcriptPanel.IsExpanded || transcriptPanel.Visibility != Visibility.Collapsed) throw new Exception("Navigation and transcript must start collapsed.");
            var host = (FrameworkElement)browser.FindName("BrowserHost");
            var tools = (FrameworkElement)browser.FindName("VideoTools");
            if (tools.TranslatePoint(new Point(), browser).Y < host.TranslatePoint(new Point(0, host.ActualHeight), browser).Y)
                throw new Exception("Capture tools must be below the browser page.");
            browser.SetTranscript("Hidden transcript result"); browser.SetTranscriptStatus("Ready");
            if (transcriptPanel.IsExpanded) throw new Exception("A transcript update must not open the panel without a click.");
            navigation.IsExpanded = true; browser.UpdateLayout();
            if (!((TextBox)browser.FindName("Address")).IsVisible) throw new Exception("Expanding navigation must reveal the address field.");
            navigation.IsExpanded = false;
            var retainedTab = await browser.AddTabAsync("about:blank");
            browser.Close(); browser.Show();
            if (browser.TabCount != 2 || browser.ActiveBrowser != retainedTab) throw new Exception("Closing and reopening must retain tabs and selection.");
            await browser.CloseTabAsync(retainedTab!);
            Click(overlay, MouseButton.Right);
            if (browser.IsVisible || window.FloatingToolbar?.IsVisible != true) throw new Exception("Right-click must only hide the browser.");
            Click(overlay, MouseButton.Left);
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Left-click must hide the player menu.");
            Click(overlay, MouseButton.Right);
            await window.OpenFileAsync(source);
            var player = ((VideoView)window.FindName("GpuVideo")).MediaPlayer!;
            await WaitAsync(() => window.PresentedFrameCount > 3 && player.IsSeekable, "source video");
            overlay = (UIElement)window.FindName("VideoInputOverlay");
            Down(overlay); Up(overlay); await Task.Delay(550);
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("A quick click must not show the menu after its hold timer expires.");
            Down(overlay); await WaitAsync(() => window.FloatingToolbar?.IsVisible == true, "GPU held left-click shows menu"); Up(overlay);
            if (window.FloatingToolbar?.IsVisible != true) throw new Exception("Long press must work over GPU video.");
            Click(overlay, MouseButton.Left);
            Console.WriteLine("GPU held/quick menu gestures passed.");
            ((Slider)window.FindName("Seek")).Value = 100;
            if (((Button)browser.FindName("RecordVideoButton")).IsEnabled) throw new Exception("Recording must be disabled immediately while a seek is pending.");
            await WaitAsync(() => player.Time >= 2500, "source position");
            await WaitAsync(() => ((Button)browser.FindName("RecordVideoButton")).IsEnabled, "browser recording action");
            Press(browser, "RecordVideoButton");
            try { await WaitAsync(() => ((TextBlock)browser.FindName("RecordVideoLabel")).Text == "Stop recording" && ((Button)window.FindName("OpenButton")).IsEnabled == false && window.PresentedFrameCount > 3, "local video recording"); }
            catch (TimeoutException ex) { throw new Exception($"Recording state: label={((TextBlock)browser.FindName("RecordVideoLabel")).Text}, open={((Button)window.FindName("OpenButton")).IsEnabled}, player={player.State}, position={player.Time}, status={((TextBlock)window.FindName("Status")).Text}", ex); }
            Click(overlay, MouseButton.Right);
            await Task.Delay(2500);
            Click(overlay, MouseButton.Right);
            if (((TextBlock)browser.FindName("RecordVideoLabel")).Text != "Stop recording") throw new Exception("Hiding browser interrupted recording.");
            Press(window, "PauseButton");
            await WaitAsync(() => player.State == VLCState.Paused, "pause recording playback");
            long pausedAt = player.Time;
            if (!((Button)browser.FindName("RecordVideoButton")).IsEnabled) throw new Exception("Stop recording must remain available while paused.");
            Press(browser, "RecordVideoButton");
            await WaitAsync(() => ((TextBlock)browser.FindName("RecordVideoLabel")).Text == "Record video" && ((Button)window.FindName("OpenButton")).IsEnabled && player.State == VLCState.Paused, "finish recording while paused");
            if (Math.Abs(player.Time - pausedAt) > 1500) throw new Exception($"Stopping recording moved playback from {pausedAt} to {player.Time}.");
            string clip = Directory.GetFiles(clips, "*.mkv").Single();
            string json = await ToolAsync("ffprobe.exe", ["-v", "error", "-show_entries", "stream=codec_type,width,height:format=duration", "-of", "json", clip]);
            using var probe = JsonDocument.Parse(json);
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (!streams.Any(s => s.GetProperty("codec_type").GetString() == "video" && s.GetProperty("width").GetInt32() == 640 && s.GetProperty("height").GetInt32() == 360) ||
                !streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new Exception("Recorded clip must contain the source video dimensions and audio.");
            double clipSeconds = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            if (clipSeconds < 1 || clipSeconds > 5) throw new Exception($"Expected a short marked segment, got {clipSeconds} seconds.");
            Press(window, "PauseButton"); await WaitAsync(() => player.IsPlaying, "continue source playback");
            await WaitAsync(() => ((Button)browser.FindName("RecordVideoButton")).IsEnabled, "record after resume");
            Press(browser, "RecordVideoButton"); await Task.Delay(500);
            Press(window, "StopButton");
            await WaitAsync(() => !((Button)window.FindName("StopButton")).IsEnabled && ((Button)window.FindName("OpenButton")).IsEnabled, "stop saves clip");
            await WaitAsync(() => Directory.GetFiles(clips, "*.mkv").Count(path => !path.EndsWith(".partial.mkv")) == 2, "stop saves pending clip");
            await window.OpenFileAsync(source);
            await WaitAsync(() => player.IsPlaying && ((Button)browser.FindName("RecordVideoButton")).IsEnabled, "play after stop");
            browser.ShowLocalPage("<html><body style='background:#101923;color:white;font:24px Arial;padding:30px'><h2>Video tools + browser</h2><p>The browser is excluded from captured video.</p><input aria-label='Test input' placeholder='Browser remains interactive'></body></html>");
            await Task.Delay(150);
            PlayerTests.CheckPointSelection((Slider)browser.FindName("Transparency"), .5);
            if (Math.Abs(browser.Opacity - .65) > .01) throw new Exception("Opacity must apply to the complete browser panel.");
            Capture(browser, Path.Combine(output, "browser-video-panel.png"));
            browser.Width = 460; browser.UpdateLayout(); await Task.Delay(250); Capture(browser, Path.Combine(output, "browser-video-panel-narrow.png"));
            ((Expander)browser.FindName("NavigationPanel")).IsExpanded = true;
            Press(browser, "AudioTextButton");
            browser.UpdateLayout(); await Task.Delay(250);
            if (!((Expander)browser.FindName("TranscriptPanel")).IsVisible || !((Expander)browser.FindName("TranscriptPanel")).IsExpanded) throw new Exception("Audio to text must show the expanded transcript controls.");
            Capture(browser, Path.Combine(output, "browser-video-panel-expanded.png"));
            if (((FrameworkElement)browser.FindName("BrowserHost")).ActualHeight < 100) throw new Exception("Expanded controls must leave usable page space.");
            Click(overlay, MouseButton.Right);
            if (browser.IsVisible || !player.IsPlaying) throw new Exception("Browser hide must leave video playing.");
            CheckActionEvents(output);
            Click(overlay, MouseButton.Right);
            await browser.AddTabAsync("about:blank");
            Press(browser, "RecordVideoButton");
            await Task.Delay(500);
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult(); window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        if (Directory.GetFiles(clips, "*.mkv").Length != 3 || Directory.GetFiles(clips, "*.partial.mkv").Length != 0)
            throw new Exception("App close did not finalize the pending clip cleanly.");
        if (preferences.Saved.BrowserTabs.Count != 2 || preferences.Saved.BrowserSelectedTab != 1)
            throw new Exception("Immediate app close must save open browser tabs and active selection before disposal.");
        var restoredBrowser = new FloatingBrowser(1, "about:blank", Path.Combine(output, "RestoredProfile"));
        try
        {
            restoredBrowser.RestoreTabs(preferences.Saved.BrowserTabs, preferences.Saved.BrowserSelectedTab);
            restoredBrowser.Show();
            await restoredBrowser.Initialization.WaitAsync(TimeSpan.FromSeconds(25));
            if (restoredBrowser.TabCount != 2 || restoredBrowser.SelectedTabIndex != 1 || !restoredBrowser.SessionReady)
                throw new Exception("Saved browser tabs and selection failed to restore.");
        }
        finally { restoredBrowser.Shutdown(); }
        Console.WriteLine("Browser panel checks passed: independent left/right toggles, no-video guards, video-only recording with source audio, hide/show during recording, stop while paused, preserved position, full-panel opacity, responsive layout, and frame/text/record event wiring.");
    }
    private static void CheckActionEvents(string output)
    {
        var panel = new FloatingBrowser(1, "about:blank", Path.Combine(output, "UnusedProfile"));
        int frame = 0, text = 0, record = 0;
        panel.CopyVideoFrameRequested += (_, _) => frame++;
        panel.CopyVideoTextRequested += (_, _) => text++;
        panel.RecordVideoRequested += (_, _) => record++;
        panel.SetVideoActions(true, true, false, "");
        Press(panel, "CopyVideoFrameButton"); Press(panel, "CopyVideoTextButton"); Press(panel, "RecordVideoButton");
        panel.Shutdown();
        if (frame != 1 || text != 1 || record != 1) throw new Exception("Browser action buttons did not raise their corresponding events.");
    }
    private static void Down(UIElement target) => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
    private static void Up(UIElement target) => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    private static void Click(UIElement video, MouseButton button)
    {
        if (button == MouseButton.Left) Down(video);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = button == MouseButton.Left ? UIElement.MouseLeftButtonUpEvent : UIElement.MouseRightButtonUpEvent };
        video.RaiseEvent(args);
    }
    private static void Press(Window window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void SendKey(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static async Task WaitAsync(Func<bool> condition, string action)
    { var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(action); await Task.Delay(25); } }
    private static void Capture(Window window, string path)
    { window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); png.Save(file); }
    private static async Task<string> ToolAsync(string tool, string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Tools", "Media", tool)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (process.ExitCode != 0) throw new Exception(await error);
        return await output;
    }
}
