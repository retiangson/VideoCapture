package com.retiangson.videocapture;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.*;
import android.net.Uri;
import android.webkit.*;
import java.util.*;
import java.util.function.Consumer;

/** Owns one user-confirmed upload request. Navigation and tab closure invalidate its callback. */
final class BrowserAttachments {
  interface Picker {
    boolean launch(Intent intent, Consumer<Uri[]> result);
  }

  private final Activity activity;
  private final Picker picker;
  private final Consumer<String> feedback;
  private ValueCallback<Uri[]> callback;
  private WebView owner;
  private AlertDialog dialog;

  BrowserAttachments(Activity activity, Picker picker, Consumer<String> feedback) {
    this.activity = activity;
    this.picker = picker;
    this.feedback = feedback;
  }

  boolean open(
      WebView view, ValueCallback<Uri[]> result, WebChromeClient.FileChooserParams params) {
    cancel();
    if (params.getMode() != WebChromeClient.FileChooserParams.MODE_OPEN
        && params.getMode() != WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE) {
      result.onReceiveValue(null);
      feedback.accept("Saving files from the browser is not supported.");
      return true;
    }
    owner = view;
    callback = result;
    Uri copied = copiedFrame(params.getAcceptTypes());
    if (copied == null) {
      chooseFiles(result, params);
    } else {
      dialog =
          new AlertDialog.Builder(activity)
              .setTitle("Attach image")
              .setItems(
                  new String[] {"Copied frame", "Choose files"},
                  (d, which) -> {
                    if (which == 0) finish(result, new Uri[] {copied});
                    else chooseFiles(result, params);
                  })
              .setOnCancelListener(d -> finish(result, null))
              .show();
    }
    return true;
  }

  private void chooseFiles(ValueCallback<Uri[]> request, WebChromeClient.FileChooserParams params) {
    if (callback != request) return;
    Intent intent = params.createIntent();
    intent.addCategory(Intent.CATEGORY_OPENABLE);
    intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
    if (!picker.launch(
        intent,
        uris -> {
          if (callback != request) return;
          if (uris != null) {
            // File providers are untrusted: never hand a web page file:// or arbitrary intent URIs.
            uris =
                Arrays.stream(uris)
                    .filter(
                        u ->
                            u != null
                                && "content".equals(u.getScheme())
                                && !(activity.getPackageName() + ".files").equals(u.getAuthority()))
                    .limit(
                        params.getMode() == WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE
                            ? 32
                            : 1)
                    .toArray(Uri[]::new);
            if (uris.length == 0) uris = null;
          }
          finish(request, uris);
        })) {
      finish(request, null);
      feedback.accept("Could not open the file picker. Close any existing picker and try again.");
    }
  }

  private Uri copiedFrame(String[] acceptTypes) {
    if (!acceptsPng(acceptTypes)) return null;
    ClipboardManager clipboard = activity.getSystemService(ClipboardManager.class);
    ClipData clip = clipboard.getPrimaryClip();
    if (clip == null || clip.getItemCount() == 0) return null;
    Uri uri = clip.getItemAt(0).getUri();
    if (uri == null
        || !"content".equals(uri.getScheme())
        || !(activity.getPackageName() + ".files").equals(uri.getAuthority())
        || uri.getPath() == null
        || !uri.getPath().startsWith("/frames/frame-")
        || !uri.getPath().endsWith(".png")) return null;
    try (var file = activity.getContentResolver().openAssetFileDescriptor(uri, "r")) {
      return file != null ? uri : null;
    } catch (java.io.IOException | SecurityException e) {
      return null;
    }
  }

  static boolean acceptsPng(String[] types) {
    if (types == null || types.length == 0) return true;
    boolean specified = false;
    for (String group : types) {
      if (group == null) continue;
      for (String raw : group.split(",")) {
        String type = raw.trim().toLowerCase(Locale.ROOT);
        if (type.isEmpty()) continue;
        specified = true;
        if (type.equals("*/*")
            || type.equals("image/*")
            || type.equals("image/png")
            || type.equals(".png")) return true;
      }
    }
    return !specified;
  }

  private void finish(ValueCallback<Uri[]> request, Uri[] uris) {
    if (callback != request) return;
    callback = null;
    owner = null;
    if (dialog != null) {
      dialog.dismiss();
      dialog = null;
    }
    request.onReceiveValue(uris);
  }

  void cancelFor(WebView view) {
    if (owner == view) cancel();
  }

  void cancel() {
    if (callback != null) finish(callback, null);
  }
}
