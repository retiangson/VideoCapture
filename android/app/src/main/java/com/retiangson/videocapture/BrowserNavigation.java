package com.retiangson.videocapture;

import android.app.Activity;
import android.content.*;
import android.net.Uri;
import java.util.function.Consumer;

final class BrowserNavigation {
  static boolean externalSignIn(String url) {
    if (url == null || url.length() > 8192) return false;
    Uri uri = Uri.parse(url);
    return "https".equals(uri.getScheme())
        && uri.getUserInfo() == null
        && (uri.getPort() == -1 || uri.getPort() == 443)
        && "accounts.google.com".equalsIgnoreCase(uri.getHost());
  }

  static void openSignIn(Activity activity, String url, Consumer<String> feedback) {
    if (!externalSignIn(url)) return;
    try {
      activity.startActivity(
          new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addCategory(Intent.CATEGORY_BROWSABLE));
      feedback.accept(
          "Google sign-in opened in your external browser. Its sign-in session is separate from"
              + " this in-app browser.");
    } catch (ActivityNotFoundException error) {
      feedback.accept("Install an external browser to use Google sign-in.");
    }
  }
}
