package com.retiangson.videocapture;

import static org.junit.Assert.*;

import android.Manifest;
import android.content.*;
import android.graphics.*;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.widget.*;
import androidx.core.content.FileProvider;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.rule.GrantPermissionRule;
import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;
import java.io.*;
import java.lang.reflect.*;
import java.nio.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import org.junit.*;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class AndroidParityTests {
  @Rule
  public GrantPermissionRule permissions =
      GrantPermissionRule.grant(
          firstBrowserPermission()
              ? new String[] {Manifest.permission.CAMERA}
              : new String[] {Manifest.permission.CAMERA, Manifest.permission.RECORD_AUDIO});

  private static boolean firstBrowserPermission() {
    return "true"
        .equals(InstrumentationRegistry.getArguments().getString("firstBrowserPermission"));
  }

  private Context target() {
    return InstrumentationRegistry.getInstrumentation().getTargetContext();
  }

  @Before
  public void reset() {
    if ("true".equals(InstrumentationRegistry.getArguments().getString("requireOffline")))
      assertNull(
          "Airplane-mode validation requires no active network",
          ((android.net.ConnectivityManager)
                  target().getSystemService(Context.CONNECTIVITY_SERVICE))
              .getActiveNetwork());
    target().getSharedPreferences("player", Context.MODE_PRIVATE).edit().clear().commit();
  }

  private static Object field(Object instance, String name) {
    try {
      Field field = instance.getClass().getDeclaredField(name);
      field.setAccessible(true);
      return field.get(instance);
    } catch (Exception e) {
      throw new AssertionError(e);
    }
  }

  private static void call(Object instance, String name) {
    try {
      Method method = instance.getClass().getDeclaredMethod(name);
      method.setAccessible(true);
      method.invoke(instance);
    } catch (Exception e) {
      throw new AssertionError(e);
    }
  }

  @Test
  public void gesturesAndBrowserTabs() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      AtomicReference<View> overlay = new AtomicReference<>();
      scenario.onActivity(
          a -> {
            overlay.set((View) field(a, "gesturesView"));
            assertEquals(View.VISIBLE, ((View) field(a, "menu")).getVisibility());
            ((Button) field(a, "fullscreenButton")).performClick();
            assertEquals(View.GONE, ((View) field(a, "menu")).getVisibility());
          });
      int[] at = new int[2];
      InstrumentationRegistry.getInstrumentation()
          .runOnMainSync(() -> overlay.get().getLocationOnScreen(at));
      float x = at[0] + 400, y = at[1] + 250;
      long time = SystemClock.uptimeMillis();
      pointer(time, time, MotionEvent.ACTION_DOWN, x, y);
      SystemClock.sleep(600);
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      scenario.onActivity(
          a -> {
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertNotNull(browser);
            assertEquals(View.VISIBLE, browser.getVisibility());
            assertEquals(View.GONE, ((View) field(a, "menu")).getVisibility());
            browser.toggle();
          });
      pointer(time, SystemClock.uptimeMillis(), MotionEvent.ACTION_UP, x, y);
      time = SystemClock.uptimeMillis();
      pointer(time, time, MotionEvent.ACTION_DOWN, x, y);
      pointer(time, time + 60, MotionEvent.ACTION_UP, x, y);
      scenario.onActivity(a -> assertEquals(View.GONE, ((View) field(a, "menu")).getVisibility()));
      long start = SystemClock.uptimeMillis();
      pointer(start, start, MotionEvent.ACTION_DOWN, x, y);
      multi(
          start,
          start + 40,
          MotionEvent.ACTION_POINTER_DOWN | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
          x,
          y);
      multi(
          start,
          start + 90,
          MotionEvent.ACTION_POINTER_UP | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
          x,
          y);
      pointer(start, start + 120, MotionEvent.ACTION_UP, x, y);
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      scenario.onActivity(
          a -> {
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertNotNull(browser);
            assertEquals(View.VISIBLE, browser.getVisibility());
            browser.add("about:blank");
            int count = ((java.util.List<?>) field(browser, "views")).size();
            browser.toggle();
            browser.toggle();
            assertEquals(count, ((java.util.List<?>) field(browser, "views")).size());
            assertTrue(BrowserOverlay.trusted("https://chatgpt.com/"));
            assertFalse(BrowserOverlay.trusted("https://chatgpt.com.evil.test/"));
          });
    }
  }

  @Test
  public void doubleTapShowsMenuAndFullscreenUsesHeaderIcon() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      AtomicReference<View> target = new AtomicReference<>();
      scenario.onActivity(a -> target.set((View) field(a, "gesturesView")));
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      int[] at = new int[2];
      InstrumentationRegistry.getInstrumentation()
          .runOnMainSync(() -> target.get().getLocationOnScreen(at));
      long down = SystemClock.uptimeMillis();
      pointer(down, down, MotionEvent.ACTION_DOWN, at[0] + 50, at[1] + 150);
      pointer(down, down + 30, MotionEvent.ACTION_UP, at[0] + 50, at[1] + 150);
      scenario.onActivity(a -> assertEquals(View.GONE, ((View) field(a, "menu")).getVisibility()));
      SystemClock.sleep(400);
      long gesture = SystemClock.uptimeMillis();
      for (int tap = 0; tap < 2; tap++) {
        down = gesture + tap * 80;
        pointer(down, down, MotionEvent.ACTION_DOWN, at[0] + 50, at[1] + 150);
        pointer(down, down + 30, MotionEvent.ACTION_UP, at[0] + 50, at[1] + 150);
        SystemClock.sleep(80);
      }
      scenario.onActivity(
          a -> {
            assertEquals(View.VISIBLE, ((View) field(a, "menu")).getVisibility());
            assertFalse((Boolean) field(a, "fullscreen"));
            ((TextView) field(a, "status")).setText("LIVE · test capture");
            Button full = (Button) field(a, "fullscreenButton");
            Button lock = (Button) field(a, "rotationLockButton");
            assertSame(full.getParent(), lock.getParent());
            assertEquals("", full.getText().toString());
            assertEquals("", lock.getText().toString());
            full.performClick();
            assertTrue((Boolean) field(a, "fullscreen"));
            assertEquals(View.GONE, ((View) field(a, "status")).getVisibility());
            full.performClick();
            assertFalse((Boolean) field(a, "fullscreen"));
          });
    }
  }

  private static void pointer(long down, long now, int action, float x, float y) {
    MotionEvent.PointerProperties property = new MotionEvent.PointerProperties();
    property.id = 0;
    property.toolType = MotionEvent.TOOL_TYPE_FINGER;
    MotionEvent.PointerCoords coordinates = new MotionEvent.PointerCoords();
    coordinates.x = x;
    coordinates.y = y;
    coordinates.pressure = 1;
    coordinates.size = 1;
    MotionEvent event =
        MotionEvent.obtain(
            down,
            now,
            action,
            1,
            new MotionEvent.PointerProperties[] {property},
            new MotionEvent.PointerCoords[] {coordinates},
            0,
            0,
            1,
            1,
            0,
            0,
            android.view.InputDevice.SOURCE_TOUCHSCREEN,
            0);
    InstrumentationRegistry.getInstrumentation().sendPointerSync(event);
    event.recycle();
  }

  private static void multi(long down, long now, int action, float x, float y) {
    MotionEvent.PointerProperties[] props = {
      new MotionEvent.PointerProperties(), new MotionEvent.PointerProperties()
    };
    MotionEvent.PointerCoords[] coords = {
      new MotionEvent.PointerCoords(), new MotionEvent.PointerCoords()
    };
    for (int i = 0; i < 2; i++) {
      props[i].id = i;
      props[i].toolType = MotionEvent.TOOL_TYPE_FINGER;
      coords[i].x = x + i * 70;
      coords[i].y = y;
      coords[i].pressure = 1;
      coords[i].size = 1;
    }
    MotionEvent e =
        MotionEvent.obtain(
            down,
            now,
            action,
            2,
            props,
            coords,
            0,
            0,
            1,
            1,
            0,
            0,
            android.view.InputDevice.SOURCE_TOUCHSCREEN,
            0);
    InstrumentationRegistry.getInstrumentation().sendPointerSync(e);
    e.recycle();
  }

  @Test
  public void decodedVideoFrameCopiesOpaquePictureAndRejectsEmptyCapture() throws Exception {
    AtomicReference<ClipData> previous = new AtomicReference<>();
    try (ActivityScenario<MainActivity> empty = ActivityScenario.launch(MainActivity.class)) {
      empty.onActivity(
          a -> {
            ClipboardManager clipboard = a.getSystemService(ClipboardManager.class);
            previous.set(clipboard.getPrimaryClip());
            clipboard.setPrimaryClip(ClipData.newPlainText("Guard", "keep clipboard"));
            capture(a);
            assertEquals("keep clipboard", clipboard.getPrimaryClip().getItemAt(0).getText());
            assertFalse((Boolean) field(a, "frameBusy"));
            Bitmap transparent = Bitmap.createBitmap(16, 16, Bitmap.Config.ARGB_8888);
            assertFalse(FrameCapture.hasPixels(transparent));
            transparent.eraseColor(Color.BLACK);
            assertTrue(
                "A legitimate opaque black frame must remain capturable",
                FrameCapture.hasPixels(transparent));
            transparent.recycle();
          });
    }
    File directory = new File(target().getCacheDir(), "frames");
    directory.mkdirs();
    File video = new File(directory, "frame-capture-source.mp4");
    copyAsset("test-video.mp4", video);
    Uri source = FileProvider.getUriForFile(target(), target().getPackageName() + ".files", video);
    AtomicReference<Uri> copied = new AtomicReference<>();
    try (ActivityScenario<MainActivity> scenario =
        ActivityScenario.launch(
            new Intent(target(), MainActivity.class)
                .setData(source)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))) {
      waitPlayer(scenario);
      long deadline = SystemClock.uptimeMillis() + 10000;
      AtomicBoolean ready = new AtomicBoolean();
      while (!ready.get() && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(a -> ready.set((Boolean) field(a, "fileFrameReady")));
        if (!ready.get()) SystemClock.sleep(80);
      }
      assertTrue("Decoder must render before capture is enabled", ready.get());
      CountDownLatch gpuDone = new CountDownLatch(1);
      AtomicReference<Bitmap> gpuImage = new AtomicReference<>();
      scenario.onActivity(
          a -> {
            androidx.media3.ui.PlayerView view =
                (androidx.media3.ui.PlayerView) field(a, "playerView");
            FrameCapture.pixelCopy(
                (android.view.TextureView) view.getVideoSurfaceView(),
                640,
                360,
                new Handler(Looper.getMainLooper()),
                image -> {
                  gpuImage.set(image);
                  gpuDone.countDown();
                });
          });
      assertTrue("GPU readback callback timed out", gpuDone.await(10, TimeUnit.SECONDS));
      assertNotNull("Source-surface GPU fallback must return a picture", gpuImage.get());
      assertTrue(FrameCapture.hasPixels(gpuImage.get()));
      gpuImage.get().recycle();
      scenario.onActivity(a -> capture(a));
      deadline = SystemClock.uptimeMillis() + 10000;
      while (copied.get() == null && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(
            a -> {
              ClipData clip = a.getSystemService(ClipboardManager.class).getPrimaryClip();
              if (clip != null && clip.getItemAt(0).getUri() != null)
                copied.set(clip.getItemAt(0).getUri());
            });
        if (copied.get() == null) SystemClock.sleep(80);
      }
      assertNotNull("Actual decoded frame must reach the clipboard", copied.get());
      assertTrue(GalleryFrames.isPublishedFrame(target(), copied.get()));
      try (InputStream input = target().getContentResolver().openInputStream(copied.get())) {
        Bitmap image = BitmapFactory.decodeStream(input);
        assertNotNull(image);
        try {
          assertTrue(FrameCapture.hasPixels(image));
          java.util.Set<Integer> colors = new java.util.HashSet<>();
          for (int y = 1; y < 16; y++)
            for (int x = 1; x < 16; x++)
              colors.add(image.getPixel(x * image.getWidth() / 16, y * image.getHeight() / 16));
          assertTrue(
              "Decoded test video must contain a picture, not a flat blank", colors.size() > 4);
          assertEquals(
              255, Color.alpha(image.getPixel(image.getWidth() / 2, image.getHeight() / 2)));
          try (OutputStream output =
              new FileOutputStream(
                  new File(target().getExternalFilesDir(null), "decoded-copied-frame.png"))) {
            image.compress(Bitmap.CompressFormat.PNG, 100, output);
          }
        } finally {
          image.recycle();
        }
      }
    } finally {
      if (copied.get() != null) target().getContentResolver().delete(copied.get(), null, null);
      InstrumentationRegistry.getInstrumentation()
          .runOnMainSync(
              () -> {
                ClipboardManager clipboard = target().getSystemService(ClipboardManager.class);
                if (previous.get() == null) clipboard.clearPrimaryClip();
                else clipboard.setPrimaryClip(previous.get());
              });
    }
  }

  private static void capture(MainActivity activity) {
    try {
      Method method = MainActivity.class.getDeclaredMethod("frame", boolean.class);
      method.setAccessible(true);
      method.invoke(activity, false);
    } catch (Exception error) {
      throw new AssertionError(error);
    }
  }

  @Test
  public void localPlaybackAndResume() throws Exception {
    File directory = new File(target().getCacheDir(), "frames");
    directory.mkdirs();
    File video = new File(directory, "test-video.mp4");
    copyAsset("test-video.mp4", video);
    Uri uri = FileProvider.getUriForFile(target(), target().getPackageName() + ".files", video);
    Intent intent =
        new Intent(target(), MainActivity.class)
            .setData(uri)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(intent)) {
      waitPlayer(scenario);
      scenario.onActivity(
          a -> {
            androidx.media3.exoplayer.ExoPlayer p =
                (androidx.media3.exoplayer.ExoPlayer) field(a, "player");
            p.seekTo(3000);
            p.pause();
          });
      SystemClock.sleep(500);
    }
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(intent)) {
      waitPlayer(scenario);
      scenario.onActivity(
          a -> {
            androidx.media3.exoplayer.ExoPlayer p =
                (androidx.media3.exoplayer.ExoPlayer) field(a, "player");
            assertTrue("Saved file position was not restored", p.getCurrentPosition() >= 2500);
          });
    }
  }

  private static void waitPlayer(ActivityScenario<MainActivity> scenario) throws Exception {
    long end = SystemClock.uptimeMillis() + 15000;
    AtomicBoolean ready = new AtomicBoolean();
    while (SystemClock.uptimeMillis() < end) {
      scenario.onActivity(
          a -> {
            androidx.media3.exoplayer.ExoPlayer p =
                (androidx.media3.exoplayer.ExoPlayer) field(a, "player");
            ready.set(p != null && p.isPlaying() && p.getVideoSize().width > 0);
          });
      if (ready.get()) return;
      SystemClock.sleep(100);
    }
    fail("Local file playback did not deliver a decoded picture.");
  }

  @Test
  public void embeddedMkvSubtitlesReachReadingPipeline() throws Exception {
    File directory = new File(target().getCacheDir(), "frames");
    directory.mkdirs();
    File video = new File(directory, "embedded-subtitles.mkv");
    copyAsset("embedded-subtitles.mkv", video);
    Uri uri = FileProvider.getUriForFile(target(), target().getPackageName() + ".files", video);
    try (ActivityScenario<MainActivity> scenario =
        ActivityScenario.launch(
            new Intent(target(), MainActivity.class)
                .setData(uri)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))) {
      waitPlayer(scenario);
      scenario.onActivity(
          a -> {
            androidx.media3.exoplayer.ExoPlayer p =
                (androidx.media3.exoplayer.ExoPlayer) field(a, "player");
            boolean found = false;
            for (androidx.media3.common.Tracks.Group group : p.getCurrentTracks().getGroups()) {
              if (group.getType() != androidx.media3.common.C.TRACK_TYPE_TEXT) continue;
              p.setTrackSelectionParameters(
                  p.getTrackSelectionParameters()
                      .buildUpon()
                      .setTrackTypeDisabled(androidx.media3.common.C.TRACK_TYPE_TEXT, false)
                      .setOverrideForType(
                          new androidx.media3.common.TrackSelectionOverride(
                              group.getMediaTrackGroup(), 0))
                      .build());
              found = true;
              break;
            }
            assertTrue("MKV embedded text track missing", found);
            p.seekTo(500);
          });
      long deadline = SystemClock.uptimeMillis() + 10000;
      AtomicBoolean cue = new AtomicBoolean();
      while (!cue.get() && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(
            a ->
                cue.set(
                    ((String) field(a, "embeddedCue")).contains("VideoCapture offline subtitle")));
        SystemClock.sleep(100);
      }
      assertTrue("Embedded text was not delivered to subtitle reading", cue.get());
    }
  }

  @Test
  public void selectedVideoAudioExtractionDoesNotUseAnotherTrack() throws Exception {
    File directory = new File(target().getCacheDir(), "frames");
    directory.mkdirs();
    File video = new File(directory, "multiple-audio.mp4");
    copyAsset("multiple-audio.mp4", video);
    Uri uri = FileProvider.getUriForFile(target(), target().getPackageName() + ".files", video);
    short[] silence = AudioDecoder.range(target(), uri, 0, 5000, new AtomicBoolean(), 0);
    short[] speech = AudioDecoder.range(target(), uri, 0, 5000, new AtomicBoolean(), 1);
    int silentPeak = 0, speechPeak = 0;
    for (short sample : silence) silentPeak = Math.max(silentPeak, Math.abs((int) sample));
    for (short sample : speech) speechPeak = Math.max(speechPeak, Math.abs((int) sample));
    assertTrue("The first audio track should be silent", silentPeak < 30);
    assertTrue("The selected second track must carry speech", speechPeak > 500);
  }

  @Test
  public void browserAddressesAndSelectionSurviveRecreation() throws Exception {
    target()
        .getSharedPreferences("player", Context.MODE_PRIVATE)
        .edit()
        .putString("browserTabs", "[\"https://chatgpt.com/\",\"https://example.com/\"]")
        .putString("browserSelected", "0")
        .apply();
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            call(a, "toggleBrowserPanel");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertEquals(2, ((java.util.List<?>) field(browser, "views")).size());
            assertEquals(0, field(browser, "selected"));
          });
      scenario.recreate();
      scenario.onActivity(
          a -> {
            call(a, "toggleBrowserPanel");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertEquals(2, ((java.util.List<?>) field(browser, "views")).size());
            assertEquals(0, field(browser, "selected"));
            assertEquals(
                "https://example.com/", new PlayerStore(target()).list("browserTabs", 32).get(1));
          });
    }
  }

  @Test
  public void localClipExportContainsSourceAudioAndVideo() throws Exception {
    File directory = new File(target().getCacheDir(), "frames");
    directory.mkdirs();
    File video = new File(directory, "clip-source.mp4");
    copyAsset("test-video.mp4", video);
    Uri uri = FileProvider.getUriForFile(target(), target().getPackageName() + ".files", video);
    Intent intent =
        new Intent(target(), MainActivity.class)
            .setData(uri)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
    AtomicReference<Uri> saved = new AtomicReference<>();
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(intent)) {
      waitPlayer(scenario);
      scenario.onActivity(a -> call(a, "record"));
      SystemClock.sleep(1900);
      scenario.onActivity(
          a -> {
            ((androidx.media3.exoplayer.ExoPlayer) field(a, "player")).pause();
            call(a, "record");
          });
      long deadline = SystemClock.uptimeMillis() + 25000;
      while (saved.get() == null && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(a -> saved.set((Uri) field(a, "lastRecording")));
        SystemClock.sleep(100);
      }
      assertNotNull("Clip export did not finish", saved.get());
      android.media.MediaExtractor extractor = new android.media.MediaExtractor();
      try {
        extractor.setDataSource(target(), saved.get(), null);
        boolean picture = false, sound = false;
        for (int i = 0; i < extractor.getTrackCount(); i++) {
          String mime = extractor.getTrackFormat(i).getString(android.media.MediaFormat.KEY_MIME);
          picture |= mime.startsWith("video/");
          sound |= mime.startsWith("audio/");
        }
        assertTrue(picture);
        assertTrue(sound);
      } finally {
        extractor.release();
      }
    } finally {
      if (saved.get() != null) target().getContentResolver().delete(saved.get(), null, null);
    }
  }

  @Test
  public void chatGptBrowserMicrophoneUsesExactOriginPolicy() throws Exception {
    if (firstBrowserPermission())
      assertEquals(
          android.content.pm.PackageManager.PERMISSION_DENIED,
          target().checkSelfPermission(Manifest.permission.RECORD_AUDIO));
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      AtomicReference<android.webkit.WebView> browser = new AtomicReference<>();
      AtomicBoolean granted = new AtomicBoolean();
      AtomicBoolean denied = new AtomicBoolean();
      scenario.onActivity(
          a -> {
            call(a, "toggleBrowserPanel");
            BrowserOverlay panel = (BrowserOverlay) field(a, "browser");
            android.webkit.WebView view =
                ((java.util.List<android.webkit.WebView>) field(panel, "views")).get(0);
            browser.set(view);
            android.webkit.WebChromeClient policy = view.getWebChromeClient();
            view.setWebChromeClient(
                new android.webkit.WebChromeClient() {
                  @Override
                  public void onPermissionRequest(android.webkit.PermissionRequest original) {
                    policy.onPermissionRequest(
                        new android.webkit.PermissionRequest() {
                          @Override
                          public Uri getOrigin() {
                            return original.getOrigin();
                          }

                          @Override
                          public String[] getResources() {
                            return original.getResources();
                          }

                          @Override
                          public void grant(String[] resources) {
                            assertArrayEquals(new String[] {RESOURCE_AUDIO_CAPTURE}, resources);
                            assertTrue((Boolean) field(a, "browserUsingMicrophone"));
                            assertTrue((Boolean) field(a, "browserAudioReady"));
                            assertNull(field(a, "microphone"));
                            assertNull(field(a, "usbAudio"));
                            granted.set(true);
                            original.grant(resources);
                          }

                          @Override
                          public void deny() {
                            denied.set(true);
                            original.deny();
                          }
                        });
                  }
                });
            view.setWebViewClient(
                new android.webkit.WebViewClient() {
                  @Override
                  public android.webkit.WebResourceResponse shouldInterceptRequest(
                      android.webkit.WebView v, android.webkit.WebResourceRequest r) {
                    return new android.webkit.WebResourceResponse(
                        "text/html",
                        "UTF-8",
                        200,
                        "OK",
                        java.util.Map.of(
                            "Content-Security-Policy", "default-src 'none'; connect-src 'none'"),
                        new ByteArrayInputStream(
                            "<html><body>Offline permission fixture</body></html>"
                                .getBytes(java.nio.charset.StandardCharsets.UTF_8)));
                  }
                });
            view.loadUrl("https://chatgpt.com/permission-fixture");
          });
      waitDocument(browser.get(), "https://chatgpt.com/permission-fixture");
      requestBrowserAudio(browser.get());
      approveBrowserMicrophone();
      if (firstBrowserPermission()) approveSystemMicrophone();
      String result = awaitJavascript(browser.get(), "window.__captureResult");
      assertTrue("Trusted browser audio was not granted", granted.get());
      assertFalse(denied.get());
      // A headless emulator may grant permission but have no readable audio hardware.
      assertTrue(result, result.equals("allowed") || result.equals("NotReadableError"));
      AtomicBoolean released = new AtomicBoolean();
      long releaseDeadline = SystemClock.uptimeMillis() + 5000;
      while (!released.get() && SystemClock.uptimeMillis() < releaseDeadline) {
        scenario.onActivity(a -> released.set(!(Boolean) field(a, "browserUsingMicrophone")));
        if (!released.get()) SystemClock.sleep(50);
      }
      assertTrue("Browser error/track stop must release app audio ownership", released.get());
      granted.set(false);
      scenario.onActivity(
          a -> browser.get().loadUrl("https://untrusted.example/permission-fixture"));
      waitDocument(browser.get(), "https://untrusted.example/permission-fixture");
      requestBrowserAudio(browser.get());
      assertEquals("NotAllowedError", awaitJavascript(browser.get(), "window.__captureResult"));
      assertFalse(granted.get());
      assertTrue(denied.get());
    }
  }

  private static void approveBrowserMicrophone() throws Exception {
    long deadline = SystemClock.uptimeMillis() + 10000;
    while (SystemClock.uptimeMillis() < deadline) {
      android.view.accessibility.AccessibilityNodeInfo root =
          InstrumentationRegistry.getInstrumentation().getUiAutomation().getRootInActiveWindow();
      if (root != null)
        for (android.view.accessibility.AccessibilityNodeInfo node :
            root.findAccessibilityNodeInfosByText("Allow microphone"))
          if (node.performAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK))
            return;
      SystemClock.sleep(80);
    }
    fail("Browser microphone disclosure was not presented");
  }

  private static void approveSystemMicrophone() throws Exception {
    long deadline = SystemClock.uptimeMillis() + 10000;
    while (SystemClock.uptimeMillis() < deadline) {
      android.view.accessibility.AccessibilityNodeInfo root =
          InstrumentationRegistry.getInstrumentation().getUiAutomation().getRootInActiveWindow();
      if (root != null)
        for (android.view.accessibility.AccessibilityNodeInfo node :
            root.findAccessibilityNodeInfosByViewId(
                "com.android.permissioncontroller:id/permission_allow_foreground_only_button"))
          if (node.performAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK))
            return;
      SystemClock.sleep(80);
    }
    fail("First browser use must request Android microphone permission");
  }

  private static void waitDocument(android.webkit.WebView view, String expectedUrl)
      throws Exception {
    long deadline = SystemClock.uptimeMillis() + 10000;
    while (SystemClock.uptimeMillis() < deadline) {
      String state = javascript(view, "document.readyState");
      if (state.equals("complete") && expectedUrl.equals(javascript(view, "location.href"))) return;
      SystemClock.sleep(50);
    }
    fail("Browser fixture did not load.");
  }

  private static void requestBrowserAudio(android.webkit.WebView view) {
    InstrumentationRegistry.getInstrumentation()
        .runOnMainSync(
            () ->
                view.evaluateJavascript(
                    "window.__captureResult='pending';navigator.mediaDevices.getUserMedia({audio:true}).then(s=>{window.__captureResult='allowed';s.getTracks().forEach(t=>t.stop());}).catch(e=>window.__captureResult=e.name);",
                    null));
  }

  private static String awaitJavascript(android.webkit.WebView view, String code) throws Exception {
    long deadline = SystemClock.uptimeMillis() + 10000;
    while (SystemClock.uptimeMillis() < deadline) {
      String value = javascript(view, code);
      if (!value.equals("pending") && !value.equals("null")) return value;
      SystemClock.sleep(50);
    }
    throw new AssertionError("Microphone permission did not complete.");
  }

  private static String javascript(android.webkit.WebView view, String code) throws Exception {
    CountDownLatch done = new CountDownLatch(1);
    AtomicReference<String> value = new AtomicReference<>("");
    InstrumentationRegistry.getInstrumentation()
        .runOnMainSync(
            () ->
                view.evaluateJavascript(
                    code,
                    result -> {
                      value.set(result);
                      done.countDown();
                    }));
    assertTrue(done.await(5, TimeUnit.SECONDS));
    return value.get().replace("\"", "");
  }

  @Test
  public void bundledOcrWorksOffline() throws Exception {
    OcrConsent.initialize(target());
    Bitmap image = Bitmap.createBitmap(1000, 200, Bitmap.Config.ARGB_8888);
    Canvas canvas = new Canvas(image);
    canvas.drawColor(Color.WHITE);
    Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    paint.setColor(Color.BLACK);
    paint.setTextSize(70);
    canvas.drawText("VideoCapture 12345", 30, 110, paint);
    try (com.google.mlkit.vision.text.TextRecognizer ocr =
        TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)) {
      String text =
          Tasks.await(ocr.process(InputImage.fromBitmap(image, 0)), 30, TimeUnit.SECONDS).getText();
      assertTrue(text, text.contains("12345"));
    } finally {
      image.recycle();
    }
  }

  @Test
  public void whisperRunsOnAndroidWithoutNetwork() throws Exception {
    File pcm = new File(target().getCacheDir(), "speech-test.pcm");
    copyAsset("speech-test.pcm", pcm);
    byte[] bytes;
    try (InputStream input = new FileInputStream(pcm)) {
      ByteArrayOutputStream out = new ByteArrayOutputStream();
      byte[] buffer = new byte[8192];
      int n;
      while ((n = input.read(buffer)) != -1) out.write(buffer, 0, n);
      bytes = out.toByteArray();
    }
    short[] samples = new short[bytes.length / 2];
    ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer().get(samples);
    CountDownLatch done = new CountDownLatch(1);
    AtomicReference<String> text = new AtomicReference<>("");
    AtomicReference<String> error = new AtomicReference<>();
    OfflineSpeech speech = new OfflineSpeech(target(), new Handler(Looper.getMainLooper()));
    try {
      speech.transcribe(
          samples,
          "en",
          new OfflineSpeech.Result() {
            public void complete(String value) {
              text.set(value);
              done.countDown();
            }

            public void error(String value) {
              error.set(value);
              done.countDown();
            }
          });
      assertTrue("Offline decoder timed out", done.await(150, TimeUnit.SECONDS));
      assertNull(error.get(), error.get());
      assertTrue(
          text.get(), text.get().toLowerCase(java.util.Locale.ROOT).contains("quick brown fox"));
    } finally {
      speech.close();
      pcm.delete();
    }
  }

  @Test
  public void usbAudioNeverFallsBackToTabletMicrophone() throws Exception {
    AudioInput input = new AudioInput();
    try {
      try {
        input.start(
            target(),
            null,
            false,
            new AudioInput.Listener() {
              public void pcm(short[] data, int count) {
                fail("Microphone fallback must not produce data.");
              }

              public void failed(String value) {}
            });
        fail("Missing USB input should fail explicitly.");
      } catch (IllegalStateException expected) {
        assertTrue(expected.getMessage().contains("fallback"));
      }
      assertFalse(input.active());
    } finally {
      input.close();
    }
  }

  @Test
  public void sourceSurfaceRecordingIncludesOnlySourceVideoAndPcm() throws Exception {
    File output = new File(target().getCacheDir(), "encoder-test.mp4");
    output.delete();
    AtomicReference<String> error = new AtomicReference<>();
    AtomicReference<File> saved = new AtomicReference<>();
    TestSurfaceSource source = new TestSurfaceSource();
    CaptureRecorder recording = null;
    try {
      recording =
          new CaptureRecorder(
              source,
              output,
              640,
              360,
              30,
              true,
              new CaptureRecorder.Result() {
                public void complete(File file) {
                  saved.set(file);
                }

                public void error(String value) {
                  error.set(value);
                }
              });
      short[] audio = new short[1024];
      for (int i = 0; i < audio.length; i++)
        audio[i] = (short) (Math.sin(i * 2 * Math.PI * 440 / 16000) * 6000);
      for (int i = 0; i < 32; i++) {
        recording.audio(audio, audio.length);
        SystemClock.sleep(64);
      }
      recording.close();
      recording = null;
      assertNull(error.get(), error.get());
      assertNotNull(saved.get());
      assertTrue(output.length() > 1000);
      android.media.MediaExtractor extractor = new android.media.MediaExtractor();
      try {
        extractor.setDataSource(output.getAbsolutePath());
        boolean video = false, sound = false;
        for (int i = 0; i < extractor.getTrackCount(); i++) {
          android.media.MediaFormat format = extractor.getTrackFormat(i);
          String mime = format.getString(android.media.MediaFormat.KEY_MIME);
          if (mime.startsWith("video/")) {
            video = true;
            assertEquals(640, format.getInteger(android.media.MediaFormat.KEY_WIDTH));
            assertEquals(360, format.getInteger(android.media.MediaFormat.KEY_HEIGHT));
          }
          if (mime.startsWith("audio/")) sound = true;
        }
        assertTrue(video);
        assertTrue(sound);
      } finally {
        extractor.release();
      }
      android.media.MediaMetadataRetriever retriever = new android.media.MediaMetadataRetriever();
      try {
        retriever.setDataSource(output.getAbsolutePath());
        Bitmap frame = retriever.getFrameAtTime(500000);
        assertNotNull(frame);
        int pixel = frame.getPixel(frame.getWidth() / 2, frame.getHeight() / 2);
        assertTrue(
            "The encoder must capture the supplied source surface",
            Color.red(pixel) > 170 && Color.green(pixel) < 70);
        frame.recycle();
      } finally {
        retriever.release();
      }
    } finally {
      if (recording != null) recording.close();
      source.encoderSurface(null, false);
      output.delete();
    }
  }

  private static final class TestSurfaceSource implements VideoSurfaceSource {
    private volatile boolean running;
    private Thread render;

    public void encoderSurface(android.view.Surface surface, boolean add) {
      if (!add) {
        running = false;
        if (render != null)
          try {
            render.join(2000);
          } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
          }
        return;
      }
      running = true;
      render =
          new Thread(
              () -> {
                android.opengl.EGLDisplay display =
                    android.opengl.EGL14.eglGetDisplay(android.opengl.EGL14.EGL_DEFAULT_DISPLAY);
                int[] version = new int[2];
                android.opengl.EGL14.eglInitialize(display, version, 0, version, 1);
                int[] attributes = {
                  android.opengl.EGL14.EGL_RENDERABLE_TYPE,
                  android.opengl.EGL14.EGL_OPENGL_ES2_BIT,
                  android.opengl.EGL14.EGL_RED_SIZE,
                  8,
                  android.opengl.EGL14.EGL_GREEN_SIZE,
                  8,
                  android.opengl.EGL14.EGL_BLUE_SIZE,
                  8,
                  0x3142,
                  1,
                  android.opengl.EGL14.EGL_NONE
                };
                android.opengl.EGLConfig[] configs = new android.opengl.EGLConfig[1];
                int[] count = new int[1];
                android.opengl.EGL14.eglChooseConfig(
                    display, attributes, 0, configs, 0, 1, count, 0);
                android.opengl.EGLContext context =
                    android.opengl.EGL14.eglCreateContext(
                        display,
                        configs[0],
                        android.opengl.EGL14.EGL_NO_CONTEXT,
                        new int[] {
                          android.opengl.EGL14.EGL_CONTEXT_CLIENT_VERSION,
                          2,
                          android.opengl.EGL14.EGL_NONE
                        },
                        0);
                android.opengl.EGLSurface window =
                    android.opengl.EGL14.eglCreateWindowSurface(
                        display, configs[0], surface, new int[] {android.opengl.EGL14.EGL_NONE}, 0);
                android.opengl.EGL14.eglMakeCurrent(display, window, window, context);
                android.opengl.GLES20.glViewport(0, 0, 640, 360);
                try {
                  while (running) {
                    android.opengl.GLES20.glClearColor(1, 0, 0, 1);
                    android.opengl.GLES20.glClear(android.opengl.GLES20.GL_COLOR_BUFFER_BIT);
                    android.opengl.EGLExt.eglPresentationTimeANDROID(
                        display, window, System.nanoTime());
                    android.opengl.EGL14.eglSwapBuffers(display, window);
                    SystemClock.sleep(33);
                  }
                } finally {
                  android.opengl.EGL14.eglMakeCurrent(
                      display,
                      android.opengl.EGL14.EGL_NO_SURFACE,
                      android.opengl.EGL14.EGL_NO_SURFACE,
                      android.opengl.EGL14.EGL_NO_CONTEXT);
                  android.opengl.EGL14.eglDestroySurface(display, window);
                  android.opengl.EGL14.eglDestroyContext(display, context);
                  android.opengl.EGL14.eglTerminate(display);
                }
              },
              "TestSourceSurface");
      render.start();
    }
  }

  private void copyAsset(String name, File file) throws Exception {
    try (InputStream input =
            InstrumentationRegistry.getInstrumentation().getContext().getAssets().open(name);
        OutputStream output = new FileOutputStream(file)) {
      byte[] buffer = new byte[8192];
      int n;
      while ((n = input.read(buffer)) != -1) output.write(buffer, 0, n);
    }
  }
}
