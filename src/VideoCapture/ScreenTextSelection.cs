using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VideoCapture.Services;

namespace VideoCapture;

internal sealed class ScreenTextSelection : Window
{
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Rectangle _outline = new() { Stroke = Brushes.Cyan, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 0, 180, 220)) };
    private readonly BitmapSource _image;
    private Point? _start;
    public BitmapSource? Selection { get; private set; }
    public ScreenTextSelection(BitmapSource image, Rect bounds)
    {
        _image = image;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        MinWidth = MinHeight = 0;
        var grid = new Grid();
        grid.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        grid.Children.Add(_canvas);
        _canvas.Children.Add(_outline);
        grid.Children.Add(new TextBlock { Text = "Drag over text to copy · Esc cancels", Background = Brushes.Black, Foreground = Brushes.White,
            Padding = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        Content = grid;
        SourceInitialized += (_, _) => FullscreenDisplay.CoverMonitor(this, bounds);
        Loaded += (_, _) => FullscreenDisplay.CoverMonitor(this, bounds);
        _canvas.MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); _canvas.CaptureMouse(); };
        _canvas.MouseMove += (_, e) =>
        {
            if (_start is not { } start) return;
            Rect rect = new(start, e.GetPosition(_canvas));
            Canvas.SetLeft(_outline, rect.Left); Canvas.SetTop(_outline, rect.Top);
            _outline.Width = rect.Width; _outline.Height = rect.Height;
        };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } start) return;
            _canvas.ReleaseMouseCapture();
            Rect rect = new(start, e.GetPosition(_canvas));
            var pixels = PixelSelection(rect, new Size(_canvas.ActualWidth, _canvas.ActualHeight), image.PixelWidth, image.PixelHeight);
            if (!pixels.IsEmpty)
            {
                Selection = new CroppedBitmap(_image, pixels);
                Selection.Freeze();
                DialogResult = true;
            }
            else { _start = null; _outline.Width = _outline.Height = 0; }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
    internal static Int32Rect PixelSelection(Rect rect, Size viewport, int width, int height)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return Int32Rect.Empty;
        rect.Intersect(new Rect(new Point(), viewport));
        if (rect.IsEmpty) return Int32Rect.Empty;
        int left = Math.Clamp((int)Math.Floor(rect.Left / viewport.Width * width), 0, width);
        int top = Math.Clamp((int)Math.Floor(rect.Top / viewport.Height * height), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(rect.Right / viewport.Width * width), left, width);
        int bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom / viewport.Height * height), top, height);
        return right - left >= 4 && bottom - top >= 4 ? new Int32Rect(left, top, right - left, bottom - top) : Int32Rect.Empty;
    }
}
