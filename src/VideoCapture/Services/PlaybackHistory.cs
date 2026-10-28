using System.IO;
using System.Text.Json;

namespace VideoCapture.Services;

internal sealed record PlaybackFile(string Path, long Size, long LastWriteUtcTicks)
{
    public static PlaybackFile Read(string path)
    {
        var file = new FileInfo(System.IO.Path.GetFullPath(path));
        if (!file.Exists) throw new FileNotFoundException("The movie file no longer exists.", path);
        return new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
    }
}

internal sealed record PlaybackBookmark(PlaybackFile File, long PositionMs, long DurationMs, DateTimeOffset UpdatedUtc);

internal sealed class PlaybackHistory
{
    public const int MaximumEntries = 1000;
    private readonly Dictionary<string, PlaybackBookmark> _bookmarks = new(StringComparer.OrdinalIgnoreCase);
    public void Restore(IEnumerable<PlaybackBookmark> entries)
    {
        _bookmarks.Clear();
        foreach (var entry in entries.Where(IsValid).OrderByDescending(e => e.UpdatedUtc))
        {
            _bookmarks.TryAdd(System.IO.Path.GetFullPath(entry.File.Path), entry);
            if (_bookmarks.Count >= MaximumEntries) break;
        }
    }
    public long ResumePosition(PlaybackFile file)
    {
        return _bookmarks.TryGetValue(file.Path, out var saved) && saved.File.Size == file.Size &&
            saved.File.LastWriteUtcTicks == file.LastWriteUtcTicks ? saved.PositionMs : 0;
    }
    public void Remember(PlaybackFile file, long positionMs, long durationMs, bool completed = false)
    {
        if (durationMs <= 0 || positionMs < 0) return;
        long position = Math.Min(positionMs, durationMs);
        // Mark the final 2% (at most ten seconds) as completed. This also handles
        // closing just before the native end-of-media event arrives.
        if (completed || durationMs - position <= Math.Min(10_000, durationMs / 50)) position = 0;
        _bookmarks[file.Path] = new(file, position, durationMs, DateTimeOffset.UtcNow);
        if (_bookmarks.Count > MaximumEntries)
            _bookmarks.Remove(_bookmarks.Values.MinBy(e => e.UpdatedUtc)!.File.Path);
    }
    public IReadOnlyList<PlaybackBookmark> Snapshot() => _bookmarks.Values.OrderByDescending(e => e.UpdatedUtc).ToArray();
    private static bool IsValid(PlaybackBookmark? entry)
    {
        if (entry?.File is not { Path: { Length: > 0 and <= 4096 }, Size: >= 0, LastWriteUtcTicks: > 0 } file ||
            entry.DurationMs <= 0 || entry.DurationMs > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond || entry.PositionMs < 0 || entry.PositionMs > entry.DurationMs) return false;
        try { return System.IO.Path.IsPathFullyQualified(file.Path) && System.IO.Path.GetFullPath(file.Path).Length <= 4096 && !string.IsNullOrWhiteSpace(System.IO.Path.GetFileName(file.Path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}

internal sealed record PlaybackHistoryLoadResult(IReadOnlyList<PlaybackBookmark> Entries, string? Warning = null);
internal interface IPlaybackHistoryStore
{
    Task<PlaybackHistoryLoadResult> LoadAsync();
    Task SaveAsync(IReadOnlyList<PlaybackBookmark> entries);
}

internal sealed class PlaybackHistoryStore(string? path = null) : IPlaybackHistoryStore
{
    internal const int MaximumFileBytes = 4_194_304;
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoCapture", "playback-history.json");
    private readonly SemaphoreSlim _writes = new(1, 1);
    public async Task<PlaybackHistoryLoadResult> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return new([]);
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("The playback history file is too large.");
            var entries = await JsonSerializer.DeserializeAsync<List<PlaybackBookmark>>(stream) ?? [];
            var history = new PlaybackHistory(); history.Restore(entries);
            return new(history.Snapshot());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return new([], "Playback history could not be read; starting a new history. " + ex.Message); }
    }
    public async Task SaveAsync(IReadOnlyList<PlaybackBookmark> entries)
    {
        await _writes.WaitAsync();
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var history = new PlaybackHistory(); history.Restore(entries);
            byte[] json = await Task.Run(() =>
            {
                var snapshot = history.Snapshot();
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                while (data.Length > MaximumFileBytes)
                {
                    snapshot = snapshot.Take(Math.Max(0, (int)(snapshot.Count * (double)MaximumFileBytes / data.Length) - 1)).ToArray();
                    data = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                }
                return data;
            });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            await File.WriteAllBytesAsync(temporary, json);
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _writes.Release(); }
        }
    }
}
