# VideoCapture

A native x64 Windows application for watching and recording a Roku/Chromecast through a USB HDMI capture device, and playing local movie files. Built with .NET 10 WPF and LibVLC. Optional text recognition runs locally; the floating browser uses Microsoft Edge WebView2 (Chromium).

The player uses a shared dark theme, icon buttons with tooltips/accessibility names, a dedicated transport bar, and separate **Capture** and **Playlist** tabs. The native caption also uses the dark palette on supported Windows 11 versions. Controls use lightweight WPF templates without blur effects or animated layout transitions.

## Android preview

The native Android port is in [android/](android/README.md), with an installable preview build, offline OCR/Whisper, local playback, USB capture integration and touch gestures. It is not yet full Windows parity; see the [Android validation and remaining work](android/PARITY.md). Windows build/install instructions below remain unchanged.

## Active option indicators

Enabled toggles use an accent fill/border and a small status dot; inactive toggles use the neutral surface. Unavailable controls remain dimmed. Highlights follow actual state, including keyboard shortcuts, saved preferences and changes from another panel. Windows covers fullscreen/fill, subtitle reading, timeline, browser visibility, playback, recording and browser transcription controls; Android also highlights the active browser tab and navigation row. **Fill screen** is highlighted when cropping to fill is on; off shows the whole frame.

## Playlists and player controls

**Open videos** accepts multiple files and plays the first. **Playlist → Add videos** queues files without interrupting current playback; dropping multiple files adds them and starts the first. Double-click a row or select it and press **Play selected** / Enter. The central Play button can also start the selected item after Stop or reopening. Previous/next buttons are available in both the transport bar and fullscreen menu. Reaching the end advances in queue order, skipping missing files. The final item stops rather than looping.

Use the up/down buttons to reorder, the trash button or Delete to remove a selected row, and **Clear** to empty the queue. Removing the currently playing item or clearing an active local playlist stops that local playback. These actions never delete video files. Duplicate paths are ignored, and the queue supports up to 200 entries. The ordered playlist and selection are saved with preferences; reopening restores the queue without automatically playing a movie. Saved live capture still resumes as configured.

Volume and opacity sliders jump directly to the clicked point and support dragging from that point. **Menu opacity** in Capture settings or the fullscreen menu controls the opacity of the entire floating menu, including its text, icons, and sliders (35–100%); the value is remembered. **Browser opacity** remains independent. The fullscreen menu starts hidden, opens with a held left-click, hides with a quick left-click, and appears near the bottom center; drag its grip to reposition it. Its Playlist button returns to the windowed playlist manager.

Player interaction checks: `dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-player artifacts/sample.mp4 artifacts/player-smoke`. This verifies queue ordering/deduplication, retained playlists, previous/next, automatic advance past a missing entry, click-position slider values, and full-menu opacity synchronization.

The fullscreen menu includes a click/drag seek bar with elapsed and total time.
**Timeline** hides/shows that bar and remembers the choice. Seeking is available
for seekable local videos; HDMI capture shows **LIVE**. The menu still starts
hidden and opens with a held left-click.

**Video** and **Reading** volume sliders are always visible in the windowed
player and in the floating fullscreen menu. Each volume is saved independently.
Reading volume adjusts the Windows voice without restarting its current line;
the Speech tab offers the same synchronized reading-volume setting.

## Resume local videos

Opening a previously played file automatically resumes its saved position once
the player is ready to seek. History loads without autoplaying a movie. Playback
positions are checkpointed every five seconds and saved on pause, stop, file or
capture-source switches, and normal app exit. An abrupt shutdown can lose the
last few seconds since the latest completed checkpoint.

History is a local, atomically replaced JSON file at
`%LOCALAPPDATA%/VideoCapture/playback-history.json`; no database server or
internet connection is used. It retains up to 1,000 recent file entries within
a 4 MB storage limit, including files removed from the playlist. File paths
match without regard to case; size and modification time must also match so
modified/replaced files normally start fresh. Moving or renaming a file gives
it a new history entry. Live capture is excluded. Finished videos, including
the final 2% (at most ten seconds), restart at the beginning. Non-seekable files
continue at their current position if resume is unavailable.

