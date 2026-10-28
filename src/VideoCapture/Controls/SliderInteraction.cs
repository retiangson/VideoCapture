using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VideoCapture.Controls;

internal static class SliderInteraction
{
    public static readonly DependencyProperty DirectManipulationProperty = DependencyProperty.RegisterAttached(
        "DirectManipulation", typeof(bool), typeof(SliderInteraction), new PropertyMetadata(false, Changed));
    public static bool GetDirectManipulation(DependencyObject obj) => (bool)obj.GetValue(DirectManipulationProperty);
    public static void SetDirectManipulation(DependencyObject obj, bool value) => obj.SetValue(DirectManipulationProperty, value);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Slider slider) return;
        if ((bool)e.NewValue) { slider.PreviewMouseLeftButtonDown += Down; slider.PreviewMouseMove += Move; slider.PreviewMouseLeftButtonUp += Up; }
        else { slider.PreviewMouseLeftButtonDown -= Down; slider.PreviewMouseMove -= Move; slider.PreviewMouseLeftButtonUp -= Up; }
    }
    private static void Down(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        for (DependencyObject? child = e.OriginalSource as DependencyObject; child is not null && child != slider; child = VisualTreeHelper.GetParent(child))
            if (child is Thumb) return;
        if (slider.Template.FindName("PART_Track", slider) is not Track track) return;
        SetFromPoint(slider, track, e.GetPosition(track));
        slider.Focus(); slider.CaptureMouse(); e.Handled = true;
    }
    private static void Move(object sender, MouseEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed && slider.Template.FindName("PART_Track", slider) is Track track)
            SetFromPoint(slider, track, e.GetPosition(track));
    }
    private static void Up(object sender, MouseButtonEventArgs e)
    { var slider = (Slider)sender; if (slider.IsMouseCaptured) { slider.ReleaseMouseCapture(); e.Handled = true; } }
    internal static void SetFromPoint(Slider slider, Track track, Point point)
    {
        double value = track.ValueFromPoint(point);
        if (double.IsFinite(value)) slider.SetCurrentValue(Slider.ValueProperty, Math.Clamp(value, slider.Minimum, slider.Maximum));
    }
}
