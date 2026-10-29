package com.retiangson.videocapture;

import android.content.Context;
import android.os.Handler;
import java.io.*;
import java.security.MessageDigest;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class OfflineSpeech implements AutoCloseable {
  interface Result {
    void complete(String text);

    void error(String message);
  }

  private final Context context;
  private final Handler main;
  private final ExecutorService worker = Executors.newSingleThreadExecutor();
  private final java.util.concurrent.atomic.AtomicInteger generation =
      new java.util.concurrent.atomic.AtomicInteger();
  private volatile boolean closed;
  private volatile boolean loaded;
  private static final String MODEL = "ggml-small-q5_1.bin",
      HASH = "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb";

  OfflineSpeech(Context context, Handler main) {
    this.context = context.getApplicationContext();
    this.main = main;
  }

  void transcribe(short[] pcm, String language, Result result) {
    if (closed) return;
    int job = generation.incrementAndGet();
    worker.execute(
        () -> {
          try {
            int peak = 0;
            for (short sample : pcm) peak = Math.max(peak, Math.abs((int) sample));
            if (peak < 100) {
              main.post(
                  () -> {
                    if (!closed && job == generation.get()) result.complete("");
                  });
              return;
            }
            File model = new File(context.getFilesDir(), MODEL);
            if (!model.exists()) {
              File part = new File(context.getFilesDir(), MODEL + ".part");
              MessageDigest digest = MessageDigest.getInstance("SHA-256");
              try {
                try (InputStream input = context.getAssets().open(MODEL);
                    OutputStream output = new FileOutputStream(part)) {
                  byte[] buffer = new byte[65536];
                  int count;
                  while ((count = input.read(buffer)) != -1) {
                    if (job != generation.get())
                      throw new java.util.concurrent.CancellationException();
                    digest.update(buffer, 0, count);
                    output.write(buffer, 0, count);
                  }
                }
                StringBuilder hash = new StringBuilder();
                for (byte value : digest.digest()) hash.append(String.format("%02x", value));
                if (!HASH.contentEquals(hash)) {
                  part.delete();
                  throw new IOException("Speech model checksum mismatch.");
                }
                if (!part.renameTo(model))
                  throw new IOException("Could not prepare offline speech model.");
              } finally {
                if (part.exists()) part.delete();
              }
            }
            if (job != generation.get()) throw new java.util.concurrent.CancellationException();
            if (!loaded) {
              System.loadLibrary("videocapture_speech");
              loaded = true;
            }
            long epoch = currentEpochNative();
            if (job != generation.get()) throw new java.util.concurrent.CancellationException();
            String text = transcribeNative(model.getAbsolutePath(), pcm, language, epoch);
            if (job == generation.get() && !closed)
              main.post(
                  () -> {
                    if (!closed && job == generation.get()) result.complete(text.trim());
                  });
          } catch (Throwable error) {
            if (!closed && job == generation.get())
              main.post(
                  () -> {
                    if (!closed && job == generation.get())
                      result.error("Offline speech: " + error.getMessage());
                  });
          }
        });
  }

  void cancel() {
    generation.incrementAndGet();
    if (loaded) cancelNative();
  }

  @Override
  public void close() {
    if (closed) return;
    closed = true;
    cancel();
    worker.execute(
        () -> {
          if (loaded) releaseNative();
        });
    worker.shutdown();
  }

  private static native String transcribeNative(
      String path, short[] samples, String language, long epoch);

  private static native long currentEpochNative();

  private static native void cancelNative();

  private static native void releaseNative();
}
