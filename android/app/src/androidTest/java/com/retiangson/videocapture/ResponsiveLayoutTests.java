package com.retiangson.videocapture;

import static org.junit.Assert.*;

import android.content.Context;
import android.graphics.Rect;
import android.os.SystemClock;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.widget.*;
import androidx.core.view.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.lang.reflect.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.junit.*;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class ResponsiveLayoutTests {
  @Rule
  public androidx.test.rule.GrantPermissionRule cameraPermission =
      androidx.test.rule.GrantPermissionRule.grant(android.Manifest.permission.CAMERA);

  private static Object field(Object instance, String name) {
    try {
      Field f = instance.getClass().getDeclaredField(name);
      f.setAccessible(true);
      return f.get(instance);
    } catch (Exception e) {
      throw new AssertionError(e);
    }
  }

  private static void invoke(Object instance, String name) {
    try {
      Method m = instance.getClass().getDeclaredMethod(name);
      m.setAccessible(true);
      m.invoke(instance);
    } catch (Exception e) {
      throw new AssertionError(e);
    }
  }

  private static void idle() {
    InstrumentationRegistry.getInstrumentation().waitForIdleSync();
    SystemClock.sleep(150);
  }

  @Before
  public void reset() {
    InstrumentationRegistry.getInstrumentation()
        .getTargetContext()
        .getSharedPreferences("player", Context.MODE_PRIVATE)
        .edit()
        .clear()
        .commit();
  }

  private static void assertInside(FrameLayout root, View panel) {
    assertTrue("Panel needs usable width", panel.getWidth() > 0);
    assertTrue("Panel needs usable height", panel.getHeight() > 0);
    assertTrue("Panel extends beyond left inset", panel.getLeft() >= root.getPaddingLeft());
    assertTrue("Panel extends beyond top inset", panel.getTop() >= root.getPaddingTop());
    assertTrue(
        "Panel extends beyond right inset",
        panel.getRight() <= root.getWidth() - root.getPaddingRight());
    assertTrue(
        "Panel extends beyond bottom/keyboard inset",
        panel.getBottom() <= root.getHeight() - root.getPaddingBottom());
  }

  @Test
  public void rotationLockRetainsStateAndReturnsControlToAndroid() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      idle();
      scenario.onActivity(
          a -> {
            Button lock = button((View) field(a, "menu"), "Lock rotation");
            assertNotNull(lock);
            assertFalse(lock.isSelected());
            lock.performClick();
            assertTrue(lock.isSelected());
            assertEquals(
                android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LOCKED,
                a.getRequestedOrientation());
          });
      scenario.recreate();
      idle();
      scenario.onActivity(
          a -> {
            Button lock = button((View) field(a, "menu"), "Lock rotation");
            assertTrue(lock.isSelected());
            assertEquals(
                android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LOCKED,
                a.getRequestedOrientation());
            lock.performClick();
            assertFalse(lock.isSelected());
            assertEquals(
                android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED,
                a.getRequestedOrientation());
            assertEquals("false", ((PlayerStore) field(a, "store")).text("rotationLocked", "true"));
          });
    }
  }

  @Test
  public void playerLevelsJumpOnTapAndRetainIndependentVolumes() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      idle();
      scenario.onActivity(
          a -> {
            @SuppressWarnings("unchecked")
            java.util.Map<String, SeekBar> levels =
                (java.util.Map<String, SeekBar>) field(a, "levelSliders");
            PlayerStore store = (PlayerStore) field(a, "store");
            tapLevel(levels.get("Video"), .8f);
            assertEquals(.8f, store.level("videoVolume", 0), .02f);
            assertEquals(.85f, (float) field(a, "readingVolume"), .001f);
            tapLevel(levels.get("Reading"), .25f);
            assertEquals(.25f, store.level("readingVolume", 0), .02f);
            assertEquals(.8f, store.level("videoVolume", 0), .02f);
            tapLevel(levels.get("Opacity"), 0);
            assertEquals(.35f, ((View) field(a, "menu")).getAlpha(), .001f);
            SeekBar video = levels.get("Video");
            video.setEnabled(false);
            tapLevel(video, .1f);
            assertEquals(.8f, store.level("videoVolume", 0), .02f);
            video.setEnabled(true);
            video.setLayoutDirection(View.LAYOUT_DIRECTION_RTL);
            tapLevel(video, .2f);
            assertEquals(.8f, store.level("videoVolume", 0), .02f);
          });
      scenario.recreate();
      idle();
      scenario.onActivity(
          a -> {
            assertEquals(.8f, (float) field(a, "videoVolume"), .02f);
            assertEquals(.25f, (float) field(a, "readingVolume"), .02f);
            assertEquals(.35f, ((View) field(a, "menu")).getAlpha(), .001f);
          });
    }
  }

  private static void tapLevel(SeekBar slider, float fraction) {
    float x =
        slider.getPaddingLeft()
            + fraction * (slider.getWidth() - slider.getPaddingLeft() - slider.getPaddingRight());
    long now = SystemClock.uptimeMillis();
    android.view.MotionEvent down =
        android.view.MotionEvent.obtain(now, now, 0, x, slider.getHeight() / 2f, 0);
    android.view.MotionEvent up =
        android.view.MotionEvent.obtain(now, now + 60, 1, x, slider.getHeight() / 2f, 0);
    slider.dispatchTouchEvent(down);
    slider.dispatchTouchEvent(up);
    down.recycle();
    up.recycle();
  }

  @Test
  public void startupShowsSourceActionsAndWindowedControls() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      idle();
      scenario.onActivity(
          a -> {
            assertFalse((Boolean) field(a, "fullscreen"));
            assertEquals(View.VISIBLE, ((View) field(a, "menu")).getVisibility());
            SourceChooser chooser = (SourceChooser) field(a, "sourceChooser");
            assertEquals(View.VISIBLE, chooser.getVisibility());
            assertInside((FrameLayout) field(a, "root"), chooser);
            LinearLayout actions = (LinearLayout) chooser.getChildAt(chooser.getChildCount() - 1);
            Rect full = new Rect(0, 0, actions.getWidth(), actions.getHeight());
            chooser.offsetDescendantRectToMyCoords(actions, full);
            assertTrue(
                "Source buttons are clipped above the player menu",
                full.top >= 0 && full.bottom <= chooser.getHeight());
            assertEquals("Select USB", ((Button) actions.getChildAt(0)).getText().toString());
            assertEquals("Open video", ((Button) actions.getChildAt(1)).getText().toString());
            ((Button) field(a, "fullscreenButton")).performClick();
            assertEquals(View.GONE, ((View) field(a, "menu")).getVisibility());
            ((Button) field(a, "fullscreenButton")).performClick();
            assertEquals(View.VISIBLE, ((View) field(a, "menu")).getVisibility());
          });
    }
  }

  @Test
  public void sourceButtonOpensUsbPickerFeedback() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      idle();
      scenario.onActivity(
          a -> {
            SourceChooser chooser = (SourceChooser) field(a, "sourceChooser");
            LinearLayout actions = (LinearLayout) chooser.getChildAt(chooser.getChildCount() - 1);
            ((Button) actions.getChildAt(0)).performClick();
          });
      idle();
      boolean feedback = false;
      long deadline = SystemClock.uptimeMillis() + 5000;
      while (!feedback && SystemClock.uptimeMillis() < deadline) {
        android.view.accessibility.AccessibilityNodeInfo window =
            InstrumentationRegistry.getInstrumentation().getUiAutomation().getRootInActiveWindow();
        feedback =
            window != null
                && (!window.findAccessibilityNodeInfosByText("USB video device").isEmpty()
                    || !window
                        .findAccessibilityNodeInfosByText("No USB capture device found")
                        .isEmpty());
        if (!feedback) SystemClock.sleep(100);
      }
      assertTrue("USB selection gave no visible device list or empty-state feedback", feedback);
      InstrumentationRegistry.getInstrumentation()
          .sendKeyDownUpSync(android.view.KeyEvent.KEYCODE_BACK);
      idle();
      scenario.onActivity(
          a -> {
            assertEquals(View.VISIBLE, ((View) field(a, "sourceChooser")).getVisibility());
            assertEquals(View.VISIBLE, ((View) field(a, "menu")).getVisibility());
          });
    }
  }

  private static Button button(View view, String label) {
    if (view instanceof Button && label.contentEquals(((Button) view).getText()))
      return (Button) view;
    if (view instanceof android.view.ViewGroup) {
      android.view.ViewGroup group = (android.view.ViewGroup) view;
      for (int i = 0; i < group.getChildCount(); i++) {
        Button found = button(group.getChildAt(i), label);
        if (found != null) return found;
      }
    }
    return null;
  }

  @Test
  public void optionHighlightsFollowStateAndSavedPreferences() throws Exception {
    Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
    context
        .getSharedPreferences("player", Context.MODE_PRIVATE)
        .edit()
        .putString("timeline", "false")
        .putString("fill", "true")
        .putString("reading", "true")
        .commit();
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      idle();
      scenario.onActivity(
          a -> {
            View menu = (View) field(a, "menu");
            Button fill = (Button) field(a, "pictureModeButton"),
                timeline = button(menu, "Timeline"),
                reading = button(menu, "Read subtitles");
            assertTrue(fill.isSelected());
            assertFalse(timeline.isSelected());
            assertTrue(reading.isSelected());
            try {
              java.lang.reflect.Method mode =
                  a.getClass().getDeclaredMethod("setPictureMode", PictureMode.class);
              mode.setAccessible(true);
              mode.invoke(a, PictureMode.REGULAR);
            } catch (Exception error) {
              throw new AssertionError(error);
            }
            timeline.performClick();
            reading.performClick();
            assertFalse(fill.isSelected());
            assertTrue(timeline.isSelected());
            assertFalse(reading.isSelected());
            assertEquals("Regular", androidx.core.view.ViewCompat.getStateDescription(fill));
            fill.setEnabled(false);
            assertEquals(.35f, fill.getAlpha(), .01f);
            fill.setEnabled(true);
            assertEquals(1f, fill.getAlpha(), .01f);
            Button browser = button(menu, "Browser");
            browser.performClick();
            assertTrue(browser.isSelected());
            BrowserOverlay overlay = (BrowserOverlay) field(a, "browser");
            overlay.setVisibility(View.GONE);
            assertFalse(browser.isSelected());
            Button audio = button(menu, "Audio to text");
            audio.performClick();
            assertTrue(audio.isSelected());
            BrowserOverlay panel = (BrowserOverlay) field(a, "browser");
            assertTrue(((Button) field(panel, "transcriptionButton")).isSelected());
            audio.performClick();
            assertFalse(audio.isSelected());
            Button nav = (Button) field(panel, "navigationButton");
            nav.performClick();
            assertTrue(nav.isSelected());
            nav.performClick();
            assertFalse(nav.isSelected());
            Button full = (Button) field(a, "fullscreenButton");
            full.performClick();
            assertTrue(full.isSelected());
            full.performClick();
            assertFalse(full.isSelected());
          });
      scenario.recreate();
      idle();
      scenario.onActivity(
          a -> {
            View menu = (View) field(a, "menu");
            assertFalse(((Button) field(a, "pictureModeButton")).isSelected());
            assertTrue(button(menu, "Timeline").isSelected());
            assertFalse(button(menu, "Read subtitles").isSelected());
          });
    }
  }

  @Test
  public void stretchAndPictureChoiceSurviveFullscreenAndRecreation() throws Exception {
    Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
    assertEquals(PictureMode.REGULAR, PictureMode.read(new PlayerStore(context)));
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            for (PictureMode mode : PictureMode.values()) {
              try {
                java.lang.reflect.Method setter =
                    a.getClass().getDeclaredMethod("setPictureMode", PictureMode.class);
                setter.setAccessible(true);
                setter.invoke(a, mode);
              } catch (Exception error) {
                throw new AssertionError(error);
              }
              assertEquals(
                  mode.resizeMode,
                  ((androidx.media3.ui.PlayerView) field(a, "playerView")).getResizeMode());
              assertEquals(
                  "Picture: " + mode.label,
                  ((Button) field(a, "pictureModeButton")).getText().toString());
              ((Button) field(a, "fullscreenButton")).performClick();
              ((Button) field(a, "fullscreenButton")).performClick();
              assertEquals(mode, PictureMode.read(new PlayerStore(context)));
            }
            assertArrayEquals(
                new int[] {1200, 675}, PictureMode.REGULAR.dimensions(1920, 1080, 1200, 800));
            assertArrayEquals(
                new int[] {1422, 800}, PictureMode.FILL.dimensions(1920, 1080, 1200, 800));
            assertArrayEquals(
                new int[] {1200, 800}, PictureMode.STRETCH.dimensions(1920, 1080, 1200, 800));
          });
      scenario.recreate();
      idle();
      scenario.onActivity(
          a ->
              assertEquals(
                  androidx.media3.ui.AspectRatioFrameLayout.RESIZE_MODE_FILL,
                  ((androidx.media3.ui.PlayerView) field(a, "playerView")).getResizeMode()));
    }
  }

  @Test
  public void controlsFitAndBrowserMaximizeRestoresWithinWindow() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            invoke(a, "toggleAudioPanel");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            ((View) field(browser, "navigation")).setVisibility(View.VISIBLE);
            ((View) field(a, "menu")).setVisibility(View.VISIBLE);
          });
      idle();
      scenario.onActivity(
          a -> {
            FrameLayout root = (FrameLayout) field(a, "root");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertInside(root, browser);
            EditText address = (EditText) field(browser, "address");
            assertTrue(
                "Address field was squeezed out by navigation buttons",
                address.getWidth() >= Ui.dp(a, 64));
            View page = (View) field(browser, "page");
            assertTrue(
                "Expanded tools must leave usable browser content",
                page.getHeight() >= Ui.dp(a, 48));
            LinearLayout header = (LinearLayout) browser.getChildAt(0);
            for (int i = 0; i < header.getChildCount(); i++) {
              View control = header.getChildAt(i);
              assertTrue("Header control clipped", control.getRight() <= header.getWidth());
              if (control instanceof Button) assertTrue(control.getWidth() >= Ui.dp(a, 48));
            }
            PlayerControls menu = (PlayerControls) field(a, "menu");
            assertInside(root, menu);
            for (String name : new String[] {"playButton", "recordButton"}) {
              View primary = (View) field(a, name);
              Rect bounds = new Rect(0, 0, primary.getWidth(), primary.getHeight());
              menu.offsetDescendantRectToMyCoords(primary, bounds);
              assertTrue(
                  "Primary control clipped on a narrow phone: " + name,
                  bounds.left >= 0 && bounds.right <= menu.getWidth());
            }
            assertTrue(
                "Player menu covers too much of the video",
                menu.getHeight() <= root.getHeight() * .76f);
            ((ScrollView) menu.content.getParent()).fullScroll(View.FOCUS_DOWN);
            invoke(browser, "maximize");
          });
      idle();
      scenario.onActivity(
          a -> {
            FrameLayout root = (FrameLayout) field(a, "root");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertInside(root, browser);
            assertEquals(
                root.getWidth() - root.getPaddingLeft() - root.getPaddingRight(),
                browser.getWidth());
            assertEquals(
                root.getHeight() - root.getPaddingTop() - root.getPaddingBottom(),
                browser.getHeight());
            invoke(browser, "maximize");
          });
      idle();
      scenario.onActivity(
          a -> {
            FrameLayout root = (FrameLayout) field(a, "root");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            assertInside(root, browser);
            PlayerControls menu = (PlayerControls) field(a, "menu");
            View volumes = menu.content.getChildAt(menu.content.getChildCount() - 1);
            assertTrue(
                "Volume controls cannot be reached by scrolling",
                volumes.getGlobalVisibleRect(new Rect()));
          });
    }
  }

  @Test
  public void activeBrowserSurvivesRotation() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      java.util.concurrent.atomic.AtomicReference<BrowserOverlay> original =
          new java.util.concurrent.atomic.AtomicReference<>();
      AtomicBoolean landscape = new AtomicBoolean();
      scenario.onActivity(
          a -> {
            invoke(a, "toggleAudioPanel");
            original.set((BrowserOverlay) field(a, "browser"));
            FrameLayout root = (FrameLayout) field(a, "root");
            landscape.set(root.getWidth() > root.getHeight());
          });
      idle();
      boolean initial = landscape.get();
      try {
        for (boolean expected : new boolean[] {!initial, initial}) {
          scenario.onActivity(
              a ->
                  a.setRequestedOrientation(
                      expected
                          ? android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
                          : android.content.pm.ActivityInfo.SCREEN_ORIENTATION_PORTRAIT));
          long deadline = SystemClock.uptimeMillis() + 10000;
          boolean rotated = false;
          while (!rotated && SystemClock.uptimeMillis() < deadline) {
            scenario.onActivity(
                a -> {
                  FrameLayout root = (FrameLayout) field(a, "root");
                  landscape.set(root.getWidth() > root.getHeight());
                });
            rotated = landscape.get() == expected;
            SystemClock.sleep(100);
          }
          assertTrue("The test display did not rotate", rotated);
          idle();
          scenario.onActivity(
              a -> {
                BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
                assertSame(
                    "Rotation should retain the browser and transcript", original.get(), browser);
                assertInside((FrameLayout) field(a, "root"), browser);
                assertEquals(View.VISIBLE, ((View) field(a, "audioPanel")).getVisibility());
              });
        }
      } finally {
        scenario.onActivity(
            a ->
                a.setRequestedOrientation(
                    android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED));
      }
    }
  }

  @Test
  public void browserStaysInsideKeyboardInsets() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> {
            invoke(a, "toggleBrowserPanel");
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            ((View) field(browser, "navigation")).setVisibility(View.VISIBLE);
          });
      idle();
      scenario.onActivity(
          a -> {
            BrowserOverlay browser = (BrowserOverlay) field(a, "browser");
            EditText address = (EditText) field(browser, "address");
            address.requestFocus();
            ((InputMethodManager) a.getSystemService(Context.INPUT_METHOD_SERVICE))
                .showSoftInput(address, InputMethodManager.SHOW_IMPLICIT);
          });
      AtomicBoolean keyboard = new AtomicBoolean();
      long deadline = SystemClock.uptimeMillis() + 7000;
      while (!keyboard.get() && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(
            a -> {
              WindowInsetsCompat insets = ViewCompat.getRootWindowInsets((View) field(a, "root"));
              keyboard.set(insets != null && insets.isVisible(WindowInsetsCompat.Type.ime()));
            });
        SystemClock.sleep(100);
      }
      assertTrue("Test keyboard did not open", keyboard.get());
      idle();
      scenario.onActivity(
          a -> assertInside((FrameLayout) field(a, "root"), (BrowserOverlay) field(a, "browser")));
    }
  }
}
