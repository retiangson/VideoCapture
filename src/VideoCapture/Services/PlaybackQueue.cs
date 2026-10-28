using System.Collections.ObjectModel;
using System.ComponentModel;

namespace VideoCapture.Services;

internal sealed class PlaylistEntry(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Location => System.IO.Path.GetDirectoryName(Path) ?? "";
    private bool _current;
    public bool IsCurrent { get => _current; internal set { if (_current == value) return; _current = value; PropertyChanged?.Invoke(this, new(nameof(IsCurrent))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class PlaybackQueue
{
    public const int MaximumEntries = 200;
    public ObservableCollection<PlaylistEntry> Items { get; } = [];
    public PlaylistEntry? Current { get; private set; }
    public int CurrentIndex => Current is null ? -1 : Items.IndexOf(Current);
    public PlaylistEntry? Next => CurrentIndex + 1 < Items.Count ? Items[CurrentIndex + 1] : null;
    public PlaylistEntry? Previous => CurrentIndex > 0 ? Items[CurrentIndex - 1] : null;
    public PlaylistEntry? Add(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var existing = Items.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        if (Items.Count >= MaximumEntries) return null;
        var entry = new PlaylistEntry(path); Items.Add(entry); return entry;
    }
    public void SetCurrent(PlaylistEntry? entry)
    {
        if (entry is not null && !Items.Contains(entry)) throw new ArgumentException("The video is not in this playlist.", nameof(entry));
        if (Current is not null) Current.IsCurrent = false;
        Current = entry;
        if (Current is not null) Current.IsCurrent = true;
    }
    public void Remove(PlaylistEntry entry) { if (Current == entry) SetCurrent(null); Items.Remove(entry); }
    public void Move(PlaylistEntry entry, int direction)
    { int from = Items.IndexOf(entry), to = from + direction; if (from >= 0 && to >= 0 && to < Items.Count) Items.Move(from, to); }
    public void Clear() { SetCurrent(null); Items.Clear(); }
}
