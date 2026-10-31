package com.retiangson.videocapture;

import static org.junit.Assert.*;

import android.Manifest;
import android.graphics.Rect;
import android.graphics.drawable.InsetDrawable;
import android.view.*;
import android.webkit.WebView;
import android.widget.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.rule.GrantPermissionRule;
import java.lang.reflect.Field;
import java.util.*;
import org.junit.*;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class CompactControlsTests {
  @Rule
  public GrantPermissionRule permissions = GrantPermissionRule.grant(Manifest.permission.CAMERA);

  @Before
  public void reset() {
    InstrumentationRegistry.getInstrumentation()
        .getTargetContext()
        .getSharedPreferences("player", 0)
        .edit()
        .clear()
        .commit();
  }

  private static Object field(Object object, String name) {
    try {
      Field field = object.getClass().getDeclaredField(name);
      field.setAccessible(true);
      return field.get(object);
    } catch (Exception error) {
      throw new AssertionError(error);
    }
  }

  private static Button button(View view, String label) {
    if (view instanceof Button b && label.contentEquals(b.getText())) return b;
    if (view instanceof ViewGroup group)
      for (int i = 0; i < group.getChildCount(); i++) {
        Button found = button(group.getChildAt(i), label);
        if (found != null) return found;
      }
    return null;
  }

  private static List<String> options(MainActivity activity, String category) {
    assertNotNull(button((View) field(activity, "menu"), category));
    button((View) field(activity, "menu"), category).performClick();
    PopupMenu popup = (PopupMenu) field(activity, "optionsPopup");
    List<String> labels = new ArrayList<>();
    for (int i = 0; i < popup.getMenu().size(); i++)
      labels.add(popup.getMenu().getItem(i).getTitle().toString());
    popup.dismiss();
    return labels;
  }

  @Test
  public void categoriesKeepAllOptionsReachableWithoutDuplicateCaptureTools() {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          activity -> {
            View menu = (View) field(activity, "menu");
            assertEquals(
                List.of("Open files", "Open folder", "USB capture"), options(activity, "Source"));
            assertEquals(
                List.of(
                    "Audio input",
                    "Audio track",
                    "Subtitle track",
                    "Read subtitles",
                    "Reading voice",
                    "Audio to text"),
                options(activity, "Audio"));
            assertEquals(
                List.of("Picture: Regular", "Mode / FPS", "Timeline", "Brightness"),
                options(activity, "Video"));
            assertEquals(List.of("Licenses", "Privacy"), options(activity, "More"));
            for (String label :
                List.of(
                    "Copy frame",
                    "Save frame",
                    "Copy recording",
                    "Audio input",
                    "Audio track",
                    "Read subtitles",
                    "Open",
                    "Open folder",
                    "USB")) assertNull("Duplicate main-menu action: " + label, button(menu, label));
            for (String label : List.of("Source", "Playlist", "Browser", "Audio", "Video", "More"))
              assertNotNull(button(menu, label));
            button(menu, "Browser").performClick();
            BrowserOverlay browser = (BrowserOverlay) field(activity, "browser");
            for (String label :
                List.of(
                    "Copy frame",
                    "Save frame",
                    "Copy recording",
                    "Record",
                    "Text",
                    "Audio to text"))
              assertNotNull("Lost browser action: " + label, button(browser, label));
          });
    }
  }

  @Test
  public void headerControlsAndLevelRowsFitWithoutShrinkingTouchTargets() {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          activity -> button((View) field(activity, "menu"), "Browser").performClick());
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      scenario.onActivity(
          activity -> {
            BrowserOverlay browser = (BrowserOverlay) field(activity, "browser");
            LinearLayout header = (LinearLayout) browser.getChildAt(0);
            assertEquals(5, header.getChildCount());
            assertTrue(
                "Browser caption squeezed out",
                header.getChildAt(0).getWidth() >= Ui.dp(activity, 40));
            for (int index = 2; index < 5; index++) {
              View control = header.getChildAt(index);
              assertEquals(Ui.dp(activity, 48), control.getWidth());
              assertTrue(control.getBackground() instanceof InsetDrawable);
              assertTrue(control.getRight() <= header.getWidth());
            }
            assertEquals(header.getBottom(), browser.getChildAt(1).getTop());
            for (String name :
                List.of("fullscreenButton", "rotationLockButton", "hideMenuButton")) {
              View control = (View) field(activity, name);
              assertEquals(Ui.dp(activity, 48), control.getWidth());
              assertTrue(control.getBackground() instanceof InsetDrawable);
            }
            @SuppressWarnings("unchecked")
            Map<String, SeekBar> levels = (Map<String, SeekBar>) field(activity, "levelSliders");
            for (SeekBar level : levels.values()) {
              View parent = (View) level.getParent();
              assertEquals(Ui.dp(activity, 48), parent.getHeight());
              assertTrue(level.getWidth() > 0);
              assertEquals(Ui.dp(activity, 48), level.getHeight());
            }
          });
    }
  }

  @Test
  public void closingAnEmbeddedTabKeepsTheOtherSelectedPageAndPersists() {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          activity -> {
            button((View) field(activity, "menu"), "Browser").performClick();
            BrowserOverlay browser = (BrowserOverlay) field(activity, "browser");
            browser.add("https://one.test/");
            browser.add("https://two.test/");
          });
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      scenario.onActivity(
          activity -> {
            BrowserOverlay browser = (BrowserOverlay) field(activity, "browser");
            @SuppressWarnings("unchecked")
            List<WebView> views = (List<WebView>) field(browser, "views");
            WebView active = views.get(2);
            LinearLayout tabs = (LinearLayout) field(browser, "tabs");
            FrameLayout chip = (FrameLayout) tabs.getChildAt(0);
            assertEquals(2, chip.getChildCount());
            View close = chip.getChildAt(1);
            Rect bounds = new Rect(0, 0, close.getWidth(), close.getHeight());
            chip.offsetDescendantRectToMyCoords(close, bounds);
            assertTrue(bounds.right <= chip.getWidth() && bounds.left >= 0);
            close.performClick();
            assertEquals(2, views.size());
            assertSame(active, views.get((Integer) field(browser, "selected")));
            FrameLayout selected = (FrameLayout) tabs.getChildAt(1);
            selected.getChildAt(1).performClick();
            assertEquals(1, views.size());
            assertEquals(0, field(browser, "selected"));
            assertEquals(
                List.of("https://one.test/"),
                ((PlayerStore) field(activity, "store")).list("browserTabs", 32));
          });
      scenario.recreate();
      scenario.onActivity(
          activity -> {
            button((View) field(activity, "menu"), "Browser").performClick();
            BrowserOverlay browser = (BrowserOverlay) field(activity, "browser");
            assertEquals(1, ((List<?>) field(browser, "views")).size());
          });
    }
  }
}
