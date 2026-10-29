using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoCapture;
using VideoCapture.Controls;

internal static class ButtonStateTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var toolbar = new FullscreenToolbar();
        try
        {
            toolbar.SetSpeechEnabled(true); toolbar.SetFillMode(true); toolbar.SetProgressVisible(true);
            toolbar.SetBrowserVisible(true); toolbar.SetTransport(true, true, true, true, true);
            toolbar.Update(true, true, "00:00:10", true, true, true);
            toolbar.Show();
            toolbar.Measure(new Size(1100, 700)); toolbar.Arrange(new Rect(toolbar.DesiredSize)); toolbar.UpdateLayout();
            foreach (string name in new[]{"SpeechButton", "FitButton", "ProgressButton", "BrowserButton", "PlayButton", "RecordButton"}) Check(toolbar, name, true);
            var image = new RenderTargetBitmap((int)Math.Ceiling(toolbar.ActualWidth), (int)Math.Ceiling(toolbar.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(toolbar);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output,"active-options-windows.png"))) encoder.Save(file);
            toolbar.SetSpeechEnabled(false); toolbar.SetFillMode(false); toolbar.SetProgressVisible(false);
            toolbar.SetBrowserVisible(false); toolbar.SetTransport(true, false, true, true, true);
            toolbar.Update(true, false, "", true, true, true);
            foreach (string name in new[]{"SpeechButton", "FitButton", "ProgressButton", "BrowserButton", "PlayButton", "RecordButton"}) Check(toolbar, name, false);
            Console.WriteLine("Active/off button surfaces, markers and accessibility states passed.");
        }
        finally { toolbar.Close(); }
    }
    private static void Check(FrameworkElement owner, string name, bool on)
    {
        var button=(Button)owner.FindName(name); button.ApplyTemplate(); button.UpdateLayout();
        var mark=(FrameworkElement)button.Template.FindName("ActiveMark",button);
        var surface=(Border)button.Template.FindName("Surface",button);
        if(ButtonState.GetIsActive(button)!=on || mark.Visibility!=(on?Visibility.Visible:Visibility.Collapsed) ||
           System.Windows.Automation.AutomationProperties.GetItemStatus(button)!=(name=="FitButton" ? on?"Fill":"Regular" : on?"On":"Off")) throw new Exception("Stale button state: "+name);
        if(on && ((SolidColorBrush)surface.Background).Color!=Color.FromRgb(0x24,0x55,0x48)) throw new Exception("Active button fill not applied: "+name);
    }
}
