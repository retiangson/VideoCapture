package com.retiangson.videocapture;

import static org.junit.Assert.*;

import android.Manifest;
import android.app.AlertDialog;
import android.content.*;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.SystemClock;
import android.util.Base64;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.webkit.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.rule.GrantPermissionRule;
import java.io.*;
import java.lang.reflect.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import java.util.function.Consumer;
import org.junit.*;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class BrowserAttachmentTests {
  @Rule
  public GrantPermissionRule permissions =
      GrantPermissionRule.grant(Manifest.permission.CAMERA, Manifest.permission.RECORD_AUDIO);

  private static Object field(Object target, String name) {
    try {
      Field f = target.getClass().getDeclaredField(name);
      f.setAccessible(true);
      return f.get(target);
    } catch (ReflectiveOperationException e) {
      throw new AssertionError(e);
    }
  }

  private static WebChromeClient.FileChooserParams params(String accept, int mode) {
    return new WebChromeClient.FileChooserParams() {
      public int getMode() {
        return mode;
      }

      public String[] getAcceptTypes() {
        return new String[] {accept};
      }

      public boolean isCaptureEnabled() {
        return false;
      }

      public CharSequence getTitle() {
        return "Attach";
      }

      public String getFilenameHint() {
        return null;
      }

      public Intent createIntent() {
        return new Intent(Intent.ACTION_GET_CONTENT)
            .setType(accept)
            .putExtra(Intent.EXTRA_ALLOW_MULTIPLE, mode == MODE_OPEN_MULTIPLE);
      }
    };
  }

  @Test
  public void copiedPngReachesActualWebFileInput() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      AtomicReference<WebView> page = new AtomicReference<>();
      AtomicReference<BrowserAttachments> attachments = new AtomicReference<>();
      AtomicReference<String> expected = new AtomicReference<>();
      AtomicReference<ClipData> previous = new AtomicReference<>();
      AtomicReference<Uri> saved = new AtomicReference<>();
      scenario.onActivity(
          a -> {
            try {
              ClipboardManager clipboard = a.getSystemService(ClipboardManager.class);
              previous.set(clipboard.getPrimaryClip());
              Bitmap bitmap = Bitmap.createBitmap(12, 8, Bitmap.Config.ARGB_8888);
              bitmap.eraseColor(0xff278ad5);
              try {
                ByteArrayOutputStream png = new ByteArrayOutputStream();
                assertTrue(bitmap.compress(Bitmap.CompressFormat.PNG, 100, png));
                expected.set(
                    "data:image/png;base64,"
                        + Base64.encodeToString(png.toByteArray(), Base64.NO_WRAP));
                Uri uri = GalleryFrames.save(a, bitmap);
                saved.set(uri);
                GalleryFrames.copy(a, uri);
              } finally {
                bitmap.recycle();
              }
              Method open = MainActivity.class.getDeclaredMethod("toggleBrowserPanel");
              open.setAccessible(true);
              open.invoke(a);
              BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
              attachments.set((BrowserAttachments) field(browser, "attachments"));
              Method current = BrowserOverlay.class.getDeclaredMethod("current");
              current.setAccessible(true);
              page.set((WebView) current.invoke(browser));
              page.get().stopLoading();
              page.get()
                  .loadDataWithBaseURL(
                      "https://upload.test/",
                      """
                      <meta name='viewport' content='width=device-width, initial-scale=1'>
                      <input id='upload' type='file' accept='image/png' style='height:64px;width:240px'>
                      <script>
                      window.ready=true; window.result='';
                      upload.onchange=()=>{const f=upload.files[0];const r=new FileReader();
                        r.onload=()=>{window.result=r.result;window.mime=f.type};r.readAsDataURL(f)};
                      </script>
                      """,
                      "text/html",
                      "UTF-8",
                      null);
            } catch (Exception e) {
              throw new AssertionError(e);
            }
          });
      try {
        awaitScript(page.get(), "window.ready===true", "true");
        org.json.JSONArray bounds =
            new org.json.JSONArray(
                script(
                    page.get(),
                    "(()=>{const r=upload.getBoundingClientRect();return"
                        + " [r.x+r.width/2,r.y+r.height/2,innerWidth]})()"));
        int[] location = new int[2];
        AtomicReference<Float> scale = new AtomicReference<>();
        scenario.onActivity(
            a -> {
              page.get().getLocationOnScreen(location);
              scale.set((float) page.get().getWidth() / (float) bounds.optDouble(2));
            });
        float x = location[0] + (float) bounds.getDouble(0) * scale.get();
        float y = location[1] + (float) bounds.getDouble(1) * scale.get();
        long down = SystemClock.uptimeMillis();
        MotionEvent press = MotionEvent.obtain(down, down, MotionEvent.ACTION_DOWN, x, y, 0);
        MotionEvent release = MotionEvent.obtain(down, down + 60, MotionEvent.ACTION_UP, x, y, 0);
        InstrumentationRegistry.getInstrumentation().sendPointerSync(press);
        InstrumentationRegistry.getInstrumentation().sendPointerSync(release);
        press.recycle();
        release.recycle();
        long deadline = SystemClock.uptimeMillis() + 10000;
        AtomicBoolean shown = new AtomicBoolean();
        while (!shown.get() && SystemClock.uptimeMillis() < deadline) {
          scenario.onActivity(
              a -> {
                AlertDialog dialog = (AlertDialog) field(attachments.get(), "dialog");
                if (dialog != null && dialog.isShowing()) {
                  assertEquals("Copied frame", dialog.getListView().getAdapter().getItem(0));
                  dialog.getListView().performItemClick(null, 0, 0);
                  shown.set(true);
                }
              });
          if (!shown.get()) SystemClock.sleep(80);
        }
        assertTrue("Upload must offer copied frame", shown.get());
        awaitScript(page.get(), "window.result", org.json.JSONObject.quote(expected.get()));
        assertEquals("\"image/png\"", script(page.get(), "window.mime"));
      } finally {
        scenario.onActivity(
            a -> {
              ClipboardManager clipboard = a.getSystemService(ClipboardManager.class);
              if (previous.get() == null) clipboard.clearPrimaryClip();
              else clipboard.setPrimaryClip(previous.get());
              if (saved.get() != null) a.getContentResolver().delete(saved.get(), null, null);
            });
      }
    }
  }

  @Test
  public void galleryFramePastesReadablePngIntoWebEditor() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      AtomicReference<WebView> page = new AtomicReference<>();
      AtomicReference<Uri> saved = new AtomicReference<>();
      AtomicReference<ClipData> previous = new AtomicReference<>();
      AtomicReference<String> expected = new AtomicReference<>();
      scenario.onActivity(
          a -> {
            try {
              ClipboardManager clipboard = a.getSystemService(ClipboardManager.class);
              previous.set(clipboard.getPrimaryClip());
              Bitmap image = Bitmap.createBitmap(24, 16, Bitmap.Config.ARGB_8888);
              image.eraseColor(0xff25e685);
              try {
                ByteArrayOutputStream png = new ByteArrayOutputStream();
                assertTrue(image.compress(Bitmap.CompressFormat.PNG, 100, png));
                expected.set(
                    "data:image/png;base64,"
                        + Base64.encodeToString(png.toByteArray(), Base64.NO_WRAP));
                saved.set(GalleryFrames.save(a, image));
              } finally {
                image.recycle();
              }
              assertTrue(GalleryFrames.isPublishedFrame(a, saved.get()));
              GalleryFrames.copy(a, saved.get());
              assertTrue(clipboard.getPrimaryClipDescription().hasMimeType("image/png"));
              Method open = MainActivity.class.getDeclaredMethod("toggleBrowserPanel");
              open.setAccessible(true);
              open.invoke(a);
              BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
              Method current = BrowserOverlay.class.getDeclaredMethod("current");
              current.setAccessible(true);
              page.set((WebView) current.invoke(browser));
              page.get().stopLoading();
              page.get()
                  .loadDataWithBaseURL(
                      "https://paste.test/",
                      """
                      <meta name='viewport' content='width=device-width,initial-scale=1'>
                      <div id='editor' contenteditable='true' style='height:100px'>Paste image here</div>
                      <script>
                      window.ready=true;window.result='';window.files=0;
                      document.addEventListener('paste',e=>{
                        window.files=e.clipboardData.files.length;
                        const f=e.clipboardData.files[0];if(!f)return;
                        const r=new FileReader();r.onload=()=>{window.result=r.result};r.readAsDataURL(f);
                        e.preventDefault();
                      });
                      </script>
                      """,
                      "text/html",
                      "UTF-8",
                      null);
            } catch (Exception error) {
              throw new AssertionError(error);
            }
          });
      try {
        awaitScript(page.get(), "window.ready===true", "true");
        scenario.onActivity(a -> page.get().requestFocus());
        script(page.get(), "editor.focus()");
        long now = SystemClock.uptimeMillis();
        int modifiers = KeyEvent.META_CTRL_ON | KeyEvent.META_CTRL_LEFT_ON;
        InstrumentationRegistry.getInstrumentation()
            .sendKeySync(
                new KeyEvent(now, now, KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_V, 0, modifiers));
        InstrumentationRegistry.getInstrumentation()
            .sendKeySync(
                new KeyEvent(now, now + 60, KeyEvent.ACTION_UP, KeyEvent.KEYCODE_V, 0, modifiers));
        awaitScript(page.get(), "window.result", org.json.JSONObject.quote(expected.get()));
        assertEquals("1", script(page.get(), "window.files"));
      } finally {
        scenario.onActivity(
            a -> {
              ClipboardManager clipboard = a.getSystemService(ClipboardManager.class);
              if (previous.get() == null) clipboard.clearPrimaryClip();
              else clipboard.setPrimaryClip(previous.get());
              if (saved.get() != null) a.getContentResolver().delete(saved.get(), null, null);
            });
      }
    }
  }

  @Test
  public void cancelledAndReplacedRequestsNeverDeliverLateFiles() {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            AtomicReference<Consumer<Uri[]>> picker = new AtomicReference<>();
            BrowserAttachments uploads =
                new BrowserAttachments(
                    a,
                    (intent, result) -> {
                      assertTrue(intent.hasCategory(Intent.CATEGORY_OPENABLE));
                      picker.set(result);
                      return true;
                    },
                    value -> {});
            WebView view = new WebView(a);
            try {
              AtomicInteger first = new AtomicInteger(), second = new AtomicInteger();
              AtomicReference<Uri[]> delivered = new AtomicReference<>();
              uploads.open(
                  view,
                  uris -> {
                    assertNull(uris);
                    first.incrementAndGet();
                  },
                  params("audio/*", 0));
              Consumer<Uri[]> stale = picker.get();
              uploads.open(
                  view,
                  uris -> {
                    delivered.set(uris);
                    second.incrementAndGet();
                  },
                  params("audio/*", 0));
              assertEquals(1, first.get());
              stale.accept(new Uri[] {Uri.parse("content://test/old")});
              assertEquals(0, second.get());
              picker
                  .get()
                  .accept(
                      new Uri[] {
                        Uri.parse("file:///private/secret"), Uri.parse("content://test/new")
                      });
              assertArrayEquals(new Uri[] {Uri.parse("content://test/new")}, delivered.get());
              assertEquals(1, second.get());
              AtomicInteger cancelled = new AtomicInteger();
              uploads.open(
                  view,
                  uris -> {
                    assertNull(uris);
                    cancelled.incrementAndGet();
                  },
                  params("audio/*", 0));
              uploads.cancelFor(view);
              picker.get().accept(new Uri[] {Uri.parse("content://test/late")});
              assertEquals(1, cancelled.get());
            } finally {
              uploads.cancel();
              view.destroy();
            }
          });
    }
  }

  @Test
  public void mimeFiltersPickerFailureAndMultipleSelection() {
    assertTrue(BrowserAttachments.acceptsPng(new String[] {"", "image/jpeg,image/png"}));
    assertTrue(BrowserAttachments.acceptsPng(new String[] {".PNG"}));
    assertTrue(BrowserAttachments.acceptsPng(new String[] {"*/*"}));
    assertFalse(BrowserAttachments.acceptsPng(new String[] {"image/jpeg", "audio/*"}));
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            WebView view = new WebView(a);
            try {
              AtomicInteger calls = new AtomicInteger();
              BrowserAttachments failed =
                  new BrowserAttachments(a, (intent, result) -> false, value -> {});
              failed.open(
                  view,
                  uris -> {
                    assertNull(uris);
                    calls.incrementAndGet();
                  },
                  params("audio/*", 0));
              failed.cancel();
              assertEquals(1, calls.get());
              BrowserAttachments multiple =
                  new BrowserAttachments(
                      a,
                      (intent, result) -> {
                        assertTrue(intent.getBooleanExtra(Intent.EXTRA_ALLOW_MULTIPLE, false));
                        result.accept(
                            new Uri[] {
                              Uri.parse("content://test/a"), Uri.parse("content://test/b")
                            });
                        return true;
                      },
                      value -> {});
              multiple.open(view, uris -> assertEquals(2, uris.length), params("audio/*", 1));
            } finally {
              view.destroy();
            }
          });
    }
  }

  private static String script(WebView view, String js) throws Exception {
    CountDownLatch done = new CountDownLatch(1);
    AtomicReference<String> result = new AtomicReference<>();
    InstrumentationRegistry.getInstrumentation()
        .runOnMainSync(
            () ->
                view.evaluateJavascript(
                    js,
                    value -> {
                      result.set(value);
                      done.countDown();
                    }));
    assertTrue(done.await(10, TimeUnit.SECONDS));
    return result.get();
  }

  private static void awaitScript(WebView view, String js, String expected) throws Exception {
    long deadline = SystemClock.uptimeMillis() + 10000;
    String actual;
    do {
      actual = script(view, js);
      if (new org.json.JSONTokener(expected)
          .nextValue()
          .equals(new org.json.JSONTokener(actual).nextValue())) return;
      SystemClock.sleep(80);
    } while (SystemClock.uptimeMillis() < deadline);
    assertEquals(
        new org.json.JSONTokener(expected).nextValue(),
        new org.json.JSONTokener(actual).nextValue());
  }
}
