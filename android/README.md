# VideoCapture for Android (preview)

Native Java/Android Views app with Media3 hardware playback, UVC USB capture, an in-app Chromium WebView browser, bundled OCR, and local whisper.cpp transcription. This is an independent Android target; Windows remains in `src/VideoCapture`.

**This preview does not yet have full Windows feature parity.** See [PARITY.md](PARITY.md) for implemented behavior, test evidence, and remaining work. The same APK supports ARM64 phones and tablets running Android 10 or later. The interface adapts to portrait, landscape, keyboard and smaller windows; physical USB functionality and performance still need validation on the Samsung Galaxy Tab S8+ and other target devices.

## Build

Requirements: JDK 17+ (tested with Android Studio JBR 21), Android SDK platform 36, Build Tools 36.0.0 or compatible AGP-selected version, NDK `27.2.12479018`, CMake `3.22.1`. Minimum Android 10/API 29. Target API 36. ABIs: ARM64 (phones/tablets) and x86_64 (emulator). Gradle 8.14.3 and AGP 8.13.2 are pinned; the wrapper distribution has a SHA-256 checksum.

From the repository root in PowerShell:

```powershell
./tools/Setup-Android.ps1
# Set ANDROID_HOME to your Android SDK, or create android/local.properties with sdk.dir.
$env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
./android/gradlew.bat -p android assemblePreview lintDebug
```

Setup downloads and verifies the pinned Whisper source and the same pinned 190,085,487-byte speech model used by Windows. Source/model are ignored by Git. Building needs internet for these assets and Gradle/Maven dependencies on the first build. The installed app does not download its OCR or transcription model.

Installable output: `android/app/build/outputs/apk/preview/app-preview.apk`.
The minified/shrunk preview uses the local **debug signing key**, package `com.retiangson.videocapture.preview`, label **VideoCapture Preview**. It is a sideload preview, not a production release signing setup. Keep the same signing key for updates; different developer machines normally have different debug keys. `assembleRelease` creates an unsigned release until private release signing is configured. Do not commit signing keys or passwords.

The APK contains both ABIs and the offline model (~218 MiB). First transcription copies the model into private storage (~181 MiB extra) and loads it on demand; playback startup does not initialize Whisper, OCR, TTS, or the browser. Small transcription models trade accuracy for speed; this preview uses multilingual Whisper small q5_1 and up to four CPU threads. Actual transcription speed depends on the device.

## Active options

Toggle buttons have an accent background/border and a small marker when on, plus an accessible On/Off state. Off uses the neutral background; unavailable buttons are dimmed. Fullscreen, picture mode, subtitle reading, timeline, browser visibility, audio transcript visibility, playback/recording, browser navigation/maximize and active tabs reflect actual state. Saved options restore their highlights; hiding a browser from its own controls also updates the player button. Picture mode defaults to Regular. Existing Fill preferences migrate to the new three-mode selector; the current mode is saved and retained when entering/leaving fullscreen.

## Phone layouts

Version 0.2.3 keeps playback/recording controls in a dedicated row and uses horizontally scrollable rows for secondary actions. Buttons have 48 dp touch targets; short windows get a vertically scrollable player panel capped at 75% of available height. The browser fits within the actual window, clamps dragging to its bounds, and reflows when rotated or when the keyboard opens. Its address field has compact navigation buttons. Browser tools/transcription scroll independently so they remain reachable without displacing the entire web page. Maximize/restore uses the current window and system/keyboard insets.

This improves layouts; it does not add background recording or built-in camera capture. Real phones still need device/USB/audio validation. USB capture requires OTG/USB host support and sufficient power for the card.

## Use

**Copy frame** first saves a complete PNG to **Pictures/VideoCapture**, publishes the Gallery image, then copies its MediaStore URI to the clipboard. Captures remain in Gallery until you delete them. This replaces the private-cache image reference that could paste as a broken image. Compatible browser editors can paste the image normally. The floating browser also supports the page's **Attach / Upload → Copied frame** chooser, plus **Choose files** for other images/files. PNG bytes were verified through both a native browser paste event and a real file input in offline tests; the actual Samsung/ChatGPT session still needs a device check. Uploads require an explicit selection and are cancelled when their tab navigates, closes or changes. Windows image copying is unchanged.

The 0.3.1 player uses charcoal surfaces, green active indicators, vector transport/action icons and a larger central play/pause button. Fullscreen and rotation-lock icons are adjacent in the menu header; readable labels remain beside action icons, and icon-only header buttons have accessible names/tooltips. Progress, video volume, reading volume and panel opacity have direct tap-to-set controls; numeric levels stay synchronized with saved settings and mouse-wheel opacity changes. **Lock rotation** keeps the current orientation while VideoCapture is open, remembers its enabled state, and locks the current display orientation again on restart. Turn it off to return control to Android's rotation setting. Multi-window/device policies can override application orientation requests.

The app starts in normal mode with visible player controls and **Select USB** / **Open video** source buttons. No gesture is needed to find the device picker. Select USB to list capture devices; an empty list produces a visible dialog. A selected card still opens automatically after permission. Close other camera apps before using the capture card here.

