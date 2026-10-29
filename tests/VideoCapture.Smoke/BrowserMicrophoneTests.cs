using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using VideoCapture;
using VideoCapture.Services;

internal static class BrowserMicrophoneTests
{
    internal static async Task RunAsync(string output)
    {
        var mic = CoreWebView2PermissionKind.Microphone;
        if (!BrowserPermissions.AllowsMicrophone(mic, "https://chatgpt.com/mic", "https://chatgpt.com/chat") ||
            !BrowserPermissions.AllowsMicrophone(mic, "https://chat.openai.com/", "https://chat.openai.com/"))
            throw new Exception("Direct secure ChatGPT microphone access must be allowed.");
        foreach (string address in new[] { "http://chatgpt.com/", "https://chatgpt.com:8443/", "https://chatgpt.com.evil.test/", "https://evil.test/", "https://user@chatgpt.com/", "data:text/html,test" })
            if (BrowserPermissions.AllowsMicrophone(mic, address, address)) throw new Exception("Unexpected microphone permission for " + address);
        if (BrowserPermissions.AllowsMicrophone(mic, "https://chatgpt.com/", "https://evil.test/") ||
            BrowserPermissions.AllowsMicrophone(CoreWebView2PermissionKind.Camera, "https://chatgpt.com/", "https://chatgpt.com/") ||
            BrowserPermissions.AllowsMicrophone(CoreWebView2PermissionKind.Geolocation, "https://chatgpt.com/", "https://chatgpt.com/"))
            throw new Exception("Embedded requests and unrelated permissions must stay denied.");

        Directory.CreateDirectory(output);
        var streams = new List<MemoryStream>();
        var browser = new FloatingBrowser(1, "about:blank", Path.Combine(output, "BrowserProfile"));
        try
        {
            browser.Show();
            await browser.Initialization.WaitAsync(TimeSpan.FromSeconds(25));
            var initial = browser.ActiveBrowser?.CoreWebView2 ?? throw new Exception("Browser initialization failed.");
            // Reproduce a profile denial written by the previous app version.
            await initial.Profile.SetPermissionStateAsync(mic, "https://chatgpt.com", CoreWebView2PermissionState.Deny);
            var view = await browser.AddTabAsync("about:blank") ?? throw new Exception("Second tab failed.");
            var web = view.CoreWebView2;
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, e) =>
            {
                // Both origins are served entirely in memory. No ChatGPT page or
                // network connection receives this test microphone stream.
                var content = new MemoryStream(Encoding.UTF8.GetBytes("<html><body>VideoCapture isolated microphone permission test</body></html>"));
                streams.Add(content);
                e.Response = web.Environment.CreateWebResourceResponse(content, 200, "OK",
                    "Content-Type: text/html; charset=utf-8\r\nContent-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; connect-src 'none'");
            };
            async Task NavigateAsync(string uri)
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ulong? navigationId = null;
                void Started(object? sender, CoreWebView2NavigationStartingEventArgs e) { if (e.Uri == uri) navigationId = e.NavigationId; }
                void Done(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                { if (e.NavigationId != navigationId) return; if (e.IsSuccess) completed.TrySetResult(); else completed.TrySetException(new Exception("Fixture navigation failed: " + e.WebErrorStatus)); }
                web.NavigationStarting += Started;
                web.NavigationCompleted += Done;
                try { web.Navigate(uri); await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { web.NavigationStarting -= Started; web.NavigationCompleted -= Done; }
            }
            async Task<JsonDocument> RequestAsync(bool video = false)
            {
                await web.ExecuteScriptAsync("window.__vcMicResult=null;navigator.mediaDevices.getUserMedia(" + (video ? "{video:true}" : "{audio:true}") + ").then(s=>{window.__vcMicResult={allowed:true,live:s.getTracks().some(t=>t.readyState==='live')};s.getTracks().forEach(t=>t.stop());}).catch(e=>{window.__vcMicResult={allowed:false,error:e.name};});");
                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(10))
                {
                    string json = await web.ExecuteScriptAsync("window.__vcMicResult");
                    if (json != "null") return JsonDocument.Parse(json);
                    await Task.Delay(25);
                }
                throw new TimeoutException("Browser microphone request did not complete.");
            }
            await NavigateAsync("https://chatgpt.com/videocapture-permission-test");
            using (var allowed = await RequestAsync())
                if (!allowed.RootElement.GetProperty("allowed").GetBoolean() || !allowed.RootElement.GetProperty("live").GetBoolean())
                    throw new Exception("ChatGPT-origin microphone request failed: " + allowed.RootElement.GetRawText());
            using (var camera = await RequestAsync(true))
                if (camera.RootElement.GetProperty("allowed").GetBoolean()) throw new Exception("Camera access must remain denied.");
            await NavigateAsync("https://untrusted.example/videocapture-permission-test");
            using (var denied = await RequestAsync())
                if (denied.RootElement.GetProperty("allowed").GetBoolean() || denied.RootElement.GetProperty("error").GetString() != "NotAllowedError")
                    throw new Exception("Untrusted-origin microphone request was not denied.");
            Console.WriteLine("Browser microphone checks passed: exact HTTPS origins, top-level restriction, prior profile denial reset, actual live audio track, stopped test tracks, blocked camera and untrusted origin. Test pages were served in memory; no microphone audio was sent or saved.");
        }
        finally { browser.Shutdown(); foreach (var stream in streams) stream.Dispose(); }
    }
}
