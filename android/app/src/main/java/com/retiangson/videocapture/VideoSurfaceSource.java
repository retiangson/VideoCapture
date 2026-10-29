package com.retiangson.videocapture;

import android.view.Surface;

interface VideoSurfaceSource {
  void encoderSurface(Surface surface, boolean add);
}
