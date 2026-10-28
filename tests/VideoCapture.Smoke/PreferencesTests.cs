using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoCapture;
using VideoCapture.Services;

internal sealed class MemoryPreferencesStore : IPreferencesStore
{
    internal AppPreferences Saved { get; private set; } = new();
    public Task<PreferencesLoadResult> LoadAsync() => Task.FromResult(new PreferencesLoadResult(Saved.Normalize()));
    public Task SaveAsync(AppPreferences preferences) { Saved = preferences.Normalize(); return Task.CompletedTask; }
}

internal sealed class MemoryPlaybackHistoryStore : IPlaybackHistoryStore
{
    public IReadOnlyList<PlaybackBookmark> Saved { get; private set; } = [];
    public Task<PlaybackHistoryLoadResult> LoadAsync() => Task.FromResult(new PlaybackHistoryLoadResult(Saved));
    public Task SaveAsync(IReadOnlyList<PlaybackBookmark> entries) { Saved = entries.ToArray(); return Task.CompletedTask; }
}

internal static class PreferencesTests
{
    internal static async Task RunAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "VideoCapture-Preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        var store = new PreferencesStore(path);
        try
        {
            var defaults = await store.LoadAsync();
            if (defaults.Warning is not null || defaults.Preferences.VideoDevice is not null) throw new Exception("Missing settings must use defaults.");
            var settings = new AppPreferences
            {
                VideoDevice = "Missing test capture device", AudioDevice = "Missing test audio", SoftwarePreview = true,
                Volume = 42, PictureFit = 1, BrowserOpacity = 0.55, MenuOpacity = 0.62,
                SpeechVoice = "Test installed voice", SpeechRate = 2, SpeechVolume = 61, SpeechRegion = 1,
                TranscriptionLanguage = "MS-1033-80-DESK",
                BrowserTabs = ["https://chatgpt.com/", "https://example.com/"], BrowserSelectedTab = 1,
                Playlist = [Path.Combine(folder, "not-connected-video.mp4")], PlaylistSelectedIndex = 0,
                CaptureModes = new() { ["Missing test capture device"] = new(1920, 1080, 333333, "MJPG") }
            };
            await store.SaveAsync(settings);
            var restored = (await store.LoadAsync()).Preferences;
            if (restored.VideoDevice != settings.VideoDevice || restored.CaptureModes[settings.VideoDevice] != settings.CaptureModes[settings.VideoDevice] || restored.Volume != 42 ||
                restored.MenuOpacity != 0.62 || restored.Playlist.Count != 1 || restored.SpeechVoice != settings.SpeechVoice || restored.SpeechRate != 2 || restored.SpeechVolume != 61 || restored.SpeechRegion != 1 || restored.TranscriptionLanguage != settings.TranscriptionLanguage || !restored.BrowserTabs.SequenceEqual(settings.BrowserTabs) || restored.BrowserSelectedTab != 1)
                throw new Exception("Settings file round trip failed.");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => store.SaveAsync(settings with { Volume = index })));
            if ((await store.LoadAsync()).Warning is not null || Directory.GetFiles(folder, "*.tmp").Length != 0)
                throw new Exception("Concurrent atomic saves left a partial file.");
            await File.WriteAllTextAsync(path, "{ broken json");
            if ((await store.LoadAsync()).Warning is null) throw new Exception("Corrupt settings must fall back with a diagnostic.");
            await File.WriteAllTextAsync(path, new string(' ', 2_097_153));
            if ((await store.LoadAsync()).Warning is null) throw new Exception("Oversized settings must fall back with a diagnostic.");
            await File.WriteAllTextAsync(path, "{\"Volume\":999,\"PictureFit\":9,\"BrowserOpacity\":0.01,\"CaptureModes\":null}");
            var normalized = (await store.LoadAsync()).Preferences;
            var speechNormalized = (new AppPreferences { SpeechRate = 99, SpeechVolume = -10, SpeechRegion = 9 }).Normalize();
            if (speechNormalized.SpeechRate != 5 || speechNormalized.SpeechVolume != 0 || speechNormalized.SpeechRegion != 0 || defaults.Preferences.SubtitleSpeechEnabled)
                throw new Exception("Speech preferences must use safe defaults and bounded values.");
            if (normalized.Volume != 100 || normalized.PictureFit != 0 || normalized.BrowserOpacity != 0.3 || normalized.CaptureModes.Count != 0)
                throw new Exception("Malformed preference values were not normalized.");
            var browserNormalized = (new AppPreferences { BrowserTabs = ["javascript:alert(1)", "file:///C:/test", "https://example.com/", "about:blank"], BrowserSelectedTab = -3 }).Normalize();
            if (!browserNormalized.BrowserTabs.SequenceEqual(["https://example.com/", "about:blank"]) || browserNormalized.BrowserSelectedTab != 0)
                throw new Exception("Saved browser sessions must reject unsafe URLs and invalid selection.");
            await store.SaveAsync(settings);
            var window = new MainWindow(store, new MemoryPlaybackHistoryStore());
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Loaded += (_, _) => loaded.TrySetResult();
            window.Show();
            await loaded.Task;
            await WaitAsync(() => ((ComboBox)window.FindName("VideoDevices")).SelectedItem is CaptureDevice, "restored missing device selection");
            if (((ComboBox)window.FindName("VideoDevices")).SelectedItem is not CaptureDevice { Name: "Missing test capture device" } ||
                ((ComboBox)window.FindName("AudioDevices")).SelectedItem is not CaptureDevice { Name: "Missing test audio" } ||
                ((ComboBox)window.FindName("PreviewRenderer")).SelectedIndex != 1 ||
                ((ComboBox)window.FindName("PictureFit")).SelectedIndex != 1 ||
                ((Slider)window.FindName("Volume")).Value != 42 || !window.IsWaitingForCapture)
                throw new Exception("Saved UI configuration did not restore without a connected camera.");
            ((Slider)window.FindName("Volume")).Value = 38;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var afterClose = (await store.LoadAsync()).Preferences;
            if (afterClose.Volume != 38 || afterClose.VideoDevice != settings.VideoDevice || afterClose.AudioDevice != settings.AudioDevice ||
                !afterClose.BrowserTabs.SequenceEqual(settings.BrowserTabs) || afterClose.BrowserSelectedTab != 1 || afterClose.BrowserOpacity != 0.55 || afterClose.CaptureModes[settings.VideoDevice] != settings.CaptureModes[settings.VideoDevice])
                throw new Exception("Immediate close did not flush changes or lost unavailable-device preferences.");
            Console.WriteLine("Preferences checks passed: defaults, JSON round trip, atomic concurrent writes, corrupt file, normalization, missing-device UI restore, immediate-close flush.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task WaitAsync(Func<bool> condition, string action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (timeout.IsCancellationRequested) throw new TimeoutException(action);
            await Task.Delay(25);
        }
    }
}
