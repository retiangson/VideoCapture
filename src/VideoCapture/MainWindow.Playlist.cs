using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VideoCapture.Services;

namespace VideoCapture;

public partial class MainWindow
{
    private readonly PlaybackQueue _playlist = new();
    internal PlaybackQueue Playlist => _playlist;
    private const string VideoFilter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.ts;*.m2ts;*.wmv;*.mpg;*.mpeg;*.flv;*.ogv|All files|*.*";
    private void InitializePlaylist()
    {
        PlaylistList.ItemsSource = _playlist.Items;
        _playlist.Items.CollectionChanged += (_, _) => { UpdatePlaylistControls(); QueuePreferencesSave(); };
    }
    internal async Task AddPlaylistFilesAsync(IEnumerable<string> paths, bool playFirst)
    {
        if (_busy || _closing || IsVideoRecording) return;
        var files = await Task.Run(() => paths.Select(Path.GetFullPath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        if (_closing || _busy || IsVideoRecording) return;
        PlaylistEntry? first = null;
        foreach (string path in files) { var item = _playlist.Add(path); first ??= item; }
        LibraryTabs.SelectedItem = PlaylistTab;
        if (first is not null) PlaylistList.SelectedItem = first;
        ShowCaptureFeedback(files.Length == 0 ? "No readable video files were found." : $"{_playlist.Items.Count} videos in playlist · double-click to play");
        UpdateControls();
        if (playFirst && first is not null) await OpenFileAsync(first.Path);
    }
    private async void AddPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = VideoFilter, Multiselect = true, Title = "Add videos to playlist" };
        if (dialog.ShowDialog(this) == true) await RunPlaylistActionAsync(() => AddPlaylistFilesAsync(dialog.FileNames, false));
    }
    private async Task RunPlaylistActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowCaptureFeedback("Playlist: " + ex.Message); }
    }
    private async void PlayPlaylist_Click(object sender, RoutedEventArgs e)
    { if (PlaylistList.SelectedItem is PlaylistEntry item) await OpenFileAsync(item.Path); }
    private async void Playlist_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem && PlaylistList.SelectedItem is PlaylistEntry item)
        { e.Handled = true; await OpenFileAsync(item.Path); }
    }
    private void Playlist_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PlayPlaylist_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.Delete) { RemovePlaylist_Click(sender, e); e.Handled = true; }
    }
    private void Playlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (PauseButton is not null) UpdateControls(); else UpdatePlaylistControls(); QueuePreferencesSave(); }
    private async void Next_Click(object sender, RoutedEventArgs e)
    { if (_playlist.Next is { } next && !IsVideoRecording) await OpenFileAsync(next.Path); }
    private async void Previous_Click(object sender, RoutedEventArgs e)
    { if (_playlist.Previous is { } previous && !IsVideoRecording) await OpenFileAsync(previous.Path); }
    private async void RemovePlaylist_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (IsVideoRecording) return;
        if (PlaylistList.SelectedItem is not PlaylistEntry selected) return;
        if (_playlist.Current == selected && _session is { IsLive: false, HasMedia: true }) await StopAsync();
        _playlist.Remove(selected);
    });
    private async void ClearPlaylist_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (IsVideoRecording) return;
        if (_session is { IsLive: false, HasMedia: true }) await StopAsync();
        _playlist.Clear();
    });
    private void PlaylistUp_Click(object sender, RoutedEventArgs e) => MovePlaylist(-1);
    private void PlaylistDown_Click(object sender, RoutedEventArgs e) => MovePlaylist(1);
    private void MovePlaylist(int direction)
    {
        if (_busy || _closing || IsVideoRecording || PlaylistList.SelectedItem is not PlaylistEntry selected) return;
        _playlist.Move(selected, direction);
        PlaylistList.SelectedItem = selected;
    }
    private void UpdatePlaylistControls()
    {
        if (PlaylistList is null) return;
        bool ready = _preferencesLoaded && !_busy && !_closing && !IsVideoRecording;
        PlaylistList.IsEnabled = ready;
        int selected = PlaylistList.SelectedIndex;
        PlaylistCount.Text = $"{_playlist.Items.Count} video{(_playlist.Items.Count == 1 ? "" : "s")}";
        PlaylistEmpty.Visibility = _playlist.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.IsEnabled = ready && _playlist.Previous is not null;
        NextButton.IsEnabled = ready && _playlist.Next is not null;
        AddPlaylistButton.IsEnabled = ready;
        PlayPlaylistButton.IsEnabled = RemovePlaylistButton.IsEnabled = ready && selected >= 0;
        ClearPlaylistButton.IsEnabled = ready && _playlist.Items.Count > 0;
        MoveUpButton.IsEnabled = ready && selected > 0;
        MoveDownButton.IsEnabled = ready && selected >= 0 && selected < _playlist.Items.Count - 1;
    }
    private void MenuOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_fullscreenToolbar is not null) _fullscreenToolbar.SetLevels(e.NewValue, Volume.Value, SpeechVolume.Value);
        QueuePreferencesSave();
    }
    private Task AdvancePlaylistAfterEndAsync() => RunAsync(async () =>
    {
        string[] remaining = _playlist.Items.Skip(_playlist.CurrentIndex + 1).Select(item => item.Path).ToArray();
        // Clear the ended state first; missing queue entries must not cause
        // repeated retries on every progress tick.
        await StopAsync();
        string? next = await Task.Run(() => remaining.FirstOrDefault(File.Exists));
        if (next is not null) await OpenFileCoreAsync(next);
        else ShowCaptureFeedback("Playlist finished · remaining files are unavailable.");
    }, showError: false);
}
