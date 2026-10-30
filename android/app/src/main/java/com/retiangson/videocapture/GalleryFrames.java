package com.retiangson.videocapture;

import android.content.*;
import android.graphics.Bitmap;
import android.net.Uri;
import android.provider.MediaStore;
import java.io.*;
import java.util.UUID;

/** Publishes complete PNGs before exposing their stable MediaStore URIs to other processes. */
final class GalleryFrames {
  static Uri save(Context context, Bitmap image) throws IOException {
    ContentResolver resolver = context.getContentResolver();
    ContentValues values = new ContentValues();
    values.put(MediaStore.Images.Media.DISPLAY_NAME, "Frame-" + UUID.randomUUID() + ".png");
    values.put(MediaStore.Images.Media.MIME_TYPE, "image/png");
    values.put(MediaStore.Images.Media.RELATIVE_PATH, "Pictures/VideoCapture");
    values.put(MediaStore.Images.Media.WIDTH, image.getWidth());
    values.put(MediaStore.Images.Media.HEIGHT, image.getHeight());
    values.put(MediaStore.Images.Media.IS_PENDING, 1);
    Uri uri = resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values);
    if (uri == null) throw new IOException("Could not create the Gallery image.");
    boolean published = false;
    try {
      try (OutputStream output = resolver.openOutputStream(uri, "w")) {
        if (output == null || !image.compress(Bitmap.CompressFormat.PNG, 100, output))
          throw new IOException("Could not write the captured image.");
      }
      values.clear();
      values.put(MediaStore.Images.Media.IS_PENDING, 0);
      if (resolver.update(uri, values, null, null) != 1)
        throw new IOException("Could not publish the captured image.");
      published = true;
      return uri;
    } finally {
      if (!published) {
        try {
          resolver.delete(uri, null, null);
        } catch (RuntimeException ignored) {
          /* Preserve the original write/publish error. */
        }
      }
    }
  }

  static void copy(Context context, Uri uri) {
    context
        .getSystemService(ClipboardManager.class)
        .setPrimaryClip(ClipData.newUri(context.getContentResolver(), "Video frame", uri));
  }

  static boolean isPublishedFrame(Context context, Uri uri) {
    if (uri == null || !"content".equals(uri.getScheme()) || !"media".equals(uri.getAuthority()))
      return false;
    String[] columns = {
      MediaStore.Images.Media.DISPLAY_NAME,
      MediaStore.Images.Media.RELATIVE_PATH,
      MediaStore.Images.Media.OWNER_PACKAGE_NAME,
      MediaStore.Images.Media.MIME_TYPE,
      MediaStore.Images.Media.IS_PENDING
    };
    try (var row = context.getContentResolver().query(uri, columns, null, null, null)) {
      return row != null
          && row.moveToFirst()
          && row.getString(0).startsWith("Frame-")
          && "Pictures/VideoCapture/".equals(row.getString(1))
          && context.getPackageName().equals(row.getString(2))
          && "image/png".equals(row.getString(3))
          && row.getInt(4) == 0;
    } catch (RuntimeException error) {
      return false;
    }
  }
}
