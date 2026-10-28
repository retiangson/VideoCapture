using System.Windows;
using VideoCapture.Services;

namespace VideoCapture;

public partial class FloatingBrowser
{
    internal bool IsBrowserFullscreen { get; private set; }
    private Rect _browserWindowBounds;
    private WindowState _browserWindowState;

    internal void SetBrowserFullscreen(bool enabled)
    {
        if (enabled == IsBrowserFullscreen) return;
        if (enabled)
        {
            var monitor = FullscreenDisplay.MonitorBounds(this);
            _browserWindowState = WindowState;
            _browserWindowBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            IsBrowserFullscreen = true;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = MinHeight = 0;
            FullscreenDisplay.CoverMonitor(this, monitor);
        }
        else
        {
            IsBrowserFullscreen = false;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 460;
            MinHeight = TranscriptPanel.IsExpanded && TranscriptPanel.IsVisible ? 660 : 400;
            Left = _browserWindowBounds.Left; Top = _browserWindowBounds.Top;
            Width = _browserWindowBounds.Width; Height = _browserWindowBounds.Height;
            WindowState = _browserWindowState;
        }
        UpdateWindowButtons();
    }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => SetBrowserFullscreen(!IsBrowserFullscreen);
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (IsBrowserFullscreen) { SetBrowserFullscreen(false); WindowState = WindowState.Normal; }
        else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowButtons();
    }
    private void UpdateWindowButtons()
    {
        if (MaximizeButton is null) return;
        MaximizeButton.Content = IsBrowserFullscreen || WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = IsBrowserFullscreen || WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        BrowserFullscreenButton.ToolTip = IsBrowserFullscreen ? "Exit fullscreen (F11 / Esc)" : "Fullscreen (F11)";
    }
}
