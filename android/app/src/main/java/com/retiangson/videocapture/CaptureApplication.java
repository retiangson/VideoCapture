package com.retiangson.videocapture;

import android.app.Application;
import com.serenegiant.utils.UVCUtils;

public final class CaptureApplication extends Application {
  @Override
  public void onCreate() {
    super.onCreate();
    UVCUtils.init(this);
  }
}