To restart a video manually, seek to the beginning. To clear all resume history,
close the app and delete `playback-history.json`. Corrupt or oversized history
falls back to an empty history with a diagnostic and does not prevent playback.

Resume/control checks: `dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-resume artifacts/sample.mp4 artifacts/resume-smoke`.

## Run

Requires Windows 10 version 2004 or later / Windows 11 x64 and the .NET 10 Desktop Runtime (the SDK includes it). OCR requires the Visual C++ 2022 x64 runtime; the floating browser requires Microsoft Edge WebView2 Runtime. Capture/playback does not depend on the browser being initialized.

Startup renders the window before discovering devices and does not initialize native playback until a source is selected. Playback-engine construction runs on a worker thread. OCR and the browser remain on demand. Publishing runs the headless `--prepare-runtime` maintenance command to generate VLC's `libvlc/win-x64/plugins/plugins.dat` index; distribute that index with the unchanged native runtime. VLC validates cached plugin metadata and retains scanning fallback. A missing index is generated when playback is first initialized if the application directory is writable.

Startup profiling: `dotnet run --project tests/VideoCapture.Smoke -c Release -- --profile-startup`. The local window test improved from roughly 22 seconds to roughly 0.85 seconds for first content rendering, and cached engine creation measured about 60 ms. These measurements exclude process/CLR launch time and vary with disk cache, antivirus, and device drivers. The startup check also verifies that idle launch does not load the native VLC DLL. `--profile-playback artifacts/sample.mp4` measures engine construction and first decoded frames separately.

```powershell
dotnet run --project src/VideoCapture -c Release
```

Connect **Roku/Chromecast HDMI output → capture device HDMI input → PC USB**. Selecting a video device automatically starts its live preview; selecting its corresponding Audio device applies the audio change automatically. Recording starts only when you choose **Record live**. First launch waits for a device selection; later launches restore your saved video/audio devices and automatically preview the saved video device. Windows camera/microphone privacy settings must allow desktop apps to access the capture device. Close other apps that are using it.

Preferences save automatically after a short debounce and are flushed on normal close to `%LOCALAPPDATA%/VideoCapture/settings.json`. Saved options include video/audio devices, a resolution/FPS/pixel-format preference for each video device, GPU/CPU renderer, windowed picture fit, volume, whole-menu opacity, browser opacity, and the ordered playlist. All executable copies share this per-user configuration. An unplugged saved device stays selected and preview resumes after it reconnects. Unavailable saved modes fall back to driver-advertised choices without discarding the stored preference; unavailable saved audio falls back to video-only preview until it returns. Recording and fullscreen are never automatically resumed. Invalid settings use safe defaults; writes replace the complete JSON file atomically. To reset preferences, close all app instances and delete `settings.json`.

Persistence checks: `dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-preferences`. A real capture restart check is available as `--probe-preferences "USB3 Video" artifacts/sample.mp4`. Tests use isolated settings stores and do not overwrite the user's configuration.

Selecting a video device loads separate **Capture resolution**, **Frame rate**, and **Video format** dropdowns. Frame rates are filtered by resolution, and video formats are filtered by both resolution and frame rate. FPS choices include driver defaults, range endpoints, and standard rates within the driver's advertised interval range. The highest-resolution, fastest mode is selected initially, preferring MJPG when available. Changes apply automatically after a short debounce and briefly restart preview. Mode changes are disabled during recording. The app requests the selected size, rate, and pixel format explicitly. **Source** reports what the driver actually negotiated; **Preview** reports the approximate displayed rate. An advertised mode can still fail if USB bandwidth, the input signal, or another application prevents it from being used. Both 1080p30 and 1080p60 were verified on USB3 Video.

