# Android parity and validation

This is a foreground-only preview, not a claim that every Windows feature is complete. Current automated coverage runs on an API 35 x86_64 emulator at phone portrait, phone landscape and tablet sizes with networking disabled; an emulator cannot validate USB/OTG hardware or sustained device performance.

| Area | Implemented | Verification / limitation |
| --- | --- | --- |
| Local media | Media3 hardware playback, seek, fit/crop, independent volumes, queue, resume | Actual decoded frames and saved resume tested offline. Android-supported codecs only; arbitrary MKV codecs are not guaranteed. |
| Gestures / fullscreen | Visible normal-mode startup and source selection; optional immersive video, long press show, tap hide, two-finger browser toggle, draggable controls | Injected native touchscreen sequences tested. Phone/tablet window bounds, keyboard insets, maximize/restore and live rotation have automated coverage. Physical-device ergonomics still need review. |
| USB video | Device selection/auto-preview, saved device/mode/FPS, surface rendering, hotplug callbacks | Compiles against pinned UVC library. **Physical Tab S8+ connection/reconnection/modes not verified.** |
| Source audio | Explicit USB input, routing rechecked per PCM buffer, monitor/volume, local audio-track selection | No-input microphone fallback rejection and selected file-track decoding tested. USB route, sync and quality need tablet validation. USB audio is 16 kHz mono in this preview. |
| Record / frames | Source-only hardware AVC/AAC capture, local segment export, source frame save/copy | Synthetic EGL + PCM recording and local video/audio export tested. USB capture/long recordings and selected-track export need hardware tests. Image clipboard support varies by Android target app. |
| Text | Bundled Latin OCR, copy/edit transcript | Actual OCR works offline. Other OCR scripts/region selection not ported. |
| Subtitle reading | Media3 selected embedded text cues; bitmap/burned-in OCR fallback; Android offline TTS | Embedded MKV text cue delivery tested. Other MKV subtitle variants and offline Samsung voices need tablet tests. Speech voice must already be installed. |
| Speech recognition | Bundled multilingual Whisper small q5_1; bounded mic/source capture; cancellation | Actual native inference on known speech works offline. Live tablet mic accuracy/performance not measured. No cloud fallback. |
| Floating browser | In-app WebView, saved tabs/cookies, ChatGPT default, minimize/maximize/restore, opacity, collapsible navigation | Restoration and exact-origin mic grant/deny tested with intercepted HTTPS fixtures offline. Actual online ChatGPT/login/voice flow not verified. |
| Offline | Local playback, OCR, native Whisper, capture/recording paths have no service dependency | OCR, decoding, inference and synthetic recording tested with no active network. USB path remains hardware-dependent. Offline TTS requires an installed offline voice. |
| State | Private SharedPreferences/JSON, bounded queue/history, URI permissions, settings/tabs | Resume/recreation tested. Providers with unknown metadata only permit best-effort file-change detection. |

## Remaining work before production / full parity

- Verify the Tab S8+ USB card: permissions, all modes, real source audio, disconnect/reconnect while previewing/recording, and capture stability. Measure frame rate, A/V sync, startup, heat, storage failures and sustained recording. Do not infer 4K60 support from an HDMI passthrough label.
- Implement foreground-service ownership and notifications for background/locked-screen recording. Current recording stops when hidden; unfinished local exports cancel when the activity is destroyed. Android may terminate gallery writes if the process is killed.
- Add a tablet/phone Camera2 backend if built-in camera capture is required. Current preview is USB capture plus local files.
- Complete missing-file recovery, saved-export retry UI, and selected-audio-track guarantees during local export.
- Port desktop-specific region OCR and decide whether Android system-wide overlay permission is actually needed. The browser currently floats only inside VideoCapture. CEC control is not implemented.
- Validate Android WebView service/login restrictions and actual browser microphone input on the tablet.
- Complete localization, screen-reader labels/touch-target review, physical phone/small-window validation, release signing, dependency/license distribution review, and production device coverage. MainActivity still owns several UI workflows; split feature controllers before extending it substantially.
- All packaged ARM64 native libraries have 16 KB ELF segment alignment. Runtime validation on 16 KB page-size devices remains required before general distribution; this preview targets the existing Tab S8+ hardware.
