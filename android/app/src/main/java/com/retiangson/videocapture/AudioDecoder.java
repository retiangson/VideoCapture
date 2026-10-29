package com.retiangson.videocapture;

import android.content.Context;
import android.media.*;
import android.net.Uri;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.atomic.AtomicBoolean;

final class AudioDecoder {
  static short[] range(
      Context context, Uri uri, long startMs, long endMs, AtomicBoolean cancelled, int audioOrdinal)
      throws Exception {
    MediaExtractor extractor = new MediaExtractor();
    MediaCodec decoder = null;
    AudioInput.Samples result = new AudioInput.Samples();
    try {
      extractor.setDataSource(context, uri, null);
      int track = -1;
      int audioIndex = 0;
      MediaFormat format = null;
      for (int i = 0; i < extractor.getTrackCount(); i++) {
        MediaFormat candidate = extractor.getTrackFormat(i);
        if (candidate.getString(MediaFormat.KEY_MIME).startsWith("audio/")
            && audioIndex++ == audioOrdinal) {
          track = i;
          format = candidate;
          break;
        }
      }
      if (track < 0) throw new IllegalStateException("This video has no decodable audio track.");
      extractor.selectTrack(track);
      extractor.seekTo(startMs * 1000, MediaExtractor.SEEK_TO_PREVIOUS_SYNC);
      format.setInteger(MediaFormat.KEY_PCM_ENCODING, AudioFormat.ENCODING_PCM_16BIT);
      decoder = MediaCodec.createDecoderByType(format.getString(MediaFormat.KEY_MIME));
      decoder.configure(format, null, null, 0);
      decoder.start();
      boolean inputEnded = false, outputEnded = false;
      int rate = format.getInteger(MediaFormat.KEY_SAMPLE_RATE),
          channels = format.getInteger(MediaFormat.KEY_CHANNEL_COUNT);
      long endUs = Math.min(endMs, startMs + 120000) * 1000;
      double next = 0;
      MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
      long deadline = System.currentTimeMillis() + 60000;
      while (!cancelled.get() && System.currentTimeMillis() < deadline) {
        if (!inputEnded) {
          int index = decoder.dequeueInputBuffer(10000);
          if (index >= 0) {
            ByteBuffer buffer = decoder.getInputBuffer(index);
            int count = extractor.readSampleData(buffer, 0);
            long time = extractor.getSampleTime();
            if (count < 0 || time > endUs) {
              decoder.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
              inputEnded = true;
            } else {
              decoder.queueInputBuffer(index, 0, count, time, 0);
              extractor.advance();
            }
          }
        }
        int out = decoder.dequeueOutputBuffer(info, 10000);
        if (out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
          MediaFormat actual = decoder.getOutputFormat();
          rate = actual.getInteger(MediaFormat.KEY_SAMPLE_RATE);
          channels = actual.getInteger(MediaFormat.KEY_CHANNEL_COUNT);
          if (actual.containsKey(MediaFormat.KEY_PCM_ENCODING)
              && actual.getInteger(MediaFormat.KEY_PCM_ENCODING) != AudioFormat.ENCODING_PCM_16BIT)
            throw new IllegalStateException("The decoder did not provide PCM16 audio.");
        } else if (out >= 0) {
          ByteBuffer data = decoder.getOutputBuffer(out).order(ByteOrder.LITTLE_ENDIAN);
          data.position(info.offset);
          data.limit(info.offset + info.size);
          int frames = info.size / (2 * channels);
          short[] converted = new short[Math.max(1, (int) (frames * 16000L / rate) + 2)];
          int used = 0;
          for (int f = 0; f < frames; f++) {
            int sum = 0;
            for (int c = 0; c < channels; c++) sum += data.getShort();
            long time = info.presentationTimeUs + f * 1_000_000L / rate;
            if (time < startMs * 1000 || time > endUs) continue;
            next += 16000.0 / rate;
            while (next >= 1) {
              converted[used++] = (short) (sum / channels);
              next -= 1;
            }
          }
          if (used > 0) result.add(converted, used);
          decoder.releaseOutputBuffer(out, false);
          if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
            outputEnded = true;
            break;
          }
        }
      }
      if (cancelled.get()) throw new java.util.concurrent.CancellationException();
      if (!outputEnded)
        throw new java.util.concurrent.TimeoutException(
            "Audio decoding exceeded 60 seconds; no partial transcript was produced.");
      return result.array();
    } finally {
      if (decoder != null) {
        try {
          decoder.stop();
        } catch (Exception ignored) {
        }
        decoder.release();
      }
      extractor.release();
    }
  }
}
