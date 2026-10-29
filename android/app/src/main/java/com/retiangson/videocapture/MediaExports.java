package com.retiangson.videocapture;

import android.content.*;
import android.net.Uri;
import android.provider.MediaStore;
import java.io.*;
import java.util.concurrent.*;
import java.util.function.BiConsumer;

/** Process-owned gallery writes may finish after the originating activity is destroyed. */
final class MediaExports {
  private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();

  static void save(Context context, File file, BiConsumer<Uri, String> result) {
    Context app = context.getApplicationContext();
    WORKER.execute(
        () -> {
          Uri uri = null;
          try {
            ContentValues values = new ContentValues();
            values.put(MediaStore.Video.Media.DISPLAY_NAME, file.getName());
            values.put(MediaStore.Video.Media.MIME_TYPE, "video/mp4");
            values.put(MediaStore.Video.Media.RELATIVE_PATH, "Movies/VideoCapture");
            values.put(MediaStore.Video.Media.IS_PENDING, 1);
            uri =
                app.getContentResolver()
                    .insert(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, values);
            if (uri == null) throw new IOException("Could not create gallery recording.");
            try (InputStream input = new FileInputStream(file);
                OutputStream output = app.getContentResolver().openOutputStream(uri)) {
              if (output == null) throw new IOException("Gallery output is unavailable.");
              byte[] buffer = new byte[65536];
              int count;
              while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
            }
            values.clear();
            values.put(MediaStore.Video.Media.IS_PENDING, 0);
            app.getContentResolver().update(uri, values, null, null);
            file.delete();
            result.accept(uri, null);
          } catch (Exception error) {
            if (uri != null) {
              try {
                app.getContentResolver().delete(uri, null, null);
              } catch (Exception cleanup) {
                error.addSuppressed(cleanup);
              }
            }
            result.accept(
                null,
                "Could not save recording: " + error.getMessage() + ". Private export was kept.");
          }
        });
  }
}