Device lists are refreshed every two seconds without opening cameras. If the selected capture device disappears, the app clears the stale picture, releases its capture session off the UI thread, retains the device/mode selection, and resumes preview after the same named device reappears. An eight-second frame-stall watchdog also recreates stalled live sessions. Recovery attempts are spaced by at least three seconds. Native teardown has a bounded UI wait; if the driver remains stuck releasing its graph, recovery waits rather than opening that hardware concurrently. A driver that never releases can still require a driver/device reset. Capture selection currently uses DirectShow friendly names, so two devices with identical names cannot be distinguished reliably.

The recovery integration probe uses real USB3 capture but simulates device-list removal/reappearance and stopped-frame delivery. It verifies automatic start/FPS apply, repeated graph recreation, the stall watchdog, manual stop, and movie isolation without saving captured images or recordings:

```powershell
dotnet run --project tests/VideoCapture.Smoke -c Release -- --probe-recovery "USB3 Video" artifacts/sample.mp4
```

This probe does not replace physically unplugging/replugging the USB cable to verify the driver's removal behavior.

**Stop** cancels automatic recovery until you select a device or change a capture setting. Opening a movie also cancels capture recovery; USB changes do not interrupt movie playback. Disconnecting during recording stops that recording and subsequently resumes preview only; inspect the interrupted file before relying on it. Recording is never silently resumed.

The connected **USB3 Video** card advertises capture up to **1920×1080 at 60 fps**. No 3840×2160 capture mode was reported. Native 1080p60 was verified on this device, including approximately 60 displayed fps with GPU preview. The app lists 4K modes automatically when a different device's driver exposes them. A 4K source or passthrough capability alone does not make a 4K USB capture mode available.

Recordings are saved to your Windows Videos folder under `VideoCapture`, as uniquely named MKV files with H.264 video and AAC audio. Choose **Stop recording**, **Stop**, or close the app to finalize the file. Starting/stopping recording restarts the input briefly. Recording uses the selected capture mode, with an 8 Mbps H.264 target. Select **No audio** for video-only capture. Verify the correct audio input before recording.

Choose **Open videos** or drop local movie files into the window to queue and play them. Playback supports pause, seeking, volume, and video-only fullscreen (`F11` or double-click the picture; `Esc` exits). Fullscreen hides the header, device panel, and playback controls and preserves the picture's aspect ratio. Playback and device changes are locked while recording. **Recordings folder** opens saved files in Explorer.

Fullscreen covers the complete physical bounds of the monitor containing the app, including the taskbar area, and defaults to **Fill screen**. It centers and crops the video to the monitor's shape without stretching it. The floating toolbar's **Show whole frame / Fill screen** button switches between preserving the entire picture (which may leave bars) and filling the display. Exiting fullscreen restores the previous window bounds, minimum size, and windowed picture-fit choice. Bars already encoded into a video picture are part of that source image; ordinary aspect-ratio fitting does not detect them.

UVC live capture uses square pixels and the selected capture raster's proportions. The tested USB3 Video driver advertised a 1920×1080 stream with incorrect 4:3 display-aspect metadata, causing pillarboxing and vertical stretching. Live capture corrects that declaration to 16:9 for 1920×1080. Local movie files retain their own pixel-aspect metadata. Legacy anamorphic analog capture may require a different pixel-aspect policy.

The fullscreen floating toolbar starts hidden. **Hold left-click on the video for 450 ms** to show it; a quick left-click hides it, or choose **Hide controls** on the toolbar. Scroll over the floating player menu to change its whole-panel opacity by 5 percentage points per notch (35–100%). Scrolling over the main app, including capture settings and video, adjusts the visible browser opacity (30–100%); if only the player menu is visible, it adjusts that menu instead. Browser-page scrolling is unchanged. It is draggable and provides recording, snapshots, text recognition, CEC, browser, picture-fit, and exit controls. It hides when leaving fullscreen and closes with the app. The main Record live button starts capture-device recording; it can also stop an active local clip started from the browser panel. **Copy frame** (also available outside fullscreen) places the current video picture on the Windows clipboard as an image and PNG; paste with `Ctrl+V`. It captures the video, excluding app controls. **Save frame** writes a PNG to a chosen location. **Copy recording** places the last completed recording on the clipboard as a file, for pasting into Explorer or apps accepting video files. Recordings are not automatically placed on the clipboard.

