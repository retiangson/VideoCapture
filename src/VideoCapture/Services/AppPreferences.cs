using System.IO;
using System.Text.Json;

namespace VideoCapture.Services;

internal sealed record AppPreferences
{
    public string? VideoDevice { get; init; }
    public string? AudioDevice { get; init; }
    public Dictionary<string, CaptureMode> CaptureModes { get; init; } = new(StringComparer.Ordinal);
    public bool SoftwarePreview { get; init; }
    public int PictureFit { get; init; }
    public bool MonitorCaptureAudio { get; init; } = true;
    public double Volume { get; init; } = 75;
    public double BrowserOpacity { get; init; } = 1;
    public double MenuOpacity { get; init; } = 0.96;
    public List<string> Playlist { get; init; } = [];
    public int PlaylistSelectedIndex { get; init; } = -1;
    public bool SubtitleSpeechEnabled { get; init; }
    public string? SpeechVoice { get; init; }
    public int SpeechRate { get; init; }
    public int SpeechVolume { get; init; } = 85;
    public int SpeechRegion { get; init; }
    public bool ShowFullscreenProgress { get; init; } = true;
    public bool TranscriptionMicrophone { get; init; }
    public string? TranscriptionLanguage { get; init; }
    public List<string> BrowserTabs { get; init; } = [];
    public int BrowserSelectedTab { get; init; }

    internal AppPreferences Normalize() => this with
    {
        BrowserTabs = (BrowserTabs ?? []).Where(url => url is { Length: > 0 and <= 8192 } &&
            (url == "about:blank" || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host))))
            .Take(32).ToList(),
        BrowserSelectedTab = Math.Clamp(BrowserSelectedTab, 0, 31),
        VideoDevice = ValidName(VideoDevice) ? VideoDevice : null,
        AudioDevice = ValidName(AudioDevice) ? AudioDevice : null,
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 100) : 75,
        BrowserOpacity = double.IsFinite(BrowserOpacity) ? Math.Clamp(BrowserOpacity, 0.3, 1) : 1,
        MenuOpacity = double.IsFinite(MenuOpacity) ? Math.Clamp(MenuOpacity, 0.35, 1) : 0.96,
        SpeechVoice = ValidName(SpeechVoice) ? SpeechVoice : null,
        TranscriptionLanguage = ValidName(TranscriptionLanguage) ? TranscriptionLanguage : null,
        SpeechRate = Math.Clamp(SpeechRate, -5, 5),
        SpeechVolume = Math.Clamp(SpeechVolume, 0, 100),
        SpeechRegion = SpeechRegion is >= 0 and <= 2 ? SpeechRegion : 0,
        Playlist = (Playlist ?? []).Where(ValidPlaylistPath).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(PlaybackQueue.MaximumEntries).ToList(),
        PlaylistSelectedIndex = Math.Clamp(PlaylistSelectedIndex, -1, PlaybackQueue.MaximumEntries - 1),
        PictureFit = PictureFit is 0 or 1 ? PictureFit : 0,
        CaptureModes = (CaptureModes ?? []).Where(entry => ValidName(entry.Key) && entry.Value is
            { Width: > 0 and <= 8192, Height: > 0 and <= 8192, FrameInterval: > 0 and <= 1_000_000_000, Chroma.Length: > 0 and <= 16 })
            .Take(64).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
    };
    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512;
    private static bool ValidPlaylistPath(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && path.Length <= 1024 && Path.IsPathFullyQualified(path) && Path.GetFullPath(path).Length <= 1024; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}

internal sealed record PreferencesLoadResult(AppPreferences Preferences, string? Warning = null);
internal interface IPreferencesStore
{
    Task<PreferencesLoadResult> LoadAsync();
    Task SaveAsync(AppPreferences preferences);
}

internal sealed class PreferencesStore : IPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;
    private readonly SemaphoreSlim _writes = new(1, 1);
    public PreferencesStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoCapture", "settings.json");

    public async Task<PreferencesLoadResult> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return new(new());
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
            if (stream.Length > 2_097_152) throw new InvalidDataException("The settings file is too large.");
            var preferences = await JsonSerializer.DeserializeAsync<AppPreferences>(stream, JsonOptions);
            return new((preferences ?? new()).Normalize());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return new(new(), "Saved settings could not be read; using defaults. " + ex.Message); }
    }

    public async Task SaveAsync(AppPreferences preferences)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(preferences.Normalize(), JsonOptions);
        await _writes.WaitAsync();
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            await File.WriteAllBytesAsync(temporary, json);
            // Same-directory rename replaces the old snapshot only after the complete
            // new file has been written. Interrupted saves leave the old file intact.
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _writes.Release(); }
        }
    }
}
