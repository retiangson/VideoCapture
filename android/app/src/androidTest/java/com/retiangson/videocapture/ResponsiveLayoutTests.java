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