Choose the **Fullscreen** icon for immersive playback. Use the restore icon or Back to exit. Live/local source captions are hidden in fullscreen; error and action feedback remains visible. That hides the player menu; **Exit fullscreen** or Back restores normal mode and its visible controls. One tap on the video hides the menu in normal and fullscreen modes; double-tap restores it at its default position.

- The menu is visible on startup. **One tap on the video hides it; double-tap restores the default menu. Hold the video for 450 ms to show/hide the browser.** Two-finger quick tap also toggles the browser. Gestures on browser content retain normal browser behavior.
- Swipe **up/down in the left third** of the video to raise/lower video volume, **center third** for reading volume, and **right third** for screen brightness. The zone is selected where the swipe begins. Horizontal drags and multi-finger motion do not change levels. A brief overlay shows the current level. Brightness has a 5% minimum and changes only this app window; **Brightness → Use system** restores Android brightness. No system-settings permission is requested.
- **Open** selects local media through Android's file picker; **Playlist** manages saved entries. The app remembers position, volume, opacity, tabs, and USB choices. File positions checkpoint every five seconds and on pause. Cloud document providers may need their own network connection; copy media locally for offline use.
- **USB** selects an attached capture card and immediately opens it after Android grants USB access. **Mode / FPS** show device-advertised modes; the 4K label alone does not establish capture output capability. **Audio input** must explicitly select the capture card's audio input. It never falls back to the room microphone for video audio.
- **Picture: Regular / Fill / Stretch** opens a three-choice selector. Regular (default) shows the whole picture with correct proportions. Fill crops edges to cover the display. Stretch fills the display by changing proportions. The same mode applies to local files and USB preview; recordings keep the original source.
- **Copy frame** saves to `Pictures/VideoCapture` before copying its Gallery image URI. **Save frame** saves without changing the clipboard. Capture waits for a rendered picture and rejects transparent readbacks; a source-only PixelCopy fallback is attempted before reporting an error. An unavailable picture does not create a Gallery item or replace the clipboard. Android paste targets must support images. **Text** OCR copies plain text. All capture tools capture the source video only, excluding app controls and browser.
- **Record** records USB source video with selected USB audio, or marks and exports a segment of a local file. Recordings save to `Movies/VideoCapture`. Export compatibility depends on Android codecs. Recording ends when the app leaves the foreground; keep the app open until saving finishes. Destroying the activity cancels an unfinished local clip export.
- **Read subtitles** uses selected embedded text cues first (including supported MKV text tracks); bitmap cues and burned-in subtitles use OCR. Select a subtitle track if necessary. Offline speech requires an installed offline Android TTS voice. Video and reading volumes are independent.
- **Audio to text** toggles the transcript panel. Select **Video audio** or **Microphone**, start capture, then stop and transcribe. Captures are limited to two minutes. Video audio uses the selected local audio track or the explicitly selected USB input. Mic capture mutes local playback/USB monitoring to avoid echo. Transcript text is editable/copyable; it is not uploaded or persisted to disk by the app.
- The browser defaults to ChatGPT, saves tabs/selection and cookies, supports double-tap title to maximize/restore, and hides its address/navigation row until requested. Drag the title to move it. Opacity affects the whole panel. Mouse-wheel gestures over browser chrome or outside the page adjust opacity; page scrolling remains normal.
- Browser microphone permission is restricted to the exact HTTPS ChatGPT origins and requires Android mic permission. Camera, arbitrary-origin mic access, downloads and file/content URL access are disabled. Embedded WebView login/service restrictions may apply. Browser usage requires internet; local playback, source capture, OCR and Whisper do not. Do not run app mic capture and browser mic capture simultaneously.

Private settings/history and WebView cookies stay in app storage; uninstall/clear app data removes them. Backups are disabled. The app does not request all-files access. A failed gallery write retains the private export rather than deleting the only copy; retry/recovery UI is not yet implemented.

## Validation

Build both the app and instrumentation APK, install on a disposable emulator, enable airplane mode and disable Wi-Fi, then run:

```powershell
./android/gradlew.bat -p android assembleDebug assembleDebugAndroidTest lintDebug
adb install -r android/app/build/outputs/apk/debug/app-debug.apk
adb install -r android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk
adb shell am instrument -w -r -e requireOffline true com.retiangson.videocapture.test/androidx.test.runner.AndroidJUnitRunner
```

Use `adb -s SERIAL` with multiple devices. Instrumentation clears the **debug package's** preferences and uses synthetic media; run it on a disposable test profile. Require the final `OK (19 tests)`; `adb shell am instrument` may exit with code zero even when tests fail. Tests exercise native touchscreen gestures, real decoding/resume, browser restoration and exact-origin mic grant/deny, offline OCR/Whisper, selected audio extraction, embedded MKV text cues, local clip export, source-only EGL surface recording with PCM, rejection of microphone fallback, window bounds, keyboard insets, maximize/restore and live rotation. A headless emulator can grant browser mic permission yet report no readable microphone; physical browser mic capture remains a tablet check.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies and source/license links.
