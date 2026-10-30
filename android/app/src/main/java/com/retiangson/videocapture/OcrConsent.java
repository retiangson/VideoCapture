package com.retiangson.videocapture;

import android.app.*;
import android.content.Context;
import com.google.mlkit.common.MlKit;

/**
 * Delay the SDK itself, including its diagnostic initialization, until OCR is chosen and disclosed.
 */
final class OcrConsent implements AutoCloseable {
  private static boolean initialized;
  private final Activity activity;
  private final PlayerStore store;
  private boolean pending, declined;
  private AlertDialog dialog;

  OcrConsent(Activity activity, PlayerStore store) {
    this.activity = activity;
    this.store = store;
  }

  static synchronized void initialize(Context context) {
    if (!initialized) {
      MlKit.initialize(context.getApplicationContext());
      initialized = true;
    }
  }

  boolean ready(boolean explicit, Runnable onAllowed) {
    if (Boolean.parseBoolean(store.text("ocrDisclosureAccepted", "false"))) {
      initialize(activity);
      return true;
    }
    if (pending || declined && !explicit || activity.isFinishing()) return false;
    pending = true;
    dialog =
        new AlertDialog.Builder(activity)
            .setTitle("On-device text recognition")
            .setMessage(
                "OCR images and recognized text are processed on this device. Google's ML Kit SDK"
                    + " also collects device/app information, installation identifiers and"
                    + " diagnostic/performance metrics, which can be sent to Google when online."
                    + " Enable OCR only if you agree to this SDK processing. Media playback and"
                    + " recording work without OCR.")
            .setPositiveButton(
                "Enable OCR",
                (dialog, which) -> {
                  store.put("ocrDisclosureAccepted", "true");
                  initialize(activity);
                  pending = false;
                  onAllowed.run();
                })
            .setNegativeButton("Not now", (dialog, which) -> declined = true)
            .setOnCancelListener(dialog -> declined = true)
            .setOnDismissListener(dialog -> pending = false)
            .show();
    return false;
  }

  @Override
  public void close() {
    if (dialog != null) dialog.dismiss();
  }
}
