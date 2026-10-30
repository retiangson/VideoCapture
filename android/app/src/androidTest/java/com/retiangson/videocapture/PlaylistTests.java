package com.retiangson.videocapture;

import static org.junit.Assert.*;

import android.content.*;
import android.net.Uri;
import android.os.*;
import android.provider.DocumentsContract;
import android.view.View;
import android.widget.*;
import androidx.core.content.FileProvider;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.rule.GrantPermissionRule;
import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.*;
import org.junit.*;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class PlaylistTests {
  @Rule
  public GrantPermissionRule permission =
      GrantPermissionRule.grant(android.Manifest.permission.CAMERA);

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

  private static void call(Object object, String name, Class<?>[] types, Object... args) {
    try {
      Method m = object.getClass().getDeclaredMethod(name, types);
      m.setAccessible(true);
      m.invoke(object, args);
    } catch (Exception error) {
      throw new AssertionError(error);
    }
  }

  private List<Uri> fixtures() throws Exception {
    File dir = new File(context().getCacheDir(), "frames");
    dir.mkdirs();
    List<Uri> uris = new ArrayList<>();
    for (String name : List.of("Episode A.mp4", "Episode B.mp4", "Episode C.mp4")) {
      File file = new File(dir, name);
      try (InputStream input =
              InstrumentationRegistry.getInstrumentation()
                  .getContext()
                  .getAssets()
                  .open("test-video.mp4");
          OutputStream output = new FileOutputStream(file)) {
        byte[] buffer = new byte[8192];
        for (int count; (count = input.read(buffer)) >= 0; ) output.write(buffer, 0, count);
      }
      uris.add(FileProvider.getUriForFile(context(), context().getPackageName() + ".files", file));
    }
    return uris;
  }

  private static void waitImport(ActivityScenario<MainActivity> scenario) throws Exception {
    AtomicBoolean done = new AtomicBoolean();
    long deadline = SystemClock.uptimeMillis() + 10000;
    while (!done.get() && SystemClock.uptimeMillis() < deadline) {
      scenario.onActivity(a -> done.set(!(Boolean) field(a, "importingPlaylist")));
      if (!done.get()) SystemClock.sleep(80);
    }
    assertTrue("Playlist import timed out", done.get());
  }

  @SuppressWarnings("unchecked")
  private static List<String> queue(MainActivity activity) {
    return (List<String>) field(activity, "queue");
  }

  @Test
  public void floatingQueueSupportsMultiAddRemoveAndPersistsWithoutDeletingFiles()
      throws Exception {
    List<Uri> media = fixtures();
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a ->
              call(
                  a,
                  "importFiles",
                  new Class<?>[] {List.class, boolean.class},
                  List.of(media.get(0), media.get(1), media.get(0), media.get(2)),
                  false));
      waitImport(scenario);
      scenario.onActivity(
          a -> {
            assertEquals(3, queue(a).size());
            assertNull("Adding to a playlist must not start playback", field(a, "player"));
            call(a, "playlist", new Class<?>[] {});
          });
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      SystemClock.sleep(200);
      scenario.onActivity(
          a -> {
            PlaylistOverlay panel = (PlaylistOverlay) field(a, "playlistPanel");
            assertEquals(View.VISIBLE, panel.getVisibility());
            ListView list = (ListView) field(panel, "list");
            assertEquals(3, list.getCount());
            View row = list.getChildAt(0);
            assertNotNull(row);
            ((Button) field(row, "remove")).performClick();
            assertEquals(List.of(media.get(1).toString(), media.get(2).toString()), queue(a));
            assertTrue(new File(a.getCacheDir(), "frames/Episode A.mp4").isFile());
            call(
                a,
                "movePlaylistItem",
                new Class<?>[] {String.class, int.class},
                media.get(2).toString(),
                -1);
            assertEquals(media.get(2).toString(), queue(a).get(0));
          });
      scenario.recreate();
      scenario.onActivity(
          a -> {
            assertEquals(List.of(media.get(2).toString(), media.get(1).toString()), queue(a));
            assertEquals(View.VISIBLE, ((View) field(a, "playlistPanel")).getVisibility());
            call(a, "clearPlaylist", new Class<?>[] {});
            assertTrue(queue(a).isEmpty());
            assertTrue(new File(a.getCacheDir(), "frames/Episode C.mp4").isFile());
          });
    }
  }

  @Test
  public void removingPrecedingItemKeepsCurrentIdentityAndPlayback() throws Exception {
    List<Uri> media = fixtures();
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(
          a -> call(a, "importFiles", new Class<?>[] {List.class, boolean.class}, media, false));
      waitImport(scenario);
      scenario.onActivity(a -> call(a, "playIndex", new Class<?>[] {int.class}, 1));
      AtomicBoolean ready = new AtomicBoolean();
      long deadline = SystemClock.uptimeMillis() + 10000;
      while (!ready.get() && SystemClock.uptimeMillis() < deadline) {
        scenario.onActivity(
            a ->
                ready.set(
                    !(Boolean) field(a, "busy")
                        && media.get(1).equals(field(a, "fileUri"))
                        && ((androidx.media3.exoplayer.ExoPlayer) field(a, "player")).isPlaying()));
        if (!ready.get()) SystemClock.sleep(80);
      }
      assertTrue(ready.get());
      scenario.onActivity(
          a -> {
            Object player = field(a, "player");
            call(a, "removePlaylistItem", new Class<?>[] {String.class}, media.get(0).toString());
            assertEquals(0, field(a, "selected"));
            assertEquals(media.get(1), field(a, "fileUri"));
            assertSame(player, field(a, "player"));
            call(
                a,
                "movePlaylistItem",
                new Class<?>[] {String.class, int.class},
                media.get(1).toString(),
                1);
            assertEquals(1, field(a, "selected"));
            call(a, "removePlaylistItem", new Class<?>[] {String.class}, media.get(1).toString());
            assertNull(field(a, "fileUri"));
            assertEquals(-1, field(a, "selected"));
          });
    }
  }

  @Test
  public void folderScanFiltersTraversesBoundsAndCancels() throws Exception {
    Map<String, List<FolderMedia.Document>> tree = new HashMap<>();
    tree.put(
        "root",
        List.of(
            new FolderMedia.Document("nested", "nested", DocumentsContract.Document.MIME_TYPE_DIR),
            new FolderMedia.Document("b", "B.mp4", "video/mp4"),
            new FolderMedia.Document("a", "A.mkv", "application/x-matroska"),
            new FolderMedia.Document("ignore", "notes.txt", "text/plain")));
    tree.put(
        "nested",
        List.of(
            new FolderMedia.Document("root", "cycle", DocumentsContract.Document.MIME_TYPE_DIR),
            new FolderMedia.Document("c", "C.FLAC", "application/octet-stream")));
    FolderMedia.Source source =
        new FolderMedia.Source() {
          public List<FolderMedia.Document> children(String id, CancellationSignal signal) {
            return tree.getOrDefault(id, List.of());
          }

          public Uri uri(String id) {
            return Uri.parse("content://fixture/" + id);
          }
        };
    FolderMedia.Result result = FolderMedia.scan(source, "root", new CancellationSignal());
    assertEquals(
        List.of("A.mkv", "B.mp4", "C.FLAC"),
        result.entries().stream()
            .map(FolderMedia.Entry::name)
            .collect(java.util.stream.Collectors.toList()));
    assertFalse(result.limited());
    List<FolderMedia.Document> many = new ArrayList<>();
    for (int i = 0; i < 250; i++)
      many.add(new FolderMedia.Document("file" + i, "Video " + i + ".mp4", "video/mp4"));
    tree.put("root", many);
    result = FolderMedia.scan(source, "root", new CancellationSignal());
    assertEquals(200, result.entries().size());
    assertTrue(result.limited());
    CancellationSignal cancelled = new CancellationSignal();
    cancelled.cancel();
    try {
      FolderMedia.scan(source, "root", cancelled);
      fail("Cancelled scans must stop");
    } catch (OperationCanceledException expected) {
    }
    assertFalse(FolderMedia.media("application/pdf", "fake.mp4"));
    Uri direct =
        DocumentsContract.buildDocumentUri("fixture.documents", "primary:Movies/Video.mp4");
    Uri treeUri = DocumentsContract.buildTreeDocumentUri("fixture.documents", "primary:Movies");
    assertEquals(
        FolderMedia.identity(direct),
        FolderMedia.identity(
            DocumentsContract.buildDocumentUriUsingTree(treeUri, "primary:Movies/Video.mp4")));
  }

  @Test
  public void floatingPlaylistFitsRotatedAndNarrowWindows() throws Exception {
    try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
      scenario.onActivity(a -> call(a, "playlist", new Class<?>[] {}));
      InstrumentationRegistry.getInstrumentation().waitForIdleSync();
      SystemClock.sleep(150);
      scenario.onActivity(
          a -> {
            FrameLayout root = (FrameLayout) field(a, "root");
            PlaylistOverlay panel = (PlaylistOverlay) field(a, "playlistPanel");
            assertTrue(panel.getWidth() > 0 && panel.getHeight() > 0);
            assertTrue(panel.getLeft() >= root.getPaddingLeft());
            assertTrue(panel.getRight() <= root.getWidth() - root.getPaddingRight());
            assertTrue(panel.getBottom() <= root.getHeight() - root.getPaddingBottom());
            a.getOnBackPressedDispatcher().onBackPressed();
            assertEquals(View.GONE, panel.getVisibility());
          });
    }
  }
}
