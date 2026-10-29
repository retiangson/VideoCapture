package com.retiangson.videocapture;

import android.media.*;
import java.io.ByteArrayOutputStream;
import java.util.concurrent.atomic.AtomicBoolean;

final class AudioInput implements AutoCloseable {
  interface Listener {
    void pcm(short[] data, int count);

    void failed(String message);
  }

  private final AtomicBoolean running = new AtomicBoolean();
  private AudioRecord recorder;
  private Thread thread;
  private AudioTrack monitor;
  private boolean speaker;
  private float volume = 1;

  void start(
      android.content.Context context,
      AudioDeviceInfo preferred,
      boolean microphone,
      Listener listener)
      throws Exception {
    if (context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)
        != android.content.pm.PackageManager.PERMISSION_GRANTED)
      throw new SecurityException("Microphone permission is required.");
    if (!microphone && preferred == null)
      throw new IllegalStateException(
          "Select the USB capture card audio input. Tablet microphone fallback is disabled.");
    if (running.get()) throw new IllegalStateException("Audio input is already open.");
    int min =
        Math.max(
            4096,
            AudioRecord.getMinBufferSize(
                16000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT));
    recorder =
        new AudioRecord.Builder()
            .setAudioSource(
                microphone
                    ? MediaRecorder.AudioSource.VOICE_RECOGNITION
                    : MediaRecorder.AudioSource.DEFAULT)
            .setAudioFormat(
                new AudioFormat.Builder()
                    .setSampleRate(16000)
                    .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .build())
            .setBufferSizeInBytes(min * 2)
            .build();
    if (preferred != null && !recorder.setPreferredDevice(preferred)) {
      recorder.release();
      recorder = null;
      throw new IllegalStateException("Android could not route the selected audio input.");
    }
    if (!microphone && preferred == null) {
      recorder.release();
      recorder = null;
      throw new IllegalStateException(
          "Select the USB capture card audio input. Tablet microphone fallback is disabled.");
    }
    if (recorder.getState() != AudioRecord.STATE_INITIALIZED) {
      recorder.release();
      recorder = null;
      throw new IllegalStateException("The selected audio format is unavailable.");
    }
    running.set(true);
    recorder.startRecording();
    thread =
        new Thread(
            () -> {
              short[] buffer = new short[1024];
              boolean verified = microphone;
              long routeDeadline = android.os.SystemClock.elapsedRealtime() + 1000;
              try {
                while (running.get()) {
                  int count = recorder.read(buffer, 0, buffer.length, AudioRecord.READ_BLOCKING);
                  if (count < 0)
                    throw new IllegalStateException("Audio input disconnected (" + count + ").");
                  if (!microphone && count > 0) {
                    AudioDeviceInfo routed = recorder.getRoutedDevice();
                    if (routed == null || routed.getId() != preferred.getId()) {
                      if (!verified && android.os.SystemClock.elapsedRealtime() < routeDeadline)
                        continue;
                      throw new IllegalStateException(
                          "USB audio routing failed; microphone audio was not used.");
                    }
                    verified = true;
                  }
                  if (count > 0) {
                    listener.pcm(buffer, count);
                    synchronized (this) {
                      if (speaker && monitor != null)
                        monitor.write(buffer, 0, count, AudioTrack.WRITE_BLOCKING);
                    }
                  }
                }
              } catch (Exception error) {
                if (running.get()) listener.failed(error.getMessage());
              } finally {
                running.set(false);
                try {
                  if (recorder != null) recorder.stop();
                } catch (IllegalStateException ignored) {
                }
              }
            },
            "CaptureAudio");
    thread.start();
  }

  synchronized void monitor(boolean enabled, float level) {
    speaker = enabled;
    volume = level;
    if (enabled && monitor == null) {
      int size =
          Math.max(
              4096,
              AudioTrack.getMinBufferSize(
                  16000, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT));
      monitor =
          new AudioTrack.Builder()
              .setAudioAttributes(
                  new AudioAttributes.Builder()
                      .setUsage(AudioAttributes.USAGE_MEDIA)
                      .setContentType(AudioAttributes.CONTENT_TYPE_MOVIE)
                      .build())
              .setAudioFormat(
                  new AudioFormat.Builder()
                      .setSampleRate(16000)
                      .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                      .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                      .build())
              .setBufferSizeInBytes(size)
              .build();
      monitor.play();
    }
    if (monitor != null) monitor.setVolume(enabled ? volume : 0);
  }

  boolean active() {
    return running.get();
  }

  @Override
  public void close() {
    running.set(false);
    if (recorder != null) {
      try {
        recorder.stop();
      } catch (IllegalStateException ignored) {
      }
    }
    if (thread != null && thread != Thread.currentThread())
      try {
        thread.join(1500);
      } catch (InterruptedException interrupted) {
        Thread.currentThread().interrupt();
      }
    synchronized (this) {
      if (monitor != null) {
        monitor.release();
        monitor = null;
      }
    }
    if (recorder != null) {
      recorder.release();
      recorder = null;
    }
    thread = null;
  }

  static final class Samples {
    private final ByteArrayOutputStream bytes = new ByteArrayOutputStream();

    synchronized void add(short[] samples, int count) {
      int allowed = Math.min(count, 16000 * 120 - bytes.size() / 2);
      for (int i = 0; i < allowed; i++) {
        bytes.write(samples[i] & 255);
        bytes.write((samples[i] >> 8) & 255);
      }
    }

    synchronized short[] array() {
      byte[] data = bytes.toByteArray();
      short[] samples = new short[data.length / 2];
      for (int i = 0; i < samples.length; i++)
        samples[i] = (short) ((data[i * 2] & 255) | (data[i * 2 + 1] << 8));
      return samples;
    }

    synchronized int size() {
      return bytes.size() / 2;
    }
  }
}
