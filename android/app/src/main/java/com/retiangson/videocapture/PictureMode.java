package com.retiangson.videocapture;

import androidx.media3.ui.AspectRatioFrameLayout;

/** Stable persisted display modes; capture/recording always retain the original source frames. */
@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
enum PictureMode {
  REGULAR("Regular", AspectRatioFrameLayout.RESIZE_MODE_FIT),
  FILL("Fill", AspectRatioFrameLayout.RESIZE_MODE_ZOOM),
  STRETCH("Stretch", AspectRatioFrameLayout.RESIZE_MODE_FILL);

  final String label;
  final int resizeMode;

  PictureMode(String label, int resizeMode) {
    this.label = label;
    this.resizeMode = resizeMode;
  }

  static PictureMode read(PlayerStore store) {
    String saved = store.text("pictureMode", "");
    if (saved.isEmpty()) return Boolean.parseBoolean(store.text("fill", "false")) ? FILL : REGULAR;
    try {
      return valueOf(saved);
    } catch (IllegalArgumentException error) {
      return REGULAR;
    }
  }

  int[] dimensions(int sourceWidth, int sourceHeight, int width, int height) {
    if (this == STRETCH) return new int[] {width, height};
    float scale =
        this == FILL
            ? Math.max(width / (float) sourceWidth, height / (float) sourceHeight)
            : Math.min(width / (float) sourceWidth, height / (float) sourceHeight);
    return new int[] {Math.round(sourceWidth * scale), Math.round(sourceHeight * scale)};
  }
}
