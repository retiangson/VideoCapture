using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoCapture.Services;

namespace VideoCapture;

public partial class FloatingBrowser : Window
{
    internal const string HomeAddress = "https://chatgpt.com/";
    public event EventHandler? ScreenTextRequested;
    public event EventHandler? CopyVideoFrameRequested, CopyVideoTextRequested, RecordVideoRequested;
    public event EventHandler? AudioTextRequested, CancelAudioRequested, CopyTranscriptRequested, TranscriptLanguageRequested;
    private bool _updatingTranscriptLanguage;
    internal bool MicrophoneSelected => TranscriptSource.SelectedIndex == 1;
    internal void SetMicrophoneSource(bool microphone) => TranscriptSource.SelectedIndex = microphone ? 1 : 0;
    internal event EventHandler? TranscriptSourceRequested;
    internal string Transcript => TranscriptText.Text;
    internal string? TranscriptLanguageId => (TranscriptLanguage.SelectedItem as TranscriptionLanguage)?.Id;
    internal void SetTranscriptLanguages(IReadOnlyList<TranscriptionLanguage> languages, string? selected)
    {
        _updatingTranscriptLanguage = true;
        try
        {
            TranscriptLanguage.ItemsSource = languages;
            TranscriptLanguage.SelectedItem = languages.FirstOrDefault(l => l.Id == selected) ?? languages.FirstOrDefault();
        }
        finally { _updatingTranscriptLanguage = false; }
    }
    internal void SetAudioState(bool canStart, bool capturing, bool processing, string elapsed)
    {
        StartAudioButton.IsEnabled = canStart;
        StartAudioButton.Content = capturing ? "Stop & transcribe · " + elapsed : processing ? "Transcribing…" : "Start capture";
        AudioTextLabel.Text = capturing ? "Audio to text · capturing" : processing ? "Audio to text · processing" : "Audio to text";
        StartAudioButton.ToolTip = capturing ? "Stop and transcribe the captured audio" : MicrophoneSelected
            ? "Listen to the default Windows microphone using offline dictation" : "Capture the playing video's audio only";
        TranscriptSource.IsEnabled = !capturing && !processing;
        TranscriptLanguage.IsEnabled = !capturing && !processing && TranscriptLanguage.Items.Count > 0;
        CancelAudioButton.IsEnabled = capturing || processing;
        TranscriptText.IsReadOnly = capturing || processing;
    }
    internal void SetMicrophoneFeedback(MicrophoneFeedback feedback)
    {
        MicrophoneFeedbackPanel.Visibility = Visibility.Visible;
        MicrophoneLevel.Value = feedback.Level;
        MicrophonePreview.Text = feedback.Text;
        SetTranscriptStatus(feedback.Status);
    }
    private void MicrophoneSettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { SetTranscriptStatus("Could not open Windows input settings: " + ex.Message); }
    }
    internal void ResetMicrophoneFeedback()
    {
        MicrophoneLevel.Value = 0;
        MicrophonePreview.Text = "Speak, then choose Stop & transcribe for the final Whisper result.";
        MicrophoneFeedbackPanel.Visibility = MicrophoneSelected ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void SetTranscript(string text) => TranscriptText.Text = text;
    internal void SetTranscriptStatus(string text) { TranscriptStatus.Text = text; TranscriptPanel.ToolTip = text; }
    private void AudioText_Click(object sender, RoutedEventArgs e)
    {
        bool show = TranscriptPanel.Visibility != Visibility.Visible;
        TranscriptPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TranscriptPanel.IsExpanded = show;
        if (!show && !IsBrowserFullscreen) MinHeight = 400;
    }
    private void AudioStart_Click(object sender, RoutedEventArgs e) => AudioTextRequested?.Invoke(this, EventArgs.Empty);
    private void TranscriptSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneLevel is not null) ResetMicrophoneFeedback();
        TranscriptSourceRequested?.Invoke(this, EventArgs.Empty);
    }
    private void CancelAudio_Click(object sender, RoutedEventArgs e) => CancelAudioRequested?.Invoke(this, EventArgs.Empty);
    private void CopyTranscript_Click(object sender, RoutedEventArgs e) => CopyTranscriptRequested?.Invoke(this, EventArgs.Empty);
    private void TranscriptLanguage_Changed(object sender, SelectionChangedEventArgs e) { if (!_updatingTranscriptLanguage) TranscriptLanguageRequested?.Invoke(this, EventArgs.Empty); }
    private void TranscriptText_Changed(object sender, TextChangedEventArgs e) { if (CopyTranscriptButton is not null) CopyTranscriptButton.IsEnabled = !string.IsNullOrWhiteSpace(TranscriptText.Text); }
    private void TranscriptPanel_Expanded(object sender, RoutedEventArgs e) { if (!IsBrowserFullscreen) MinHeight = 660; }
    private void TranscriptPanel_Collapsed(object sender, RoutedEventArgs e) { if (!IsBrowserFullscreen) MinHeight = 400; }
    public event EventHandler? OpacityPreferenceChanged;
    internal event EventHandler? SessionChanged;
    private List<string> _restoreTabs = [];
    private int _restoreSelected;
    internal bool SessionReady { get; private set; }
    internal List<string> SessionTabs => _tabs.Select(t => t.SavedAddress).ToList();
    internal int SelectedTabIndex => Tabs.SelectedIndex;
    internal void RestoreTabs(IEnumerable<string> addresses, int selected)
    {
        if (_started) throw new InvalidOperationException("Restore tabs before showing the browser.");
        _restoreTabs = addresses.Where(url => url == "about:blank" || TryAddress(url, out _)).Take(32).ToList();
        _restoreSelected = selected;
    }
    private void NotifySessionChanged() { if (SessionReady && !_disposed) SessionChanged?.Invoke(this, EventArgs.Empty); }
    private readonly List<BrowserTab> _tabs = [];
    private readonly string _initialAddress, _profileDirectory;
    private CoreWebView2Environment? _environment;
    private bool _disposed, _started;
    internal Task Initialization { get; private set; } = Task.CompletedTask;
    private BrowserTab? ActiveTab => (Tabs.SelectedItem as TabItem)?.Tag as BrowserTab;
    internal WebView2CompositionControl? ActiveBrowser => ActiveTab?.View;
    internal int TabCount => _tabs.Count;
    internal void SetVideoActions(bool canCapture, bool canRecord, bool recording, string elapsed, bool savingClip = false)
    {
        CopyVideoFrameButton.IsEnabled = CopyVideoTextButton.IsEnabled = canCapture;
        RecordVideoButton.IsEnabled = canRecord;
        RecordVideoLabel.Text = recording ? "Stop recording" : savingClip ? "Saving clip…" : "Record video";
        RecordVideoButton.ToolTip = recording ? "Stop and save the video recording" : "Record the playing video and its source audio; browser is excluded";
        VideoRecordingTime.Text = recording ? elapsed : "";
    }
    internal void SetVideoFeedback(string message) => VideoFeedback.Text = message;
    private void CopyVideoFrame_Click(object sender, RoutedEventArgs e) => CopyVideoFrameRequested?.Invoke(this, EventArgs.Empty);
    private void CopyVideoText_Click(object sender, RoutedEventArgs e) => CopyVideoTextRequested?.Invoke(this, EventArgs.Empty);
    private void RecordVideo_Click(object sender, RoutedEventArgs e) => RecordVideoRequested?.Invoke(this, EventArgs.Empty);

    public FloatingBrowser(double opacity = 1) : this(opacity, HomeAddress, Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoCapture", "BrowserProfile")) { }

    internal FloatingBrowser(double opacity, string initialAddress, string profileDirectory)
    {
        _initialAddress = initialAddress;
        _profileDirectory = profileDirectory;
        InitializeComponent();
        StateChanged += (_, _) => UpdateWindowButtons();
        Transparency.Value = Math.Clamp(opacity, 0.3, 1);
        Loaded += (_, _) => { if (!_started) { _started = true; Initialization = InitializeAsync(); } };
        Closing += (_, e) => { if (!_disposed) { e.Cancel = true; Hide(); } };
    }

    private async Task InitializeAsync()
    {
        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _profileDirectory);
            if (_disposed) return;
            foreach (string address in _restoreTabs.Count > 0 ? _restoreTabs : [_initialAddress])
            {
                await AddTabAsync(address);
                if (_disposed) return;
            }
            Tabs.SelectedIndex = Math.Clamp(_restoreSelected, 0, _tabs.Count - 1);
            SessionReady = true;
            NewTabButton.IsEnabled = true;
            NotifySessionChanged();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_disposed) Feedback.Text = "Browser unavailable. Check Microsoft Edge WebView2 Runtime. " + ex.Message; }
    }

    internal async Task<WebView2CompositionControl?> AddTabAsync(string? address = HomeAddress)
    {
        if (_disposed || _environment is null) return null;
        if (address is not null && address != "about:blank")
        {
            if (!TryAddress(address, out var uri)) throw new ArgumentException("Enter a valid HTTP or HTTPS address.", nameof(address));
            address = uri!.AbsoluteUri;
        }
        var tab = new BrowserTab { SavedAddress = address ?? "about:blank" };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(tab.Title);
        var close = new Button { Content = "×", Padding = new Thickness(0), Margin = new Thickness(5, 0, 0, 0), Width = 18, Height = 18, MinHeight = 18, FontSize = 11, ToolTip = "Close tab (Ctrl+W)", Focusable = false };
        close.Click += async (_, e) => { e.Handled = true; await RunBrowserActionAsync(() => CloseTabAsync(tab.View)); };
        header.Children.Add(close);
        tab.Item.Header = header;
        tab.Item.Tag = tab;
        _tabs.Add(tab);
        BrowserHost.Children.Add(tab.View);
        Tabs.Items.Add(tab.Item);
        Tabs.SelectedItem = tab.Item;
        try
        {
            await tab.View.EnsureCoreWebView2Async(_environment);
            if (_disposed || !_tabs.Contains(tab)) return null;
            ConfigureTab(tab);
            if (address is not null) tab.View.CoreWebView2.Navigate(address);
            UpdateNavigation();
            return tab.View;
        }
        catch when (_disposed || !_tabs.Contains(tab)) { RemoveTab(tab); return null; }
        catch { RemoveTab(tab); throw; }
    }

    private void ConfigureTab(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;
        core.NewWindowRequested += async (_, e) =>
        {
            e.Handled = true;
            if (e.Uri != "about:blank" && !TryAddress(e.Uri, out var popupAddress))
            { SetFeedback(tab, "Blocked an unsupported popup address."); return; }
            var deferral = e.GetDeferral();
            try
            {
                // NewWindow assignment retains window.opener for sign-in popups.
                var view = await AddTabAsync(null);
                if (!_disposed && _tabs.Contains(tab) && view?.CoreWebView2 is { } popup) e.NewWindow = popup;
                else if (view is not null) await CloseTabAsync(view);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { if (!_disposed) SetFeedback(tab, "Could not open tab: " + ex.Message); }
            finally
            {
                try { deferral.Complete(); }
                catch (Exception ex) when ((_disposed || !_tabs.Contains(tab)) && ex is ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
            }
        };
        core.NavigationStarting += (_, e) =>
        {
            bool local = e.Uri == tab.LocalPageUri;
            if (local) tab.LocalPageUri = null;
            if (!local && e.Uri != "about:blank" && !TryAddress(e.Uri, out var navigationAddress))
            { e.Cancel = true; SetFeedback(tab, "Only HTTP and HTTPS navigation is supported."); }
            else SetFeedback(tab, "Loading…");
        };
        core.NavigationCompleted += (_, e) =>
        {
            SetFeedback(tab, e.IsSuccess ? "Ready · Ctrl+T new tab · Ctrl+W close tab · Ctrl+Shift+B hide" : "Navigation failed: " + e.WebErrorStatus);
            UpdateTitle(tab);
            if (tab == ActiveTab) UpdateNavigation();
        };
        core.DocumentTitleChanged += (_, _) => UpdateTitle(tab);
        core.WindowCloseRequested += async (_, _) => await RunBrowserActionAsync(() => CloseTabAsync(tab.View));
        core.SourceChanged += (_, _) =>
        {
            string source = core.Source;
            tab.SavedAddress = source == "about:blank" ? source : TryAddress(source, out var uri) ? uri!.AbsoluteUri : HomeAddress;
            NotifySessionChanged();
            if (tab == ActiveTab) UpdateNavigation();
        };
        core.HistoryChanged += (_, _) => { if (tab == ActiveTab) UpdateNavigation(); };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => { e.Cancel = true; SetFeedback(tab, "Downloads are not supported in the floating browser."); };
        core.ProcessFailed += (_, e) => SetFeedback(tab, "Browser process failed: " + e.ProcessFailedKind + ". Reload or close this tab.");
    }

    private void SetFeedback(BrowserTab tab, string message)
    { tab.Feedback = message; if (tab == ActiveTab) Feedback.Text = message; }
    private void UpdateTitle(BrowserTab tab)
    {
        if (_disposed || !_tabs.Contains(tab)) return;
        string title = tab.View.CoreWebView2.DocumentTitle;
        tab.Title.Text = string.IsNullOrWhiteSpace(title) ? "New tab" : title;
        tab.Title.ToolTip = title;
    }
    private void UpdateNavigation()
    {
        if (_disposed || Address is null) return;
        var core = ActiveBrowser?.CoreWebView2;
        BackButton.IsEnabled = core?.CanGoBack == true;
        ForwardButton.IsEnabled = core?.CanGoForward == true;
        Address.Text = core is null || core.Source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "" : core.Source;
        Feedback.Text = ActiveTab?.Feedback ?? "Starting browser…";
        NotifySessionChanged();
    }
    private void Tab_Changed(object sender, SelectionChangedEventArgs e)
    {
        foreach (var tab in _tabs) tab.View.Visibility = tab == ActiveTab ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigation();
    }
    internal void SelectTab(WebView2CompositionControl view) => Tabs.SelectedItem = _tabs.First(tab => tab.View == view).Item;
    internal async Task CloseTabAsync(WebView2CompositionControl view)
    {
        var tab = _tabs.FirstOrDefault(tab => tab.View == view);
        if (tab is null) return;
        RemoveTab(tab);
        if (!_disposed && _tabs.Count == 0) await AddTabAsync();
    }
    private void RemoveTab(BrowserTab tab)
    {
        if (!_tabs.Remove(tab)) return;
        int index = Tabs.Items.IndexOf(tab.Item);
        Tabs.Items.Remove(tab.Item);
        BrowserHost.Children.Remove(tab.View);
        tab.View.Dispose();
        if (Tabs.SelectedItem is null && Tabs.Items.Count > 0) Tabs.SelectedIndex = Math.Min(index, Tabs.Items.Count - 1);
        UpdateNavigation();
    }
    private async Task RunBrowserActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_disposed) Feedback.Text = "Browser action failed: " + ex.Message; }
    }
    private async void NewTab_Click(object sender, RoutedEventArgs e) => await RunBrowserActionAsync(async () => { await AddTabAsync(); });
    internal static bool TryAddress(string text, out Uri? uri)
    {
        text = text.Trim();
        if (!text.Contains("://", StringComparison.Ordinal) && !text.Contains(':')) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);
    }
    internal void ShowLocalPage(string html)
    {
        var tab = ActiveTab ?? throw new InvalidOperationException("No active browser tab.");
        tab.LocalPageUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        tab.View.CoreWebView2.NavigateToString(html);
    }
    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveBrowser?.CoreWebView2 is not { } core) { Feedback.Text = "Browser is still starting or is unavailable."; return; }
        if (TryAddress(Address.Text, out var uri)) core.Navigate(uri!.AbsoluteUri);
        else Feedback.Text = "Enter a valid HTTP or HTTPS address.";
    }
    private void Address_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Go_Click(sender, e); e.Handled = true; } }
    private void Back_Click(object sender, RoutedEventArgs e) { if (ActiveBrowser?.CoreWebView2?.CanGoBack == true) ActiveBrowser.CoreWebView2.GoBack(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (ActiveBrowser?.CoreWebView2?.CanGoForward == true) ActiveBrowser.CoreWebView2.GoForward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) => ActiveBrowser?.CoreWebView2?.Reload();
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    internal void AdjustOpacity(int wheelDelta) => Transparency.Value = Math.Clamp(
        Transparency.Value + wheelDelta / 120d * .05, Transparency.Minimum, Transparency.Maximum);
    private void Browser_OpacityWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Media.Visual source && (source == BrowserHost || BrowserHost.IsAncestorOf(source))) return;
        AdjustOpacity(e.Delta);
        e.Handled = true;
    }
    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Opacity = Math.Clamp(e.NewValue, 0.3, 1);
        OpacityPreferenceChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not TextBlock || e.LeftButton != MouseButtonState.Pressed) return;
        if (e.ClickCount == 2) { SetBrowserFullscreen(!IsBrowserFullscreen); e.Handled = true; }
        else if (!IsBrowserFullscreen && WindowState == WindowState.Normal) DragMove();
    }
    private async void Browser_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { SetBrowserFullscreen(!IsBrowserFullscreen); e.Handled = true; return; }
        if (e.Key == Key.Escape && IsBrowserFullscreen) { SetBrowserFullscreen(false); e.Handled = true; return; }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.B) { Hide(); e.Handled = true; return; }
            if (e.Key == Key.X) { ScreenTextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (e.Key == Key.T) { e.Handled = true; await RunBrowserActionAsync(async () => { await AddTabAsync(); }); }
        else if (e.Key == Key.W && ActiveBrowser is { } view) { e.Handled = true; await RunBrowserActionAsync(() => CloseTabAsync(view)); }
        else if (e.Key == Key.L) { NavigationPanel.IsExpanded = true; Address.Focus(); Address.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Tab && Tabs.Items.Count > 0)
        {
            int direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            Tabs.SelectedIndex = (Tabs.SelectedIndex + direction + Tabs.Items.Count) % Tabs.Items.Count;
            e.Handled = true;
        }
    }
    internal void Shutdown()
    {
        _disposed = true;
        SessionReady = false;
        foreach (var tab in _tabs.ToArray()) RemoveTab(tab);
        Close();
    }
    private sealed class BrowserTab
    {
        public TabItem Item { get; } = new();
        public WebView2CompositionControl View { get; } = new();
        public TextBlock Title { get; } = new() { Text = "New tab", MaxWidth = 112, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        public string Feedback { get; set; } = "Starting tab…";
        public string? LocalPageUri { get; set; }
        public string SavedAddress { get; set; } = "about:blank";
    }
}
