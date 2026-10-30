package com.retiangson.videocapture;

import android.graphics.Bitmap;
import android.os.Handler;
import android.view.*;
import java.util.function.Consumer;

/** Source-only GPU readback. An unavailable frame must never become a transparent clipboard PNG. */
final class FrameCapture {
  static boolean hasPixels(Bitmap image) {
    if (image == null || image.isRecycled()) return false;
    int width = image.getWidth(), height = image.getHeight();
    for (int row = 0; row < 18; row++)
      for (int col = 0; col < 32; col++)
        if ((image.getPixel(
                    Math.min(width - 1, col * width / 32), Math.min(height - 1, row * height / 18))
                >>> 24)
            != 0) return true;
    return false;
  }

  static Bitmap read(TextureView texture, int width, int height) {
    if (texture == null || !texture.isAvailable() || width <= 0 || height <= 0) return null;
    Bitmap image = texture.getBitmap(width, height);
    if (hasPixels(image)) return image;
    if (image != null) image.recycle();
    return null;
  }

  static void capture(
      TextureView texture, int width, int height, Handler handler, Consumer<Bitmap> result) {
    Bitmap image = read(texture, width, height);
    if (image != null) {
      result.accept(image);
      return;
    }
    pixelCopy(texture, width, height, handler, result);
  }

  static void pixelCopy(
      TextureView texture, int width, int height, Handler handler, Consumer<Bitmap> result) {
    if (texture == null
        || !texture.isAvailable()
        || texture.getSurfaceTexture() == null
        || width <= 0
        || height <= 0) {
      result.accept(null);
      return;
    }
    Bitmap target = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
    Surface surface = new Surface(texture.getSurfaceTexture());
    try {
      PixelCopy.request(
          surface,
          target,
          status -> {
            surface.release();
            if (status == PixelCopy.SUCCESS && hasPixels(target)) result.accept(target);
            else {
              target.recycle();
              result.accept(null);
            }
          },
          handler);
    } catch (IllegalArgumentException error) {
      surface.release();
      target.recycle();
      result.accept(null);
    }
  }
}
