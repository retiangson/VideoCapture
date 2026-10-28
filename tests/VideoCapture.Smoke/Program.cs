using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using LibVLCSharp.Shared;
using VideoCapture;
using VideoCapture.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--fake-cec", var cecScenario, var cecLog]) return CecTests.RunFake(cecScenario, cecLog);
            if (args is ["--test-cec"])
            {
                Task.Run(CecTests.RunAsync).GetAwaiter().GetResult();
                return 0;
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            if (args is ["--test-transcription", var transcriptionOutput])
            {
                var checks = TranscriptionTests.RunAsync(Path.GetFullPath(transcriptionOutput));
                PumpUntil(() => checks.IsCompleted, "offline transcription checks", 55);
                checks.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--test-browser-panel", var panelInput, var panelOutput])
            {
                var checks = BrowserPanelTests.RunAsync(Path.GetFullPath(panelInput), Path.GetFullPath(panelOutput));
                PumpUntil(() => checks.IsCompleted, "browser capture panel checks", 55);
                checks.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--test-resume", var resumeInput, var resumeOutput])
            {
                var checks = PlaybackResumeTests.RunAsync(Path.GetFullPath(resumeInput), Path.GetFullPath(resumeOutput));
                PumpUntil(() => checks.IsCompleted, "fullscreen controls and playback history checks", 55);
                checks.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--test-speech", var speechOutput])
            {
                var checks = SubtitleSpeechTests.RunAsync(Path.GetFullPath(speechOutput));
                PumpUntil(() => checks.IsCompleted, "subtitle speech checks", 55);
                checks.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--test-player", var playerInput, var playerOutput])
            {
                var playerChecks = PlayerTests.RunAsync(Path.GetFullPath(playerInput), Path.GetFullPath(playerOutput));
                PumpUntil(() => playerChecks.IsCompleted, "playlist and player interaction checks");
                playerChecks.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--test-preferences"])
            {
                var preferencesCheck = PreferencesTests.RunAsync();
                PumpUntil(() => preferencesCheck.IsCompleted, "saved preferences checks");
                preferencesCheck.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--profile-startup"])
            {
                var startupWatch = Stopwatch.StartNew();
                var startupWindow = new MainWindow(new MemoryPreferencesStore(), new MemoryPlaybackHistoryStore());
                double constructedMs = startupWatch.Elapsed.TotalMilliseconds;
                double paintedMs = -1;
                startupWindow.ContentRendered += (_, _) => paintedMs = startupWatch.Elapsed.TotalMilliseconds;
                var heartbeat = Stopwatch.StartNew();
                double worstGap = 0;
                var pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                pulse.Tick += (_, _) => { worstGap = Math.Max(worstGap, heartbeat.Elapsed.TotalMilliseconds); heartbeat.Restart(); };
                pulse.Start();
                startupWindow.Show();
                PumpUntil(() => paintedMs >= 0 && ((ComboBox)startupWindow.FindName("VideoDevices")).Items.Count > 0, "startup profile");
                pulse.Stop();
                if (Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Equals("libvlc.dll", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Idle startup must not load the native playback engine.");
                Console.WriteLine($"Constructed={constructedMs:0} ms; first content render={paintedMs:0} ms; devices ready={startupWatch.Elapsed.TotalMilliseconds:0} ms; largest UI heartbeat gap={worstGap:0} ms.");
                CloseWindow(startupWindow);
                return 0;
            }
            if (args is ["--profile-playback", var profileMovie])
            {
                var playbackWatch = Stopwatch.StartNew();
                using var profileSession = new PlaybackSession();
                double engineMs = playbackWatch.Elapsed.TotalMilliseconds;
                profileSession.OpenFile(Path.GetFullPath(profileMovie));
                WaitUntil(() => profileSession.Frames.FrameCount >= 3, profileSession, "first playback frames");
                Console.WriteLine($"Playback engine={engineMs:0} ms; first 3 video frames={playbackWatch.Elapsed.TotalMilliseconds:0} ms.");
                return 0;
            }
            if (args is ["--test-overlays", var overlayOutput])
            {
                var overlays = OverlayTests.RunAsync(Path.GetFullPath(overlayOutput));
                PumpUntil(() => overlays.IsCompleted, "OCR and browser checks");
                overlays.GetAwaiter().GetResult();
                return 0;
            }
            if (args is ["--test-cec-ui", var cecScreenshot])
            {
                var remote = new CecRemoteWindow();
                remote.Show();
                remote.Dispatcher.Invoke(() => remote.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                if (((Panel)remote.FindName("RemoteButtons")).IsEnabled || ((Button)remote.FindName("ScanButton")).IsEnabled)
                    throw new Exception("CEC commands must be disabled before a connection and explicit target selection.");
                string screenshotPath = Path.GetFullPath(cecScreenshot);
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
                var image = new RenderTargetBitmap((int)remote.ActualWidth, (int)remote.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(remote);
                var cecEncoder = new PngBitmapEncoder();
                cecEncoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(screenshotPath)) cecEncoder.Save(file);
                var shutdown = remote.ShutdownAsync();
                PumpUntil(() => shutdown.IsCompleted, "CEC remote shutdown");
                shutdown.GetAwaiter().GetResult();
                remote.Close();
                Console.WriteLine("CEC disconnected controls and window shutdown verified.");
                return 0;
            }
            var fourByThree = new VideoSourceInfo(640, 480, 30, 4d / 3, false);
            if (VideoLayout.CenterCrop(fourByThree, 1920, 1080) != new VideoCrop(640, 360, 0, 60))
                throw new Exception("4:3 must fill a 16:9 screen with a centered vertical crop, without changing pixel proportions.");
            var wide = new VideoSourceInfo(1920, 1080, 60, 16d / 9, false);
            if (VideoLayout.CenterCrop(wide, 1024, 768) != new VideoCrop(1440, 1080, 240, 0))
                throw new Exception("16:9 must fill a 4:3 screen with a centered horizontal crop.");
            if (args is ["--check-fullscreen", var aspectFile, var aspectSnapshot])
            {
                var aspectWindow = new MainWindow(new MemoryPreferencesStore(), new MemoryPlaybackHistoryStore());
                aspectWindow.Show();
                aspectWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var openAspect = aspectWindow.OpenFileAsync(Path.GetFullPath(aspectFile));
                PumpUntil(() => openAspect.IsCompleted, "aspect test playback");
                openAspect.GetAwaiter().GetResult();
                PumpUntil(() => aspectWindow.PresentedFrameCount > 4 && aspectWindow.SourceVideoInfo is not null, "aspect test picture");
                ((Button)aspectWindow.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var expectedBounds = FullscreenDisplay.MonitorBounds(aspectWindow);
                SendKey(aspectWindow, Key.F11);
                aspectWindow.Dispatcher.Invoke(() => aspectWindow.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                if (FullscreenDisplay.WindowBounds(aspectWindow) != expectedBounds)
                    throw new Exception($"Fullscreen must cover monitor pixels {expectedBounds}, got {FullscreenDisplay.WindowBounds(aspectWindow)}.");
                var view = (LibVLCSharp.WPF.VideoView)aspectWindow.FindName("GpuVideo");
                var size = FullscreenDisplay.ClientSize(view.MediaPlayer!.Hwnd);
                if (Math.Abs(size.Width - expectedBounds.Width) > 1 || Math.Abs(size.Height - expectedBounds.Height) > 1)
                    throw new Exception($"Native GPU surface is {size}; monitor is {expectedBounds.Size}.");
                var source = aspectWindow.SourceVideoInfo!;
                var crop = VideoLayout.CenterCrop(source, size.Width, size.Height);
                if (view.MediaPlayer.CropGeometry != crop.BorderGeometry(source)) throw new Exception($"Expected crop borders {crop.BorderGeometry(source)}, got {view.MediaPlayer.CropGeometry}.");
                Console.WriteLine($"Actual monitor and native video surface: {size}; source aspect {source.DisplayAspectRatio:0.####}; centered crop {crop.Geometry}.");
                string aspectSnapshotPath = Path.GetFullPath(aspectSnapshot);
                Directory.CreateDirectory(Path.GetDirectoryName(aspectSnapshotPath)!);
                if (!aspectWindow.TakeVideoSnapshot(aspectSnapshotPath)) throw new Exception("Fullscreen snapshot failed.");
                var croppedPicture = new BitmapImage();
                croppedPicture.BeginInit();
                croppedPicture.CacheOption = BitmapCacheOption.OnLoad;
                croppedPicture.UriSource = new Uri(aspectSnapshotPath);
                croppedPicture.EndInit();
                if (croppedPicture.PixelWidth != crop.Width || croppedPicture.PixelHeight != crop.Height)
                    throw new Exception($"Native cropped picture is {croppedPicture.PixelWidth}x{croppedPicture.PixelHeight}, expected {crop.Width}x{crop.Height}.");
                SendKey(aspectWindow, Key.Escape);
                if (((ComboBox)aspectWindow.FindName("PictureFit")).SelectedIndex != 0)
                    throw new Exception("Exiting fullscreen must restore the windowed fit setting.");
                CloseWindow(aspectWindow);
                return 0;
            }
            if (args is ["--probe-ui", var uiDevice, var uiWidth, var uiHeight, var uiFps])
            {
                var previewWindow = new MainWindow(new MemoryPreferencesStore(), new MemoryPlaybackHistoryStore());
                previewWindow.Show();
                previewWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var devices = (ComboBox)previewWindow.FindName("VideoDevices");
                PumpUntil(() => devices.Items.Cast<CaptureDevice>().Any(d => d.Name == uiDevice), "initial capture discovery");
                devices.SelectedItem = devices.Items.Cast<CaptureDevice>().Single(d => d.Name == uiDevice);
                var resolutions = (ComboBox)previewWindow.FindName("CaptureResolutions");
                PumpUntil(() => resolutions.IsEnabled && resolutions.Items.Count > 0, "resolution selector");
                resolutions.SelectedItem = resolutions.Items.Cast<CaptureResolution>().Single(r => r.Width == int.Parse(uiWidth) && r.Height == int.Parse(uiHeight));
                var rates = (ComboBox)previewWindow.FindName("CaptureFrameRates");
                rates.SelectedItem = rates.Items.Cast<CaptureFrameRate>().Single(r => Math.Abs(r.FramesPerSecond - double.Parse(uiFps, System.Globalization.CultureInfo.InvariantCulture)) < 0.01);
                PumpUntil(() => previewWindow.PresentedFrameCount >= 10, "live WPF picture");
                long initial = previewWindow.PresentedFrameCount;
                var elapsed = Stopwatch.StartNew();
                PumpUntil(() => elapsed.Elapsed.TotalSeconds >= 5, "live preview sample");
                double fps = (previewWindow.PresentedFrameCount - initial) / elapsed.Elapsed.TotalSeconds;
                var source = previewWindow.SourceVideoInfo ?? throw new Exception("Source metadata missing.");
                if (Math.Abs(source.DisplayAspectRatio - (double)source.Width / source.Height) > 0.001)
                    throw new Exception("Live capture must not apply the card's incorrect non-square pixel metadata.");
                Console.WriteLine($"Live {(previewWindow.IsGpuPreview ? "GPU" : "CPU")} preview: {source.Width}x{source.Height}; {fps:0.0} displayed fps.");
                Console.WriteLine(((TextBlock)previewWindow.FindName("SourceDetails")).Text);
                if (source.Width != int.Parse(uiWidth) || source.Height != int.Parse(uiHeight))
                    throw new Exception("The preview lost native resolution or included decoder padding.");
                SendKey(previewWindow, Key.F11);
                previewWindow.Dispatcher.Invoke(() => previewWindow.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                var liveMonitor = FullscreenDisplay.MonitorBounds(previewWindow);
                if (FullscreenDisplay.WindowBounds(previewWindow) != liveMonitor)
                    throw new Exception("Live fullscreen does not cover the monitor.");
                var liveView = (LibVLCSharp.WPF.VideoView)previewWindow.FindName("GpuVideo");
                var liveSurface = FullscreenDisplay.ClientSize(liveView.MediaPlayer!.Hwnd);
                if (liveSurface != liveMonitor.Size) throw new Exception("Live native video surface is smaller than the monitor.");
                var liveCrop = VideoLayout.CenterCrop(source, liveSurface.Width, liveSurface.Height);
                if (liveView.MediaPlayer.CropGeometry != liveCrop.BorderGeometry(source))
                    throw new Exception("Live fullscreen has incorrect aspect-preserving crop margins.");
                Console.WriteLine($"Live fullscreen: {liveSurface}; source aspect {source.DisplayAspectRatio:0.####}; crop margins {liveView.MediaPlayer.CropGeometry}.");
                SendKey(previewWindow, Key.Escape);
                CloseWindow(previewWindow);
                return 0;
            }
            if (args is ["--probe-recovery", var recoveryDevice, var recoveryMovie])
            {
                var recoveryWindow = new MainWindow(new MemoryPreferencesStore(), new MemoryPlaybackHistoryStore());
                recoveryWindow.Show();
                var recoveryDevices = (ComboBox)recoveryWindow.FindName("VideoDevices");
                PumpUntil(() => recoveryDevices.Items.Cast<CaptureDevice>().Any(d => d.Name == recoveryDevice), "initial device discovery");
                recoveryDevices.SelectedItem = recoveryDevices.Items.Cast<CaptureDevice>().Single(d => d.Name == recoveryDevice);
                PumpUntil(() => recoveryWindow.PresentedFrameCount >= 10, "automatic preview on selection");
                if (recoveryWindow.FindName("PreviewButton") is not null) throw new Exception("Manual preview button must be removed.");
                var recoveryRates = (ComboBox)recoveryWindow.FindName("CaptureFrameRates");
                recoveryRates.SelectedItem = recoveryRates.Items.Cast<CaptureFrameRate>().First(r => Math.Abs(r.FramesPerSecond - 30) < 0.01);
                try { PumpUntil(() => recoveryWindow.SourceVideoInfo is { FramesPerSecond: > 29 and < 31 }, "automatic FPS apply"); }
                catch
                {
                    Console.WriteLine($"FPS apply diagnostic: selected {recoveryRates.SelectedItem}; source {recoveryWindow.SourceVideoInfo}; status {((TextBlock)recoveryWindow.FindName("Status")).Text}; details {((TextBlock)recoveryWindow.FindName("SourceDetails")).Text}");
                    throw;
                }
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    recoveryWindow.ReadVideoDevices = () => CaptureDevices.Video().Where(d => d.Name != recoveryDevice).ToArray();
                    PumpUntil(() => recoveryWindow.IsWaitingForCapture, "capture removal detection");
                    if (((LibVLCSharp.WPF.VideoView)recoveryWindow.FindName("GpuVideo")).Visibility != Visibility.Collapsed)
                        throw new Exception("Disconnected capture must clear its stale GPU image.");
                    recoveryWindow.ReadVideoDevices = CaptureDevices.Video;
                    PumpUntil(() => !recoveryWindow.IsWaitingForCapture && recoveryWindow.PresentedFrameCount >= 10, "automatic capture reconnection");
                }
                var recoveryView = (LibVLCSharp.WPF.VideoView)recoveryWindow.FindName("GpuVideo");
                var stalledPlayer = recoveryView.MediaPlayer!;
                var simulateStall = Task.Run(stalledPlayer.Stop);
                PumpUntil(() => simulateStall.IsCompleted, "simulate stopped frame delivery");
                simulateStall.GetAwaiter().GetResult();
                PumpUntil(() => recoveryView.MediaPlayer is not null && !ReferenceEquals(stalledPlayer, recoveryView.MediaPlayer)
                    && recoveryWindow.PresentedFrameCount >= 10, "frame-stall watchdog recovery");
                ((Button)recoveryWindow.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var stoppedWatch = Stopwatch.StartNew();
                PumpUntil(() => stoppedWatch.Elapsed.TotalSeconds > 3, "manual stop remains stopped");
                if (recoveryWindow.IsWaitingForCapture || ((Button)recoveryWindow.FindName("StopButton")).IsEnabled)
                    throw new Exception("Polling must not undo manual Stop.");
                var recoveryOpen = recoveryWindow.OpenFileAsync(Path.GetFullPath(recoveryMovie));
                PumpUntil(() => recoveryOpen.IsCompleted, "movie after capture recovery");
                recoveryOpen.GetAwaiter().GetResult();
                recoveryWindow.ReadVideoDevices = () => CaptureDevices.Video().Where(d => d.Name != recoveryDevice).ToArray();
                var movieWatch = Stopwatch.StartNew();
                PumpUntil(() => movieWatch.Elapsed.TotalSeconds > 3, "device removal during movie");
                if (((TextBlock)recoveryWindow.FindName("PositionText")).Text == "LIVE") throw new Exception("Device polling must not replace movie playback.");
                CloseWindow(recoveryWindow);
                Console.WriteLine("Auto preview, FPS apply, two simulated removal/reappearance cycles with real graph recreation, stopped-frame watchdog recovery, manual stop, and movie isolation passed. Physical unplug still needs a separate check.");
                return 0;
            }
            if (args is ["--probe-preferences", var persistedDevice, var preferenceMovie])
            {
                string preferenceTestFolder = Path.Combine(Path.GetTempPath(), "VideoCapture-DeviceSettings-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(preferenceTestFolder);
                try
                {
                    var devicePreferenceStore = new PreferencesStore(Path.Combine(preferenceTestFolder, "settings.json"));
                    var seedSettings = new AppPreferences
                    {
                        VideoDevice = persistedDevice, AudioDevice = "No audio", Volume = 42,
                        CaptureModes = new() { [persistedDevice] = new(1920, 1080, 333333, "MJPG") }
                    };
                    var seededPreferences = devicePreferenceStore.SaveAsync(seedSettings);
                    PumpUntil(() => seededPreferences.IsCompleted, "seed device preferences");
                    seededPreferences.GetAwaiter().GetResult();
                    var restoredWindow = new MainWindow(devicePreferenceStore, new MemoryPlaybackHistoryStore());
                    restoredWindow.Show();
                    PumpUntil(() => restoredWindow.SourceVideoInfo is { Width: 1920, Height: 1080, FramesPerSecond: > 29 and < 31 } && restoredWindow.PresentedFrameCount >= 10, "saved device automatic 1080p30 preview");
                    var restoredView = (LibVLCSharp.WPF.VideoView)restoredWindow.FindName("GpuVideo");
                    if (((Slider)restoredWindow.FindName("Volume")).Value != 42 || ((ComboBox)restoredWindow.FindName("CaptureFormats")).SelectedItem as string != "MJPG")
                        throw new Exception($"Restored format or actual playback volume did not match saved preferences. Format={((ComboBox)restoredWindow.FindName("CaptureFormats")).SelectedItem}, actual volume={restoredView.MediaPlayer!.Volume}, slider={((Slider)restoredWindow.FindName("Volume")).Value}");
                    var preferenceMovieOpen = restoredWindow.OpenFileAsync(Path.GetFullPath(preferenceMovie));
                    PumpUntil(() => preferenceMovieOpen.IsCompleted, "audio fixture startup for restored volume");
                    preferenceMovieOpen.GetAwaiter().GetResult();
                    PumpUntil(() => restoredView.MediaPlayer!.Volume == 42, "actual restored audio output volume");
                    var savedRateSelector = (ComboBox)restoredWindow.FindName("CaptureFrameRates");
                    savedRateSelector.SelectedItem = savedRateSelector.Items.Cast<CaptureFrameRate>().First(rate => Math.Abs(rate.FramesPerSecond - 60) < 0.01);
                    CloseWindow(restoredWindow);
                    var reopenedWindow = new MainWindow(devicePreferenceStore, new MemoryPlaybackHistoryStore());
                    reopenedWindow.Show();
                    PumpUntil(() => reopenedWindow.SourceVideoInfo is { Width: 1920, Height: 1080, FramesPerSecond: > 59 and < 61 } && reopenedWindow.PresentedFrameCount >= 10, "changed 60 fps restored after closing and reopening");
                    CloseWindow(reopenedWindow);
                    Console.WriteLine("Real-device preferences passed: automatic restored 1080p30/MJPG, actual volume, immediate-close FPS save, and reopening at 1080p60.");
                }
                finally { Directory.Delete(preferenceTestFolder, true); }
                return 0;
            }
            if (args is ["--probe-mode", var selectedDevice, var modeWidth, var modeHeight, var modeFps])
            {
                var modes = CaptureDevices.Modes(selectedDevice);
                var selected = modes.First(m => m.Width == int.Parse(modeWidth) && m.Height == int.Parse(modeHeight) && Math.Abs(m.FramesPerSecond - double.Parse(modeFps, System.Globalization.CultureInfo.InvariantCulture)) < 0.1);
                using var capture = new PlaybackSession("--aout=dummy");
                capture.OpenCapture(selectedDevice, null, mode: selected);
                WaitUntil(() => capture.Frames.FrameCount >= 10, capture, "selected capture mode");
                var measured = Stopwatch.StartNew();
                long initialFrames = capture.Frames.FrameCount;
                Thread.Sleep(5000);
                double delivered = (capture.Frames.FrameCount - initialFrames) / measured.Elapsed.TotalSeconds;
                var info = capture.GetVideoInfo() ?? throw new Exception("No source metadata.");
                Console.WriteLine($"Requested: {selected.Name}; actual source: {info.Width}x{info.Height} @ {info.FramesPerSecond:0.##}; decoded: {delivered:0.0} fps.");
                capture.Frames.TryRead(-1, (_, width, height, _, _) => Console.WriteLine($"Preview buffer: {width}x{height}."));
                if (info.Width != selected.Width || info.Height != selected.Height || Math.Abs(info.FramesPerSecond - selected.FramesPerSecond) > 0.2)
                    throw new Exception("The driver did not negotiate the requested capture mode.");
                return 0;
            }
            if (args is ["--list-modes", var modeDevice])
            {
                foreach (var available in CaptureDevices.Video()) Console.WriteLine("Device: " + available.Name);
                foreach (var mode in CaptureDevices.Modes(modeDevice)) Console.WriteLine(mode.Name);
                return 0;
            }
            if (args is ["--probe-camera", var device])
            {
                using var capture = new PlaybackSession("--aout=dummy");
                capture.OpenCapture(device, null);
                WaitUntil(() => capture.Frames.FrameCount >= 10, capture, "camera frames");
                Console.WriteLine($"Camera delivered {capture.Frames.FrameCount} preview frames. No image or recording saved.");
                return 0;
            }
            if (args.Length != 2) throw new ArgumentException("Usage: smoke <sample.mp4> <output-directory>");
            string input = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            var videos = CaptureDevices.Video();
            var audio = CaptureDevices.Audio();
            Console.WriteLine($"Device enumeration: {videos.Count} video, {audio.Count} audio. No device opened.");

            using (var session = new PlaybackSession("--vout=dummy", "--aout=dummy", "--avcodec-hw=none"))
            {
                session.Player.Mute = true;
                session.OpenFile(input);
                WaitUntil(() => session.Player.IsPlaying, session, "file playback");
                WaitUntil(() => session.Player.Time > 400, session, "playback progress");
                WaitUntil(() => session.Frames.FrameCount > 3, session, "decoded preview frames");
                if (!session.Player.IsSeekable) throw new Exception("Test file is not seekable.");
                session.Player.SetPause(true);
                WaitUntil(() => session.Player.State == VLCState.Paused, session, "pause");
                session.Player.Position = 0.5f;
                session.Player.SetPause(false);
                WaitUntil(() => session.Player.Time > 3000, session, "seek and resume");
                try { session.OpenFile(Path.Combine(output, "does-not-exist.mp4")); throw new Exception("Missing file was accepted."); }
                catch (FileNotFoundException) { }
                if (!session.HasMedia) throw new Exception("Missing file incorrectly stopped active playback.");
                session.Stop();
                if (session.HasMedia || session.IsRecording || session.IsLive) throw new Exception("Stop did not reset session state.");

                string recording = Path.Combine(output, "recording 'quoted'.mkv");
                File.Delete(recording);
                session.OpenFile(input, recording);
                WaitUntil(() => session.Player.IsPlaying, session, "recording start");
                WaitUntil(() => session.Player.State == VLCState.Ended, session, "recording completion");
                session.Stop();
                if (!File.Exists(recording) || new FileInfo(recording).Length < 10000)
                    throw new Exception("Recording output is missing or empty.");
                using var exclusive = new FileStream(recording, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Console.WriteLine($"Native H.264/AAC recording finalized: {exclusive.Length} bytes. Output handle released.");
            }

            var window = new MainWindow(new MemoryPreferencesStore(), new MemoryPlaybackHistoryStore());
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var renderer = (ComboBox)window.FindName("PreviewRenderer");
            PumpUntil(() => renderer.IsEnabled, "initial settings restore");
            renderer.SelectedIndex = 1;
            PumpUntil(() => renderer.IsEnabled && !window.IsGpuPreview, "compatibility renderer selection");
            var playback = window.OpenFileAsync(input);
            PumpUntil(() => playback.IsCompleted, "window playback startup");
            playback.GetAwaiter().GetResult();
            var videoImage = (Image)window.FindName("Video");
            PumpUntil(() => videoImage.Source is BitmapSource, "visible video frame");
            var frame = (BitmapSource)videoImage.Source;
            if (frame.PixelWidth != 640 || frame.PixelHeight != 360) throw new Exception("Decoded frame must preserve the source's visible dimensions without padding.");
            byte[] pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            if (pixels.Distinct().Count() < 16) throw new Exception("The WPF preview is blank or a solid color.");
            Console.WriteLine($"WPF preview verified: {frame.PixelWidth}x{frame.PixelHeight}, nonuniform decoded pixels.");
            PumpUntil(() => Math.Abs(videoImage.Width / videoImage.Height - 16d / 9) < 0.001, "source aspect ratio");
            var fit = (ComboBox)window.FindName("PictureFit");
            var scale = (Viewbox)window.FindName("VideoScale");
            fit.SelectedIndex = 1;
            if (scale.Stretch != Stretch.UniformToFill) throw new Exception("Fill must crop without distorting aspect ratio.");
            fit.SelectedIndex = 0;
            if (scale.Stretch != Stretch.Uniform) throw new Exception("Fit must preserve the entire picture.");
            SendKey(window, Key.F11);
            window.Dispatcher.Invoke(() => { window.UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
            foreach (string name in new[] { "HeaderPanel", "DevicePanel", "PlaybackControls" })
                if (((UIElement)window.FindName(name)).Visibility != Visibility.Collapsed)
                    throw new Exception("Fullscreen did not hide " + name);
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Fullscreen toolbar must start hidden.");
            window.ToggleFullscreenToolbar();
            PumpUntil(() => window.FloatingToolbar?.IsVisible == true, "floating fullscreen toolbar");
            if (((Button)window.FloatingToolbar!.FindName("CopyFrameButton")).IsEnabled != true)
                throw new Exception("Floating toolbar must permit frame capture while video is playing.");
            RenderElement((FrameworkElement)window.FloatingToolbar.Content, Path.Combine(output, "toolbar.png"));
            var root = (FrameworkElement)window.FindName("RootLayout");
            var viewport = (FrameworkElement)window.FindName("VideoBorder");
            if (Math.Abs(viewport.ActualWidth - root.ActualWidth) > 1 || Math.Abs(viewport.ActualHeight - root.ActualHeight) > 1)
                throw new Exception($"Fullscreen viewport {viewport.ActualWidth}x{viewport.ActualHeight} does not fill root {root.ActualWidth}x{root.ActualHeight}.");
            ((Button)window.FloatingToolbar!.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            if (((UIElement)window.FindName("DevicePanel")).Visibility != Visibility.Visible)
                throw new Exception("Exiting fullscreen did not restore controls.");
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Floating toolbar must hide outside fullscreen.");
            Console.WriteLine("Video-only fullscreen and control restoration verified.");
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1180, 760));
            content.Arrange(new Rect(0, 0, 1180, 760));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1180, 760, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 19, 24)), null, new Rect(0, 0, 1180, 760));
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, 1180, 760));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, "window.png"))) encoder.Save(file);
            ((Button)window.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => renderer.IsEnabled, "stop compatibility playback");
            renderer.SelectedIndex = 0;
            PumpUntil(() => renderer.IsEnabled && window.IsGpuPreview, "GPU renderer selection");
            var gpuPlayback = window.OpenFileAsync(input);
            PumpUntil(() => gpuPlayback.IsCompleted, "GPU file startup");
            gpuPlayback.GetAwaiter().GetResult();
            PumpUntil(() => window.PresentedFrameCount > 3, "GPU displayed frames");
            string snapshot = Path.Combine(output, "gpu-picture.png");
            File.Delete(snapshot);
            if (!window.TakeVideoSnapshot(snapshot)) throw new Exception("GPU picture snapshot failed.");
            PumpUntil(() => File.Exists(snapshot), "GPU snapshot output");
            var gpuPicture = new BitmapImage();
            gpuPicture.BeginInit();
            gpuPicture.CacheOption = BitmapCacheOption.OnLoad;
            gpuPicture.UriSource = new Uri(snapshot);
            gpuPicture.EndInit();
            if (gpuPicture.PixelWidth != 640 || gpuPicture.PixelHeight != 360)
                throw new Exception("GPU output did not preserve the source resolution.");
            Console.WriteLine("Native GPU video output verified at source resolution.");
            if (Environment.GetEnvironmentVariable("VIDEOCAPTURE_TEST_CLIPBOARD") == "1")
            {
                var previousClipboard = Clipboard.GetDataObject();
                try
                {
                    var copy = window.CopyFrameAsync();
                    PumpUntil(() => copy.IsCompleted, "frame copied to clipboard");
                    copy.GetAwaiter().GetResult();
                    var copied = Clipboard.GetImage();
                    if (copied?.PixelWidth != 640 || copied.PixelHeight != 360 || !Clipboard.ContainsData("PNG"))
                        throw new Exception("Clipboard must contain the native-resolution picture and PNG format.");
                    var fileCopy = SnapshotService.CopyFileAsync(Path.Combine(output, "recording 'quoted'.mkv"));
                    PumpUntil(() => fileCopy.IsCompleted, "recording file copied to clipboard");
                    fileCopy.GetAwaiter().GetResult();
                    if (!Clipboard.ContainsFileDropList() || Clipboard.GetFileDropList().Count != 1)
                        throw new Exception("Recording clipboard data must be a pasteable file.");
                    Console.WriteLine("Image and recording-file clipboard formats verified.");
                }
                finally
                {
                    if (previousClipboard is null) Clipboard.Clear();
                    else Clipboard.SetDataObject(previousClipboard, true);
                }
            }
            var gpuView = (LibVLCSharp.WPF.VideoView)window.FindName("GpuVideo");
            if (gpuView.MediaPlayer?.Hwnd == IntPtr.Zero) throw new Exception("GPU output is not attached to the app's native video surface.");
            fit.SelectedIndex = 1;
            if (string.IsNullOrEmpty(gpuView.MediaPlayer!.CropGeometry)) throw new Exception("GPU Fill did not apply aspect-preserving cropping.");
            fit.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(gpuView.MediaPlayer!.CropGeometry)) throw new Exception("GPU Fit did not clear cropping.");
            SendKey(window, Key.F11);
            window.Dispatcher.Invoke(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            if (fit.SelectedIndex != 1) throw new Exception("Fullscreen must default to aspect-preserving fill.");
            var monitorBounds = FullscreenDisplay.MonitorBounds(window);
            if (FullscreenDisplay.WindowBounds(window) != monitorBounds)
                throw new Exception("Fullscreen must cover the complete monitor rectangle, not the work area.");
            if (((FrameworkElement)window.FindName("DevicePanel")).Visibility != Visibility.Collapsed)
                throw new Exception("GPU fullscreen must hide the device panel.");
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("GPU fullscreen controls must start hidden.");
            window.ToggleFullscreenToolbar();
            if (window.FloatingToolbar?.IsVisible != true) throw new Exception("Showing GPU overlay controls failed.");
            window.ToggleFullscreenToolbar();
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Hiding GPU overlay controls failed.");
            SendKey(window, Key.Escape);
            CloseWindow(window);
            Console.WriteLine("WPF layout rendered. All smoke checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void WaitUntil(Func<bool> condition, PlaybackSession session, string operation)
    {
        Console.WriteLine("Checking " + operation);
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (session.Player.State == VLCState.Error) throw new Exception($"Native media error during {operation}.");
            if (timeout.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(operation);
            Thread.Sleep(30);
        }
    }

    private static void PumpUntil(Func<bool> condition, string operation, int timeoutSeconds = 20)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed.TotalSeconds > timeoutSeconds) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!condition()) throw new TimeoutException(operation);
    }

    private static void SendKey(Window window, Key key) => window.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static void CloseWindow(Window window)
    {
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        window.Close();
        if (window.IsVisible) Dispatcher.PushFrame(frame);
    }

    private static void RenderElement(FrameworkElement element, string path)
    {
        var size = new Size(Math.Ceiling(element.ActualWidth), Math.Ceiling(element.ActualHeight));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(size));
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
