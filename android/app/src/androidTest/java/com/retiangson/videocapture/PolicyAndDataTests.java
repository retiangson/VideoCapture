package com.retiangson.videocapture;

import static org.junit.Assert.*;

import android.app.AlertDialog;
import android.content.*;
import android.content.pm.*;
import android.os.SystemClock;
import android.webkit.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.rule.GrantPermissionRule;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import org.junit.*;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class PolicyAndDataTests {
  @Rule
  public GrantPermissionRule permissions =
      GrantPermissionRule.grant(
          android.Manifest.permission.CAMERA, android.Manifest.permission.RECORD_AUDIO);

  private Context context() {
    return InstrumentationRegistry.getInstrumentation().getTargetContext();
  }

  @Before
  public void reset() {
    context().getSharedPreferences("player", 0).edit().clear().commit();
  }

  private static Object field(Object object, String name) {
    try {
      Field f = object.getClass().getDeclaredField(name);
      f.setAccessible(true);
      return f.get(object);
    } catch (Exception error) {
      throw new AssertionError(error);
    }
  }

  private static void call(Object object, String name) {
    try {
      Method m = object.getClass().getDeclaredMethod(name);
      m.setAccessible(true);
      m.invoke(object);
    } catch (Exception error) {
      throw new AssertionError(error);
    }
  }

  @Test
  public void permissionManifestAndGoogleSignInStayConstrained() throws Exception {
    PackageInfo info =
        context()
            .getPackageManager()
            .getPackageInfo(
                context().getPackageName(),
                PackageManager.GET_PERMISSIONS | PackageManager.GET_PROVIDERS);
    Set<String> requested = new HashSet<>(Arrays.asList(info.requestedPermissions));
    for (String prohibited :
        List.of(
            "android.permission.MANAGE_EXTERNAL_STORAGE",
            "android.permission.QUERY_ALL_PACKAGES",
            "android.permission.READ_MEDIA_IMAGES",
            "android.permission.READ_MEDIA_VIDEO",
            "android.permission.WRITE_EXTERNAL_STORAGE",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.SYSTEM_ALERT_WINDOW"))
      assertFalse(prohibited, requested.contains(prohibited));
    assertEquals(36, info.applicationInfo.targetSdkVersion);
    assertEquals(0, info.applicationInfo.flags & ApplicationInfo.FLAG_ALLOW_BACKUP);
    assertEquals(0, info.applicationInfo.flags & ApplicationInfo.FLAG_USES_CLEARTEXT_TRAFFIC);
    for (ProviderInfo provider : info.providers) {
      assertFalse(
          "SDK must not initialize before OCR consent",
          provider.name.contains("MlKitInitProvider"));
      if (provider.name.contains("FileProvider")) assertFalse(provider.exported);
    }
    assertTrue(
        BrowserNavigation.externalSignIn(
            "https://accounts.google.com/o/oauth2/v2/auth?client_id=fixture"));
    assertFalse(BrowserNavigation.externalSignIn("https://accounts.google.com.evil.test/auth"));
    assertFalse(BrowserNavigation.externalSignIn("https://user@accounts.google.com/auth"));
    assertFalse(BrowserNavigation.externalSignIn("http://accounts.google.com/auth"));
    assertFalse(BrowserNavigation.externalSignIn("https://accounts.google.com:8443/auth"));
  }

  @Test
  public void ocrDisclosureRequiresAffirmativeConsent() {
    AtomicBoolean accepted = new AtomicBoolean();
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            OcrConsent consent = (OcrConsent) field(a, "ocrConsent");
            AtomicBoolean allowed = new AtomicBoolean();
            assertFalse(consent.ready(true, () -> allowed.set(true)));
            AlertDialog dialog = (AlertDialog) field(consent, "dialog");
            assertTrue(dialog.isShowing());
            dialog.getButton(AlertDialog.BUTTON_NEGATIVE).performClick();
            assertFalse(allowed.get());
            assertEquals(
                "false", ((PlayerStore) field(a, "store")).text("ocrDisclosureAccepted", "false"));
            assertNull(field(a, "ocr"));
          });
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      scenario.onActivity(
          a -> {
            OcrConsent consent = (OcrConsent) field(a, "ocrConsent");
            assertFalse(consent.ready(true, () -> accepted.set(true)));
            ((AlertDialog) field(consent, "dialog"))
                .getButton(AlertDialog.BUTTON_POSITIVE)
                .performClick();
          });
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      scenario.onActivity(
          a -> {
            assertTrue(accepted.get());
            assertEquals(
                "true", ((PlayerStore) field(a, "store")).text("ocrDisclosureAccepted", "false"));
          });
    }
  }

  @Test
  public void clearingBrowserDataSignsOutWithoutRemovingPlaylist() throws Exception {

    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      AtomicReference<WebView> view = new AtomicReference<>();
      scenario.onActivity(
          a -> {
            call(a, "toggleBrowserPanel");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            @SuppressWarnings("unchecked")
            List<WebView> views = (List<WebView>) field(browser, "views");
            view.set(views.get(0));
            view.get().stopLoading();
            view.get()
                .loadDataWithBaseURL(
                    "https://privacy.test/",
                    "<script>localStorage.setItem('fixture','retained');window.ready=true</script>",
                    "text/html",
                    "UTF-8",
                    null);
            CookieManager.getInstance()
                .setCookie("https://privacy.test/", "session=fixture; Secure");
            CookieManager.getInstance().flush();
            @SuppressWarnings("unchecked")
            List<String> queue = (List<String>) field(a, "queue");
            queue.add("content://fixture/playlist.mp4");
            ((PlayerStore) field(a, "store")).list("playlist", queue);
          });
      long deadline = SystemClock.uptimeMillis() + 10000;
      while (!"true".equals(js(view.get(), "window.ready===true"))
          && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(50);
      assertEquals("\"retained\"", js(view.get(), "localStorage.getItem('fixture')"));
      assertNotNull(CookieManager.getInstance().getCookie("https://privacy.test/"));
      scenario.onActivity(a -> call(a, "clearBrowserData"));
      AtomicBoolean done = new AtomicBoolean();
      deadline = SystemClock.uptimeMillis() + 10000;
      while (!done.get() && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(a -> done.set(!(Boolean) field(a, "clearingBrowserData")));
        if (!done.get()) SystemClock.sleep(50);
      }
      assertTrue(done.get());
      assertNull(CookieManager.getInstance().getCookie("https://privacy.test/"));
      scenario.onActivity(
          a -> {
            assertNull(field(a, "browser"));
            assertEquals(
                List.of("content://fixture/playlist.mp4"),
                ((PlayerStore) field(a, "store")).list("playlist", 200));
            assertTrue(((PlayerStore) field(a, "store")).list("browserTabs", 32).isEmpty());
            call(a, "toggleBrowserPanel");
            @SuppressWarnings("unchecked")
            List<WebView> reopened = (List<WebView>) field(field(a, "browser"), "views");
            view.set(reopened.get(0));
            view.get().stopLoading();
            view.get()
                .loadDataWithBaseURL(
                    "https://privacy.test/",
                    "<script>window.ready=true</script>",
                    "text/html",
                    "UTF-8",
                    null);
          });
      deadline = SystemClock.uptimeMillis() + 10000;
      while (!"true".equals(js(view.get(), "window.ready===true"))
          && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(50);
      assertEquals("null", js(view.get(), "localStorage.getItem('fixture')"));
    }
  }

  private static String js(WebView view, String script) throws Exception {
    AtomicReference<String> result = new AtomicReference<>();
    CountDownLatch done = new CountDownLatch(1);
    InstrumentationRegistry.getInstrumentation()
        .runOnMainSync(
            () ->
                view.evaluateJavascript(
                    script,
                    value -> {
                      result.set(value);
                      done.countDown();
                    }));
    assertTrue(done.await(5, TimeUnit.SECONDS));
    return result.get();
  }
}
