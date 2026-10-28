using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using VideoCapture;
using VideoCapture.Services;

internal static class OverlayTests
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        if (ScreenTextSelection.PixelSelection(new Rect(-50, 10, 100, 40), new Size(100, 100), 200, 200) != new Int32Rect(0, 20, 100, 80) ||
            !ScreenTextSelection.PixelSelection(new Rect(200, 200, 5, 5), new Size(100, 100), 200, 200).IsEmpty)
            throw new Exception("Screen selection bounds/DPI scaling checks failed.");
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 220));
            drawing.DrawText(new FormattedText("VideoCapture 12345\nCopy this screen text", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Arial"), 46, Brushes.Black, 1), new Point(30, 35));
        }
        var image = new RenderTargetBitmap(900, 220, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        string text = await TextRecognition.ReadAsync(image);
        if (!text.Contains("VideoCapture", StringComparison.OrdinalIgnoreCase) || !text.Contains("12345") || !text.Contains("screen text"))
            throw new Exception("OCR did not recognize the known text fixture: " + text);
        var blank = BitmapSource.Create(400, 100, 96, 96, PixelFormats.Gray8, null, Enumerable.Repeat((byte)255, 40_000).ToArray(), 400);
        if (!string.IsNullOrWhiteSpace(await TextRecognition.ReadAsync(blank))) throw new Exception("OCR found text in a blank image.");
        Console.WriteLine("Offline English OCR and empty-image checks passed: " + text.Replace('\n', ' '));
        if (Environment.GetEnvironmentVariable("VIDEOCAPTURE_TEST_CLIPBOARD") == "1")
        {
            var previousClipboard = Clipboard.GetDataObject();
            try
            {
                await SnapshotService.CopyTextAsync(text);
                if (Clipboard.GetText(TextDataFormat.UnicodeText) != text) throw new Exception("OCR Unicode text clipboard round-trip failed.");
            }
            finally
            {
                if (previousClipboard is not null) Clipboard.SetDataObject(previousClipboard, true);
                else Clipboard.Clear();
            }
            var desktop = DesktopSnapshot.Capture();
            if (desktop.Image.PixelWidth != desktop.Bounds.Width || desktop.Image.PixelHeight != desktop.Bounds.Height)
                throw new Exception("Physical desktop capture dimensions are inconsistent.");
            Console.WriteLine("OCR clipboard copy and desktop bitmap capture passed; original clipboard restored. Desktop images were not saved.");
        }
        if (FloatingBrowser.TryAddress("javascript:alert(1)", out _) || FloatingBrowser.TryAddress("file:///C:/test", out _) ||
            !FloatingBrowser.TryAddress("example.com", out var valid) || valid!.Scheme != "https")
            throw new Exception("Browser URI restrictions failed.");
        if (FloatingBrowser.HomeAddress != "https://chatgpt.com/") throw new Exception("New browser windows/tabs must default to ChatGPT.");
        var browser = new FloatingBrowser(1, "about:blank", Path.Combine(output, "BrowserTestProfile"));
        try
        {
            browser.Show();
            await browser.Initialization.WaitAsync(TimeSpan.FromSeconds(25));
            var web = browser.ActiveBrowser;
            if (web?.CoreWebView2 is null) throw new Exception("WebView2 runtime failed: " + ((System.Windows.Controls.TextBlock)browser.FindName("Feedback")).Text);
            var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            web.CoreWebView2.NavigationCompleted += (_, args) => { if (args.IsSuccess) navigated.TrySetResult(); };
            browser.ShowLocalPage("<html><body style='font:24px Arial;background:white'><h1>Browser input test</h1><input id='text' aria-label='Test input'><button id='button' onclick=\"document.title='clicked'\">Click me</button></body></html>");
            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            browser.Opacity = 0.55;
            await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('text').value='keyboard test'; document.getElementById('button').click();");
            string actualTitle = await web.CoreWebView2.ExecuteScriptAsync("document.title");
            string actualInput = await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('text')?.value");
            if (actualTitle != "\"clicked\"" || actualInput != "\"keyboard test\"")
                throw new Exception($"Browser interaction failed: title={actualTitle}, input={actualInput}, body={await web.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML")}");
            browser.Hide();
            browser.Show();
            if (await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('text').value") != "\"keyboard test\"")
                throw new Exception("Browser state was lost on hide/show.");
            if (Environment.GetEnvironmentVariable("VIDEOCAPTURE_TEST_INPUT") == "1") await BrowserInputTest.RunAsync(browser, web);
            var second = await browser.AddTabAsync("about:blank") ?? throw new Exception("Second tab failed to initialize.");
            if (browser.TabCount != 2 || browser.ActiveBrowser != second || web.Visibility != Visibility.Collapsed)
                throw new Exception("Adding a tab must select it and hide the previous view.");
            var secondNavigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second.CoreWebView2.NavigationCompleted += (_, args) => { if (args.IsSuccess) secondNavigation.TrySetResult(); };
            browser.ShowLocalPage("<html><head><title>Second tab</title></head><body>Independent page</body></html>");
            await secondNavigation.Task.WaitAsync(TimeSpan.FromSeconds(10));
            browser.SelectTab(web);
            if (await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('text').value") != "\"keyboard test\"" || second.Visibility != Visibility.Collapsed)
                throw new Exception("Switching tabs must preserve independent page state.");
            string cookieName = "VideoCaptureTabTest" + Guid.NewGuid().ToString("N");
            var cookie = web.CoreWebView2.CookieManager.CreateCookie(cookieName, "shared", ".tabs.example", "/");
            web.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
            var sharedCookies = await second.CoreWebView2.CookieManager.GetCookiesAsync("https://tabs.example/");
            if (!sharedCookies.Any(item => item.Name == cookieName && item.Value == "shared")) throw new Exception("Tabs must share cookies/profile.");
            web.CoreWebView2.CookieManager.DeleteCookie(cookie);
            await browser.CloseTabAsync(second);
            if (browser.TabCount != 1 || browser.ActiveBrowser != web) throw new Exception("Closing a tab must preserve/select the remaining tab.");
            var third = await browser.AddTabAsync("about:blank") ?? throw new Exception("Third tab failed.");
            browser.Hide(); browser.Show();
            if (browser.TabCount != 2 || browser.ActiveBrowser != third) throw new Exception("Hiding browser must preserve tabs and selection.");
            await browser.CloseTabAsync(third);
            await web.CoreWebView2.ExecuteScriptAsync("window.open('about:blank', '_blank');");
            await WaitForAsync(() => browser.TabCount == 2 && browser.ActiveBrowser?.CoreWebView2 is not null, "popup tab initialization");
            var popupView = browser.ActiveBrowser!;
            if (await popupView.CoreWebView2.ExecuteScriptAsync("window.opener !== null") != "true")
                throw new Exception("Popup tabs must retain their opener for sign-in flows.");
            await popupView.CoreWebView2.ExecuteScriptAsync("window.close();");
            await WaitForAsync(() => browser.TabCount == 1 && browser.ActiveBrowser == web, "popup tab close");
            browser.Opacity = 1;
            await Task.Delay(500);
            var screenshot = new RenderTargetBitmap((int)browser.ActualWidth, (int)browser.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            screenshot.Render(browser);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using var file = File.Create(Path.Combine(output, "browser.png"));
            encoder.Save(file);
            Console.WriteLine("Browser initialization, ChatGPT default, tab creation/switch/close, independent page state, shared cookies, opacity, and hide/show passed. Native mouse/keyboard hit testing still needs interactive validation.");
        }
        finally { browser.Shutdown(); }
    }
    private static async Task WaitForAsync(Func<bool> condition, string action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (timeout.IsCancellationRequested) throw new TimeoutException(action);
            await Task.Delay(25);
        }
    }
}
