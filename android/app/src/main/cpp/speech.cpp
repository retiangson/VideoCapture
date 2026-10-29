#include <algorithm>
#include <atomic>
#include <jni.h>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <whisper.h>
static std::mutex gate;
static std::atomic<uint64_t> cancelEpoch{0};
static whisper_context *context = nullptr;
extern "C" JNIEXPORT void JNICALL
Java_com_retiangson_videocapture_OfflineSpeech_cancelNative(JNIEnv *, jclass) {
  cancelEpoch++;
}
extern "C" JNIEXPORT jstring JNICALL
Java_com_retiangson_videocapture_OfflineSpeech_transcribeNative(
    JNIEnv *env, jclass, jstring path, jshortArray samples, jstring language,
    jlong expectedEpoch) {
  std::lock_guard<std::mutex> lock(gate);
  uint64_t expected = static_cast<uint64_t>(expectedEpoch);
  if (cancelEpoch.load() != expected) {
    env->ThrowNew(env->FindClass("java/util/concurrent/CancellationException"),
                  "Transcription cancelled.");
    return nullptr;
  }
  const char *file = env->GetStringUTFChars(path, nullptr);
  if (!context) {
    auto options = whisper_context_default_params();
    options.use_gpu = false;
    context = whisper_init_from_file_with_params(file, options);
  }
  env->ReleaseStringUTFChars(path, file);
  if (!context) {
    env->ThrowNew(env->FindClass("java/lang/IllegalStateException"),
                  "Offline speech model could not load.");
    return nullptr;
  }
  jsize count = env->GetArrayLength(samples);
  jshort *raw = env->GetShortArrayElements(samples, nullptr);
  std::vector<float> audio(count);
  for (jsize i = 0; i < count; i++)
    audio[i] = raw[i] / 32768.0f;
  env->ReleaseShortArrayElements(samples, raw, JNI_ABORT);
  const char *lang = env->GetStringUTFChars(language, nullptr);
  std::string langCopy(lang);
  env->ReleaseStringUTFChars(language, lang);
  auto params = whisper_full_default_params(WHISPER_SAMPLING_BEAM_SEARCH);
  params.language = langCopy.c_str();
  params.n_threads =
      std::min(4u, std::max(1u, std::thread::hardware_concurrency()));
  params.translate = false;
  params.no_context = true;
  params.print_progress = false;
  params.print_realtime = false;
  params.print_timestamps = false;
  params.print_special = false;
  params.abort_callback = [](void *data) {
    return cancelEpoch.load() != *static_cast<uint64_t *>(data);
  };
  params.abort_callback_user_data = &expected;
  if (whisper_full(context, params, audio.data(), count) != 0) {
    if (cancelEpoch.load() != expected) {
      env->ThrowNew(
          env->FindClass("java/util/concurrent/CancellationException"),
          "Transcription cancelled.");
    } else {
      env->ThrowNew(env->FindClass("java/lang/IllegalStateException"),
                    "Offline transcription failed.");
    }
    return nullptr;
  }
  std::string result;
  for (int i = 0; i < whisper_full_n_segments(context); i++) {
    if (!result.empty())
      result += "\n";
    result += whisper_full_get_segment_text(context, i);
  }
  jbyteArray bytes = env->NewByteArray(result.size());
  env->SetByteArrayRegion(bytes, 0, result.size(),
                          reinterpret_cast<const jbyte *>(result.data()));
  jclass stringClass = env->FindClass("java/lang/String");
  jmethodID constructor =
      env->GetMethodID(stringClass, "<init>", "([BLjava/lang/String;)V");
  return static_cast<jstring>(env->NewObject(stringClass, constructor, bytes,
                                             env->NewStringUTF("UTF-8")));
}
extern "C" JNIEXPORT void JNICALL
Java_com_retiangson_videocapture_OfflineSpeech_releaseNative(JNIEnv *, jclass) {
  std::lock_guard<std::mutex> lock(gate);
  if (context) {
    whisper_free(context);
    context = nullptr;
  }
}

extern "C" JNIEXPORT jlong JNICALL
Java_com_retiangson_videocapture_OfflineSpeech_currentEpochNative(JNIEnv *,
                                                                  jclass) {
  return static_cast<jlong>(cancelEpoch.load());
}