Shortcuts: `Ctrl+Shift+C` copies the video frame, `Ctrl+Shift+T` copies video text, `Ctrl+Shift+X` selects screen text, `Ctrl+Shift+B` hides/shows the browser, `Ctrl+Shift+R` starts/stops capture recording, and `Esc` exits fullscreen from the video or toolbar. Shortcuts work while the corresponding VideoCapture window has focus. Copying images or text does not stop an active recording.

**Copy video text (OCR)** recognizes English text from a native video snapshot and copies plain Unicode text for pasting. **Copy screen text…** takes a temporary desktop bitmap, lets you drag a rectangle over visible text, and copies recognized text from that rectangle. `Esc` cancels selection. The toolbar hides during screen selection and then restores its previous visibility; visible browser content remains available for selection. Images are processed locally in memory; video snapshots use a temporary PNG that is deleted afterward. No OCR service or account is used. Empty recognition or cancellation preserves the clipboard. The bundled Tesseract English model is in `tessdata`; OCR may misread small/blurred text and currently does not offer other languages.

**Browser** opens a draggable, resizable Chromium/WebView2 window at **https://chatgpt.com/**. Use **+** or `Ctrl+T` for a new ChatGPT tab, click a tab to switch, and use **×** or `Ctrl+W` to close it. `Ctrl+Tab` / `Ctrl+Shift+Tab` cycle tabs; `Ctrl+L` expands navigation and focuses the address bar. Closing the last tab opens a fresh ChatGPT tab. Each tab has its own page and navigation history; back/forward/reload and the address bar apply to the selected tab. Sign-in popups and links requesting a new window open as tabs and preserve their opener relationship.

In fullscreen, **right-click the video** to show/hide the browser independently
of the player menu. Clicks inside a browser page keep their normal browser
behavior. Double-clicking the video still enters/exits fullscreen.

The browser window includes **Copy frame**, **Record video**, **Copy video text**,
and **Panel opacity** together. Frame capture and OCR operate on the video
source and copy to the clipboard. They exclude the browser, player controls,
and other desktop windows. Panel opacity applies to the complete browser window,
including its action bar and page, and retains the existing saved opacity.
Hiding the browser preserves tabs and does not stop playback or recording.

**Record video** records HDMI capture using the existing video/audio pipeline.
For a local file, it marks the current playback position and saves the played
segment when recording stops, without restarting playback. Pauses add no frozen
frames to a local clip. Seeking, opening another source, and playlist changes
are disabled while marking a clip; pausing, copying frames/text, and hiding the
panel remain available. Stop, end-of-file, and normal app close finalize a pending
clip. A crash before finalization cannot save an unfinished local clip.

Local clips are exported to the Videos/VideoCapture folder as H.264/AAC MKV
files, with source audio when available. They exclude browser content, desktop
audio, and synthesized subtitle speech. Export is local and runs in a hidden
FFmpeg process; long/high-resolution clips can take time to save. Playback,
seeking, browser interaction and the other controls remain available while a
clip saves; another local recording waits for that export to finish. Pending output
uses a temporary filename and becomes the final clip only on success. Clips
require the original source file to remain available and unchanged. Local-file
subtitles are not burned into the exported clip; HDMI captions already present
in the captured image remain part of that source.

Browser action/click checks (isolated browser profile and generated media):
`dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-browser-panel artifacts/sample.mp4 artifacts/browser-panel-smoke`.

