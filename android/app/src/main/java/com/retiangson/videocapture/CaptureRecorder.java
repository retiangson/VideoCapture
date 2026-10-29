package com.retiangson.videocapture;

import android.media.*;
import android.view.Surface;
import java.io.File;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ArrayBlockingQueue;

final class CaptureRecorder implements AutoCloseable {
  interface Result {
    void complete(File file);

    void error(String message);
  }

  private final VideoSurfaceSource source;
  private final File file;
  private final Result result;
  private MediaCodec video, audio;
  private MediaMuxer muxer;
  private Surface surface;
  private int videoTrack = -1, audioTrack = -1;
  private boolean muxing, withAudio, failed;
  private volatile boolean stopping;
  private final java.util.concurrent.atomic.AtomicBoolean closed =
      new java.util.concurrent.atomic.AtomicBoolean();
  private Thread videoThread, audioThread;
  private final ArrayBlockingQueue<byte[]> pcm = new ArrayBlockingQueue<>(128);
  private final List<Packet> pending = new ArrayList<>();
  private int pendingBytes;
  private long videoStart = -1;

  private record Packet(int track, byte[] bytes, long pts, int flags) {}

  CaptureRecorder(
      VideoSurfaceSource source,
      File file,
      int width,
      int height,
      int fps,
      boolean withAudio,
      Result result)
      throws Exception {
    this.source = source;
    this.file = file;
    this.result = result;
    this.withAudio = withAudio;
    try {
      muxer = new MediaMuxer(file.getAbsolutePath(), MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
      MediaFormat format = MediaFormat.createVideoFormat("video/avc", width, height);
      format.setInteger(
          MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
      format.setInteger(
          MediaFormat.KEY_BIT_RATE, Math.min(40_000_000, Math.max(8_000_000, width * height * 6)));
      format.setInteger(MediaFormat.KEY_FRAME_RATE, Math.max(1, Math.min(60, fps)));
      format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
      video = MediaCodec.createEncoderByType("video/avc");
      video.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
      surface = video.createInputSurface();
      video.start();
      if (withAudio) {
        MediaFormat af = MediaFormat.createAudioFormat("audio/mp4a-latm", 16000, 1);
        af.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
        af.setInteger(MediaFormat.KEY_BIT_RATE, 64000);
        audio = MediaCodec.createEncoderByType("audio/mp4a-latm");
        audio.configure(af, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        audio.start();
      }
      source.encoderSurface(surface, true);
      videoThread = new Thread(() -> drainVideo(), "VideoEncoder");
      videoThread.start();
      if (withAudio) {
        audioThread = new Thread(() -> encodeAudio(), "AudioEncoder");
        audioThread.start();
      }
    } catch (Exception e) {
      dispose();
      file.delete();
      throw e;
    }
  }

  void audio(short[] data, int count) {
    if (!withAudio || stopping) return;
    byte[] bytes = new byte[count * 2];
    for (int i = 0; i < count; i++) {
      bytes[i * 2] = (byte) data[i];
      bytes[i * 2 + 1] = (byte) (data[i] >> 8);
    }
    if (!pcm.offer(bytes)) fail("USB audio encoder could not keep up.");
  }

  private void drainVideo() {
    try {
      MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
      long stoppedAt = 0;
      while (true) {
        if (stopping && stoppedAt == 0) {
          stoppedAt = System.nanoTime();
          video.signalEndOfInputStream();
        }
        int index = video.dequeueOutputBuffer(info, 10000);
        if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
          synchronized (this) {
            videoTrack = muxer.addTrack(video.getOutputFormat());
            startMuxer();
          }
        } else if (index >= 0) {
          if (info.size > 0 && (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) {
            if (videoStart < 0) videoStart = info.presentationTimeUs;
            packet(
                videoTrack,
                video.getOutputBuffer(index),
                info,
                Math.max(0, info.presentationTimeUs - videoStart));
          }
          video.releaseOutputBuffer(index, false);
          if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) break;
        }
        if (stoppedAt != 0 && System.nanoTime() - stoppedAt > 3_000_000_000L)
          throw new IllegalStateException("Video encoder did not finish.");
      }
    } catch (Exception e) {
      fail(e.getMessage());
    }
  }

  private void encodeAudio() {
    try {
      MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
      long samples = 0;
      boolean eos = false;
      long stopAt = 0;
      while (true) {
        byte[] chunk = pcm.poll();
        if (chunk != null || stopping && !eos) {
          int index = audio.dequeueInputBuffer(10000);
          if (index >= 0) {
            ByteBuffer input = audio.getInputBuffer(index);
            input.clear();
            if (chunk != null) {
              input.put(chunk);
              audio.queueInputBuffer(index, 0, chunk.length, samples * 1_000_000 / 16000, 0);
              samples += chunk.length / 2;
            } else {
              audio.queueInputBuffer(
                  index, 0, 0, samples * 1_000_000 / 16000, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
              eos = true;
              stopAt = System.nanoTime();
            }
          } else if (chunk != null) throw new IllegalStateException("Audio encoder input stalled.");
        }
        int out = audio.dequeueOutputBuffer(info, 10000);
        if (out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
          synchronized (this) {
            audioTrack = muxer.addTrack(audio.getOutputFormat());
            startMuxer();
          }
        } else if (out >= 0) {
          if (info.size > 0 && (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0)
            packet(audioTrack, audio.getOutputBuffer(out), info, info.presentationTimeUs);
          audio.releaseOutputBuffer(out, false);
          if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) break;
        }
        if (eos && System.nanoTime() - stopAt > 3_000_000_000L)
          throw new IllegalStateException("Audio encoder did not finish.");
      }
    } catch (Exception e) {
      fail(e.getMessage());
    }
  }

  private synchronized void startMuxer() {
    if (!muxing && videoTrack >= 0 && (!withAudio || audioTrack >= 0)) {
      muxer.start();
      muxing = true;
      for (Packet p : pending) write(p);
      pending.clear();
      pendingBytes = 0;
    }
  }

  private synchronized void packet(
      int track, ByteBuffer buffer, MediaCodec.BufferInfo info, long pts) {
    if (track < 0) return;
    byte[] bytes = new byte[info.size];
    buffer.position(info.offset);
    buffer.limit(info.offset + info.size);
    buffer.get(bytes);
    Packet packet = new Packet(track, bytes, pts, info.flags);
    if (muxing) write(packet);
    else {
      pendingBytes += bytes.length;
      if (pendingBytes > 16_000_000)
        throw new IllegalStateException("Audio/video format synchronization failed.");
      pending.add(packet);
    }
  }

  private void write(Packet p) {
    MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
    info.set(0, p.bytes.length, p.pts, p.flags);
    muxer.writeSampleData(p.track, ByteBuffer.wrap(p.bytes), info);
  }

  private synchronized void fail(String message) {
    if (!failed) {
      failed = true;
      stopping = true;
      result.error("Recording: " + message);
    }
  }

  @Override
  public void close() {
    if (!closed.compareAndSet(false, true)) return;
    stopping = true;
    if (surface != null) source.encoderSurface(surface, false);
    try {
      if (videoThread != null) videoThread.join(4000);
      if (audioThread != null) audioThread.join(4000);
    } catch (InterruptedException e) {
      Thread.currentThread().interrupt();
      failed = true;
    }
    boolean valid = muxing && !failed;
    try {
      if (muxing) muxer.stop();
    } catch (Exception e) {
      valid = false;
    }
    dispose();
    if (valid && file.length() > 1000) result.complete(file);
    else {
      file.delete();
      if (!failed) result.error("The recording was too short or the source disconnected.");
    }
  }

  private void dispose() {
    if (video != null) {
      try {
        video.stop();
      } catch (Exception ignored) {
      }
      video.release();
      video = null;
    }
    if (audio != null) {
      try {
        audio.stop();
      } catch (Exception ignored) {
      }
      audio.release();
      audio = null;
    }
    if (surface != null) {
      surface.release();
      surface = null;
    }
    if (muxer != null) {
      muxer.release();
      muxer = null;
    }
  }
}
