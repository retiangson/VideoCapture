using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VideoCapture;
using VideoCapture.Controls;
using VideoCapture.Services;

internal static class PlayerTests
{
    internal static async Task RunAsync(string input, string output)
    {
        Directory.CreateDirectory(output);
        string second = Path.Combine(output, "playlist-second.mp4");
        File.Copy(input, second, true);
        var queue = new PlaybackQueue();
        var firstEntry = queue.Add(input)!;
        if (queue.Add(input) != firstEntry || queue.Items.Count != 1) throw new Exception("Playlist must deduplicate paths.");
        var secondEntry = queue.Add(second)!;
        queue.SetCurrent(firstEntry);
        if (queue.Next != secondEntry || queue.Previous is not null) throw new Exception("Playlist next/previous are wrong.");
        queue.Move(secondEntry, -1);
        if (queue.CurrentIndex != 1 || queue.Previous != secondEntry) throw new Exception("Reordering lost the current video.");
        queue.Remove(secondEntry);
        if (queue.Current != firstEntry || queue.CurrentIndex != 0) throw new Exception("Removing a different entry changed current video.");
        queue.Clear();
        if (queue.Current is not null || queue.Items.Count != 0) throw new Exception("Clearing playlist must clear current video.");
        var settings = new MemoryPreferencesStore();
        await settings.SaveAsync(new AppPreferences { MenuOpacity = 0.58, Volume = 25, Playlist = [input, second], PlaylistSelectedIndex = 0 });
        var window = new MainWindow(settings, new MemoryPlaybackHistoryStore());
        window.Show();
        try
        {
            await WaitAsync(() => ((Button)window.FindName("OpenButton")).IsEnabled && window.Playlist.Items.Count == 2, "playlist restore");
            var volume = (Slider)window.FindName("Volume");
            CheckPointSelection(volume, 0.8);
            SendKey(window, Key.F11);
            await WaitAsync(() => window.WindowStyle == WindowStyle.None, "fullscreen");
            await Task.Delay(100);
            if (window.FloatingToolbar?.IsVisible == true) throw new Exception("Menu must start hidden.");
            window.ToggleFullscreenToolbar();
            var toolbar = window.FloatingToolbar!;
            if (Math.Abs(toolbar.Opacity - 0.58) > 0.01) throw new Exception("Entire menu opacity must restore from preferences.");
            CheckPointSelection((Slider)toolbar.FindName("OpacitySlider"), 0.5);
            if (Math.Abs(toolbar.Opacity - 0.675) > 0.02 || Math.Abs(((Slider)window.FindName("MenuOpacity")).Value - toolbar.Opacity) > 0.01)
                throw new Exception("Menu opacity slider must change the whole window and synchronize preferences.");
            CheckPointSelection((Slider)toolbar.FindName("MenuVolume"), 0.3);
            if (Math.Abs(volume.Value - 30) > 1) throw new Exception("Menu volume must synchronize with player volume.");
            SendKey(window, Key.Escape);
            await window.OpenFileAsync(input);
            await WaitAsync(() => window.PresentedFrameCount >= 3, "first playlist video");
            ((Button)window.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => window.Playlist.Current?.Path == second && window.PresentedFrameCount >= 3, "next video");
            ((Button)window.FindName("PreviousButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => window.Playlist.Current?.Path == input && window.PresentedFrameCount >= 3, "previous video");
            var unavailable = window.Playlist.Add(Path.Combine(output, "missing-video.mp4"))!;
            window.Playlist.Move(unavailable, -1);
            var view = (LibVLCSharp.WPF.VideoView)window.FindName("GpuVideo");
            view.MediaPlayer!.Position = 0.95f;
            await WaitAsync(() => window.Playlist.Current?.Path == second && window.PresentedFrameCount >= 3, "automatic playlist advance");
            double opacity = ((Slider)window.FindName("MenuOpacity")).Value;
            await CloseAsync(window);
            if (settings.Saved.Playlist.Count != 3 || Math.Abs(settings.Saved.MenuOpacity - opacity) > 0.001)
                throw new Exception("Playlist order and whole-menu opacity must persist on close.");
            var reopened = new MainWindow(settings, new MemoryPlaybackHistoryStore());
            reopened.Show();
            await WaitAsync(() => ((Button)reopened.FindName("OpenButton")).IsEnabled && reopened.Playlist.Items.Count == 3, "playlist reopen");
            if (reopened.Playlist.Current is not null || ((Button)reopened.FindName("StopButton")).IsEnabled)
                throw new Exception("Restoring playlist must not automatically play a movie.");
            if (!((Button)reopened.FindName("PauseButton")).IsEnabled) throw new Exception("Play must be available for the restored selected video.");
            await CloseAsync(reopened);
            Console.WriteLine("Player checks passed: queue ordering/deduplication, retained playlist, point-selected volume/menu opacity, full-window opacity, previous/next, automatic advance, and no movie autoplay on restore.");
        }
        finally { if (window.IsVisible) await CloseAsync(window); }
    }
    internal static void CheckPointSelection(Slider slider, double fraction)
    {
        slider.ApplyTemplate(); slider.UpdateLayout();
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        if (!SliderInteraction.GetDirectManipulation(slider) || track.ActualWidth <= track.Thumb.ActualWidth)
            throw new Exception("Interactive slider template is missing or has no usable track.");
        double x = track.Thumb.ActualWidth / 2 + (track.ActualWidth - track.Thumb.ActualWidth) * fraction;
        SliderInteraction.SetFromPoint(slider, track, new Point(x, track.ActualHeight / 2));
        double expected = slider.Minimum + (slider.Maximum - slider.Minimum) * fraction;
        if (Math.Abs(slider.Value - expected) > (slider.Maximum - slider.Minimum) * 0.02)
            throw new Exception($"Click-to-position slider expected {expected}, got {slider.Value}.");
    }
    private static void SendKey(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static async Task CloseAsync(Window window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(); window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
    }
    private static async Task WaitAsync(Func<bool> condition, string action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) { if (timeout.IsCancellationRequested) throw new TimeoutException(action); await Task.Delay(25); }
    }
}