Browser tabs use compact 24-pixel headers, shorter titles with ellipses/tooltips,
and small close/new-tab buttons. Tabs and panel opacity stay above the page; capture tools and navigation sit below it. Navigation starts collapsed (Ctrl+L also opens it). The Audio transcript section is hidden until **Audio to text** is clicked; clicking the same button again hides it without stopping capture or recognition. Transcript updates never force the panel open. Keyboard tab shortcuts remain available.

### Video audio to text

Open **Audio to text** to choose a source and transcription language. **Auto detect · Whisper** and **English · Whisper** use the bundled offline model. Select **Video audio** for the playing video or **Microphone** for the default Windows input device (video playback is optional). Choose **Start capture**, then **Stop & transcribe** after the passage you want. Capture
stops automatically after two minutes. The resulting transcript is editable;
choose **Copy transcript** and paste with `Ctrl+V`. Cancelling, errors, or empty
recognition preserve the previous transcript and do not overwrite the clipboard.

Microphone final transcription uses the bundled multilingual Whisper small q5_1 model through FFmpeg/whisper.cpp. It runs on the CPU locally after Stop & transcribe; it does not send audio to a server or require an account. Choose English explicitly when speaking English to avoid short-clip language-detection mistakes. Video audio uses Whisper for Whisper language options, or Windows desktop dictation for an installed Windows language option. Text-to-speech voices are separate. Recognition can still mishear accents, noise, or overlapping speakers; review the result before copying.

The model is approximately 190 MB, loaded by an on-demand worker after capture stops; it does not slow initial app startup. Restore the pinned, SHA256-verified model with `./tools/Setup-SpeechModel.ps1` before building/publishing from Git. The distributed app already includes it, so normal use works offline. Setup uses the network only to download the public model.

Transcripts stay in memory until the app closes and survive browser hide/show.

**Video audio** uses only the selected video's source audio, before playback-volume adjustment. Microphone input, browser audio, other desktop sound and synthesized subtitle reading are excluded from that source. **Microphone** opens the default Windows microphone only after Start capture; WASAPI records a private temporary 16 kHz mono PCM WAV for local Whisper decoding. That temporary audio is deleted on completion, cancellation, or normal close. A crash can leave an owned temporary file. No playback or speaker-monitor stream is opened by the microphone recorder. It stops after two minutes, on Stop & transcribe, Cancel, or app close. Windows microphone privacy permissions and a working default input device are required; failures appear in the transcript panel. The live meter shows input reaching the recognizer, and identifies the input device. Final words appear after Stop & transcribe. Stop & transcribe puts the final result in the editable transcript. Input settings opens Windows sound settings to choose/test the default microphone. Empty results distinguish missing input signal from unrecognized speech; choose a language matching your speech and check input volume/mute settings. The source selection is remembered. To prevent echo, live-preview sound is temporarily suppressed during microphone capture. If the preview is using the same microphone, its **Monitor capture audio** setting is turned off and saved; enable it only when monitoring is intentional. This affects playback through speakers, not source recordings/transcription. For HDMI movie sound, select the capture card audio device instead of the PC microphone. Local files are sampled from the marked playback range;
the selected playback audio track is used. Live HDMI capture needs its audio
input selected and briefly reconnects preview to start/finish an audio-only WAV
capture. Source/seek changes and video recording are disabled while capturing
audio. Playback and other actions are available again during recognition.

Stop/end-of-file transcribes a pending passage. Cancel discards the current
capture; closing the app cancels audio capture/recognition. Temporary PCM audio
files are deleted after recognition/cancellation or shutdown; a device that is
still releasing a file can defer deletion until shutdown. Recognition errors
appear in the transcript panel. Device disconnects interrupt an active capture
and require a new capture after recovery.

Offline dictation tests use synthesized speech without playing it or accessing
the microphone:
`dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-transcription artifacts/transcription-smoke`.
Set `VIDEOCAPTURE_TEST_MICROPHONE=1` to additionally exercise the actual default microphone briefly (start, graceful stop, cancellation/release, and no-video capture); its transcript is not saved or printed.
They also exercise source-audio extraction/tapping, silence, in-flight
cancellation, compact tabs, pause preservation and editable transcripts. Set
`VIDEOCAPTURE_TEST_CLIPBOARD=1` to verify copying and restore the prior clipboard.

