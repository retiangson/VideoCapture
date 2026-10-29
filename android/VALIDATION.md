# Android preview validation

Validated 30 October 2026 (Pacific/Auckland), version 0.2.0. Isolated API 35 x86_64 emulator; no physical phone/tablet connected. Airplane mode and Wi-Fi disabled for offline tests. Core tests assert no active network using `requireOffline=true`.

- `assembleDebug`, `assembleDebugAndroidTest`, `assemblePreview`: successful. ARM64 and x86_64 native libraries compiled; release-optimized preview is locally debug-signed.
- `lintDebug`: zero errors, 13 warnings (English-only strings, custom touch accessibility and intentional JavaScript among them). This does not establish production acceptance.
- Full phone-portrait suite: **14 tests passed**, 68.099 seconds. This covers decoding/resume, touchscreen gestures, saved browser tabs, exact-origin browser microphone policy, MKV text cues, bundled OCR/Whisper, selected audio-track extraction, local clip export, source-only EGL/PCM recording, USB microphone-fallback rejection, and responsive layouts.
- Three layout tests repeated at phone portrait (1080×2400, density 420), phone landscape (2400×1080, density 420), a narrow 320 dp phone (1080×2400, density 540), and the emulator's native tablet size. They check window bounds, usable address/page areas, reachable controls, maximize/restore, real keyboard insets and rotation with the browser/transcript open. Final tab/spinner touch targets and primary playback/recording control bounds are included in repeated checks. The narrow-phone run also repeats actual local clip export after the recording-icon change.
- Rotation initially recreated the activity and lost the active browser. The app now handles the complete set of size-based configuration changes and reflows the current views. The regression test asserts the same browser instance and visible transcript survive both orientations.
- Manual emulator layout review covers the phone player and floating browser. The minified APK is installed and launched separately from instrumentation. APK signature and 16 KB ZIP alignment are verified when packaging.
- Prior 0.1.0 native optimized speech verification transcribed the known offline video fixture: “Quick brown fox jumps over the lazy dog. Please copy this video transcript.” The native speech implementation is unchanged in 0.2.0, and core offline inference still passes instrumentation.

Headless emulator audio hardware is unavailable: browser tests assert an actual audio-only permission grant and rejection of untrusted origins, while accepting Android's `NotReadableError` after a grant. This does not prove physical browser microphone capture. Fixtures are synthetic; no real account login was used.

Physical USB/OTG capture, microphone accuracy, USB audio routing/sync, hotplug recovery, codec coverage, offline TTS voices, sustained recordings, thermal behavior and real-device performance remain unverified. Background/locked-screen recording and built-in Camera2 capture remain unimplemented. See [PARITY.md](PARITY.md).
