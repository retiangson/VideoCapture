# Android preview validation

Validated 2026-10-29 UTC. Target: isolated API 35 x86_64 Pixel Tablet emulator; no physical tablet connected. Wi-Fi disabled, airplane mode enabled, and `requireOffline=true` asserted no active network before every test.

- `assembleDebug`, `assembleDebugAndroidTest`, `assemblePreview`: successful; ARM64 and x86_64 native libraries compiled.
- `lintDebug`: zero errors, 21 warnings (including English-only UI strings, custom touch accessibility and intentional JavaScript). Warnings are not treated as production acceptance.
- AndroidJUnitRunner: **11 tests passed**, final run 58.194 seconds.
- Tested: actual decoded local playback/resume; long press, tap and two-finger touch gestures; saved browser tabs/selection after activity recreation; exact-origin WebView audio permission grant/deny; embedded MKV text cues; bundled Latin OCR; native Whisper on a known speech fixture; selected audio-track extraction; local clip video/audio export; synthetic EGL + PCM source-only recording; rejected USB-to-microphone fallback.
- Minified preview installed and launched successfully. Actual local source-audio transcription completed offline with: “Quick brown fox jumps over the lazy dog. Please copy this video transcript.” This checks the release-optimized JNI path as well as debug instrumentation.
- Manual emulator layout review: player menu, floating browser, collapsible navigation and transcript panel.
- Preview APK signature verification passed; 16 KB ZIP alignment passed. Every packaged ARM64 native library's ELF LOAD segment alignment is 16 KB. No runtime test on a 16 KB Android device was performed.

Headless emulator audio hardware is unavailable: the browser permission test asserts an actual audio-only grant and rejects untrusted origins, while allowing Android's `NotReadableError` after grant. This is not evidence of successful physical browser microphone capture. Test fixtures contain synthetic media only. No real account login was used.

USB/OTG capture, microphone accuracy, USB audio routing/sync, hotplug recovery, hardware codec coverage, offline TTS voices, long recordings, thermal behavior and tablet performance remain unverified. See [PARITY.md](PARITY.md). The preview APK is locally debug-signed and not a production release.