Enter an HTTP/HTTPS address and adjust browser opacity between 30% and 100%. **Hide** or `Ctrl+Shift+B` preserves all open tabs and their state. Normal app close saves up to 32 HTTP/HTTPS tab addresses and the active tab; reopening the browser restores them on demand. Restarting reloads pages and does not restore form drafts or navigation history. Tab URLs are stored locally in settings.json. New sessions without saved tabs start at ChatGPT. All tabs share the existing cookies, login sessions, cache, and website storage under `%LOCALAPPDATA%/VideoCapture/BrowserProfile`; this does not share a Chrome profile or extensions. Double-click the browser title bar or press F11 to enter/exit full-monitor fullscreen; Esc exits it. Buttons beside panel opacity provide minimize, maximize/restore, and fullscreen/restore. Scrolling over browser chrome (including capture actions, navigation/address bar, and empty fullscreen borders) adjusts whole-panel opacity by 5 percentage points per wheel notch, clamped to 30–100%; scrolling over web content is unchanged. Page double-clicks keep their normal website behavior. The browser closes with the app and stays above video while VideoCapture is foreground in fullscreen. Microphone access is allowed for directly opened HTTPS ChatGPT pages (`chatgpt.com` and legacy `chat.openai.com`) when the requesting origin matches the top-level page. Old saved microphone denials for those two origins are reset when a tab initializes; each request is checked again without storing a blanket grant. Other origins, embedded cross-origin requests, camera/location permissions, and downloads remain blocked. Windows must still allow desktop microphone access. Use ChatGPT’s microphone/voice controls to start and stop its microphone; this website feature is separate from the app’s offline Audio to text panel. The English OCR engine and language data use Apache 2.0 licensing; the model license is included under `tessdata/LICENSE`. WebView2 is subject to Microsoft's runtime/SDK terms.

Overlay verification: `dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-overlays artifacts/overlay-smoke` checks OCR against known text/blank images, crop bounds, browser URI validation, initialization, DOM interaction, opacity property, and hide/show state. Set `VIDEOCAPTURE_TEST_CLIPBOARD=1` to check real text clipboard round-tripping (restoring prior contents) and desktop bitmap dimensions. `VIDEOCAPTURE_TEST_INPUT=1` additionally attempts native input in the test window; it fails safely if that window cannot gain foreground focus.

**GPU — fastest** is the default preview renderer, using native GPU presentation without copying every frame through a WPF bitmap. **Compatibility — CPU** is available while stopped if GPU presentation fails; its rate depends on CPU and WPF compositor performance. It follows the display's render cycle, with no fixed 30 fps timer. **Fit** shows the whole image with letterboxing when needed; **Fill** crops the edges to cover the screen. Both preserve the source's display aspect ratio, including non-square pixels. Visible source dimensions exclude decoder alignment padding. The app does not change Windows display resolution or refresh rate.

The bundled encoder produces standard 8-bit H.264 video. Recording uses software encoding, so CPU use depends on capture resolution and frame rate. Measure performance on the target PC before long recordings.

Roku's Wi-Fi control API does not provide its video output. A capture device is required. HDCP-protected output cannot be captured; this application does not remove HDCP. A black picture may also indicate the wrong input or a capture format unsupported by the device. Hardware compatibility, end-to-end latency, and audio synchronization must be verified with the actual capture device.

## Build and publish

