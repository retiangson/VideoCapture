using Microsoft.Web.WebView2.Core;

namespace VideoCapture.Services;

internal static class BrowserPermissions
{
    private static readonly string[] ChatGptOrigins = ["https://chatgpt.com", "https://chat.openai.com"];

    internal static bool AllowsMicrophone(CoreWebView2PermissionKind kind, string request, string topLevel)
    {
        if (kind != CoreWebView2PermissionKind.Microphone || !TrustedOrigin(request, out var origin) || !TrustedOrigin(topLevel, out var parent)) return false;
        return string.Equals(origin, parent, StringComparison.OrdinalIgnoreCase);
    }
    private static bool TrustedOrigin(string address, out string? origin)
    {
        origin = null;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        string authority = uri.GetLeftPart(UriPartial.Authority);
        if (!ChatGptOrigins.Contains(authority, StringComparer.OrdinalIgnoreCase)) return false;
        origin = authority;
        return true;
    }
    internal static async Task ResetOldMicrophoneDecisionsAsync(CoreWebView2Profile profile)
    {
        // Older app versions persisted a blanket denial. Reset only these microphone
        // decisions; the request handler checks the current top-level origin each time.
        foreach (string origin in ChatGptOrigins)
            await profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Microphone, origin, CoreWebView2PermissionState.Default);
    }
}