Install the .NET 10 SDK. Before the first build, restore the pinned external
media tools (their executables exceed GitHub's file-size limit and are excluded
from source control). Extract the **Gyan FFmpeg 8.1.1 full Windows build** from
the [build archive](https://github.com/GyanD/codexffmpeg/releases/tag/8.1.1),
then supply its `bin` folder. The setup script validates both executable SHA-256
hashes before copying them; it can also find a matching installation on `PATH`.
Licenses, source references, and build configuration remain in `Tools/Media`.

```powershell
./tools/Setup-MediaTools.ps1 -FFmpegDirectory 'C:\Tools\ffmpeg-8.1.1-full_build\bin'
./tools/Setup-SpeechModel.ps1
```

```powershell
dotnet build src/VideoCapture -c Release
dotnet publish src/VideoCapture -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Distribute the entire publish folder, including the native `libvlc` directory. Do not distribute the EXE alone. Capture requires the complete `VideoLAN.LibVLC.Windows.GPL` runtime, including `libvlc/win-x64/plugins/access/libdshow_plugin.dll`; the non-GPL package omits DirectShow capture. Build and publish checks reject a missing capture plugin. Do not reference both runtime packages: their MSBuild targets have the same name and one overrides the other.

The bundled VideoLAN runtime includes GPL components. Before distributing a release, review and satisfy the runtime's GPL license requirements and include the required licenses, notices, and corresponding source. LibVLCSharp and bundled codecs also carry third-party licenses. This repository does not assign a license to the application source.

## Integration verification

The smoke executable tests real native playback, pause, seek, missing-file handling, state reset, H.264/AAC recording with a quoted output path, output finalization, device enumeration without opening devices, and WPF layout rendering. FFmpeg generates and inspects the test fixtures; the application also bundles FFmpeg/ffprobe for optional embedded-subtitle reading.

```powershell
New-Item -ItemType Directory artifacts -Force
ffmpeg -f lavfi -i testsrc2=size=640x360:rate=30 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 8 -c:v libx264 -preset ultrafast -c:a aac -y artifacts/sample.mp4
dotnet run --project tests/VideoCapture.Smoke -c Release -- artifacts/sample.mp4 artifacts/smoke
ffprobe -v error -show_streams "artifacts/smoke/recording 'quoted'.mkv"
```

Test HDMI capture separately: select the actual card and audio input, preview motion and sound, record at least 30 seconds, stop, reopen the MKV, and check audio synchronization. Repeat after disconnecting/reconnecting the device. Local test clips cannot validate capture-card drivers, HDCP, or the physical Roku signal.

To probe a specific installed device without saving images or recordings:

```powershell
dotnet run --project tests/VideoCapture.Smoke -c Release -- --probe-camera "USB2.0 HD UVC WebCam"
```

The probe requires ten actual decoded frames. Windows error `0x80070005` means access was denied; a probe run inside a restricted execution environment can receive this even when a normal desktop application can use the device. The installed USB webcam delivered frames successfully when tested outside the restricted environment.

Additional device checks (no images or recordings are saved):

```powershell
dotnet run --project tests/VideoCapture.Smoke -c Release -- --list-modes "USB3 Video"
dotnet run --project tests/VideoCapture.Smoke -c Release -- --probe-mode "USB3 Video" 1920 1080 60
dotnet run --project tests/VideoCapture.Smoke -c Release -- --probe-ui "USB3 Video" 1920 1080 60
```

To include clipboard image/file format checks using generated test media, set `VIDEOCAPTURE_TEST_CLIPBOARD=1` before running the standard smoke executable. This optional test restores the previous clipboard afterward.
## Read subtitles aloud

Open the **Speech** tab and enable **Read subtitles aloud**. The feature is off
by default; its enabled state, installed voice selection, speed, speech volume,
and OCR region are saved. The fullscreen menu includes a **Read aloud** toggle.

**Auto** probes local movie files for embedded text tracks, including MKV SRT
and ASS/SSA subtitles, and reads the selected timeline at playback time. It
prefers an English text track, then a non-forced/default track. Choose another
track explicitly in **Subtitle source / track**. Track selection resets for a
new file. Subtitle timing follows the player's subtitle delay. Probe/extraction
runs in a hidden process with bounded output and cancellation; long files can
take time to load. Failed extraction falls back to OCR.

For live HDMI input, burned-in captions, image subtitle tracks, or files without
readable embedded text, the reader uses offline OCR of the visible video. Turn
on subtitles on the source device for HDMI capture. The default OCR region is
the bottom 35%; choose bottom 50% or the whole picture for other layouts. Image
tracks selected in the app are enabled in the player so OCR can see them. Use
**Fit** if **Fill** crops captions. For spoken-audio transcription, use
**Audio to text** in the browser panel.

OCR currently recognizes English. It samples at most roughly once per second
and requires two matching observations, so very brief captions can be missed;
expect a delay of about 1–2 seconds plus recognition time. Small, animated,
low-contrast or obscured text can be misread. Whole-picture OCR may also read
menus or other visible text. Repeated OCR lines are suppressed. New captions
replace unfinished speech rather than building an audio backlog.

Speech uses installed Windows desktop speech voices through
[System.Speech](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer).
OCR and subtitle extraction run locally and do not need internet access. A
compatible voice must already be installed; voice availability and languages
depend on Windows. Pausing playback pauses speech; seeking, stopping, changing
the source, or disabling the feature cancels old speech and pending OCR results.
Speech volume is independent of the movie volume and is not mixed into capture
recordings. The browser continues to need internet access for online pages.

The app bundles FFmpeg/ffprobe 8.1.1 from
[Gyan's Windows builds](https://www.gyan.dev/ffmpeg/builds/) for subtitle extraction.
See `Tools/Media/LICENSE.FFmpeg.txt`, `BUILD.txt`, and `SOURCE.txt` in the app
folder for license, build configuration, and corresponding source references.

Run the generated-media integration tests (no audible test speech):

```powershell
dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-speech artifacts/speech-smoke
```

## Optional USB-CEC remote

Open **CEC remote** from the main window or fullscreen toolbar. This requires a
separate libCEC-compatible USB-CEC adapter; the USB3 Video capture card is not a
documented CEC controller. Capture and movie playback work without libCEC.

1. Install the [official libCEC Windows tools](https://github.com/Pulse-Eight/libcec/releases)
   and the adapter's Windows driver. If automatic discovery cannot find the tools,
   use **Locate cec-client.exe** to select the executable from that installation.
2. Connect the adapter's USB cable to the PC and its HDMI CEC connection to the
   same HDMI chain as the device being controlled, following the adapter's wiring
   instructions. Enable CEC on the relevant TV/source. An HDMI switch or capture
   device that blocks the CEC wire can prevent discovery.
3. Select **Detect adapters**, select its COM port, and **Connect**. Select an HDMI
   device explicitly before using any remote commands. **Scan HDMI devices**
   refreshes the target list. Disconnect before changing adapters.

The remote provides direction/OK, Home/Back, Play/Pause, volume/mute, power on,
and standby. Commands target the selected logical device, never a broadcast.
Select a TV or audio system for its volume. Roku navigation support depends on
the Roku model/firmware and is not guaranteed. A submitted command does not prove
the target performed the action; the client reports available error diagnostics.

The integration keeps a hidden `cec-client` process open for the connection,
serializes commands, sends both key-press and key-release packets, times out
unresponsive operations, and closes the process on disconnect/application exit.
It disables auto-wake and does not request active-source switching. No adapter
firmware updates or autonomous-mode/EEPROM changes are performed. The external
libCEC tools are installed separately and are not bundled in this application.

Protocol/process failure checks (using a simulated client, without CEC hardware):

```powershell
dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-cec
```

Browser microphone verification: `dotnet run --project tests/VideoCapture.Smoke -c Release -- --test-browser-microphone artifacts/browser-microphone-smoke` checks exact origins, old-denial recovery, a real live audio track, camera denial and an untrusted origin. Fixture pages are served in memory, microphone tracks are stopped immediately, and no microphone audio is sent or saved.
