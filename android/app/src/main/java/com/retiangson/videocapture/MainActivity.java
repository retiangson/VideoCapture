package com.retiangson.videocapture;

import android.Manifest;
import android.content.*;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.*;
import android.hardware.usb.UsbDevice;
import android.media.*;
import android.net.Uri;
import android.os.*;
import android.provider.*;
import android.speech.tts.*;
import android.view.*;
import android.widget.*;
import androidx.activity.ComponentActivity;
import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.annotation.OptIn;
import androidx.core.view.*;
import androidx.media3.common.*;
import androidx.media3.common.text.Cue;
import androidx.media3.common.text.CueGroup;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.transformer.*;
import androidx.media3.ui.*;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;
import com.serenegiant.usb.Size;
import java.io.*;
import java.nio.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

@OptIn(markerClass = UnstableApi.class)
public final class MainActivity extends ComponentActivity {
  private final Handler main = new Handler(Looper.getMainLooper());
  private final ExecutorService io = Executors.newSingleThreadExecutor();
  private FrameLayout root, videoLayer;
  private final Map<Button, java.util.function.BooleanSupplier> toggleStates =
      new LinkedHashMap<>();
  private final Map<String, SeekBar> levelSliders = new LinkedHashMap<>();
  private final Map<String, TextView> levelCaptions = new LinkedHashMap<>();
  private long previewFrames, previewStamp, previewCount;
  private String liveCaption = "LIVE";
  private PlayerView playerView;
  private TextureView usbView;
  private View gesturesView;
  private LinearLayout menu, audioPanel;
  private SourceChooser sourceChooser;
  private TextView status, position, transcriptStatus, mediaTitle, gestureFeedback;
  private EditText transcript;
  private SeekBar seek;
  private ProgressBar levelMeter;
  private Button playButton,
      recordButton,
      audioStart,
      fullscreenButton,
      rotationLockButton,
      hideMenuButton,
      pictureModeButton;
  private Spinner audioSource;
  private ExoPlayer player;
  private PlayerStore store;
  private BrowserOverlay browser;
  private boolean clearingBrowserData;
  private PlaylistOverlay playlistPanel;
  private Button playlistButton;
  private List<PlaylistOverlay.Entry> playlistRows = List.of();
  private String playlistCurrent = "";
  private final ExecutorService playlistIo = Executors.newSingleThreadExecutor();
  private CancellationSignal playlistScan, playlistNamesScan;
  private boolean importingPlaylist, pickerPlay = true;
  private String pickerResumeUri;
  private ActivityResultLauncher<Uri> folders;
  private VideoGestures videoGestures;
  private UsbCapture usb;
  private Surface usbSurface;
  private Size captureSize;
  private boolean wantLive,
      live,
      closed,
      fullscreen = false,
      reading,
      busy,
      ocrBusy,
      rotationLocked;
  private volatile boolean foreground = true;
  private volatile int audioJob;
  private boolean usbRetiring, restoredMode;
  private String selectedUsb = "";
  private final List<String> queue = new ArrayList<>();
  private final Map<String, String> fileNames = new ConcurrentHashMap<>();
  private int selected = -1, generation, usbGeneration;
  private Uri fileUri, lastRecording;
  private long fileSize = -1, fileModified = -1, lastBookmark;
  private boolean draggingSeek;
  private boolean fileFrameReady, usbFrameReady, frameBusy;
  private volatile CaptureRecorder recorder;
  private final Object recordingGate = new Object();
  private Transformer clipExport;
  private File clipExportFile;
  private File pendingLocalClip;
  private long localClipStart;
  private volatile AudioInput usbAudio, microphone;
  private AudioDeviceInfo usbAudioDevice;
  private volatile AudioInput.Samples samples;
  private volatile boolean audioCapturing;
  private boolean transcribing, browserUsingMicrophone, browserAudioReady;
  private final List<Consumer<Boolean>> browserMicrophoneWaiters = new ArrayList<>();
  private final Runnable restoreBrowserAudio = this::restoreBrowserAudioNow;
  private volatile boolean capturingMic;
  private long audioStarted, audioFileStart;
  private AtomicBoolean decoderCancelled = new AtomicBoolean();
  private OfflineSpeech speech;
  private TextRecognizer ocr;
  private OcrConsent ocrConsent;
  private TextToSpeech speaker;
  private boolean ttsReady;
  private String lastRead = "", embeddedCue = "";
  private float videoVolume, readingVolume, brightnessLevel;
  private final Runnable hideGestureFeedback =
      () -> {
        if (gestureFeedback != null) gestureFeedback.setVisibility(View.GONE);
      };
  private long lastOcr;
  private Consumer<Boolean> pendingPermission;
  private boolean requestingMicrophonePermission;
  private Runnable permissionResultOnResume;
  private ActivityResultLauncher<String[]> files;
  private ActivityResultLauncher<Intent> browserFiles;
  private Consumer<Uri[]> browserFilesResult;

  @Override
  public void onCreate(Bundle state) {
    super.onCreate(state);
    browserFiles =
        registerForActivityResult(
            new ActivityResultContracts.StartActivityForResult(),
            result -> {
              Consumer<Uri[]> callback = browserFilesResult;
              browserFilesResult = null;
              if (callback != null)
                callback.accept(
                    android.webkit.WebChromeClient.FileChooserParams.parseResult(
                        result.getResultCode(), result.getData()));
            });
    store = new PlayerStore(this);
    rotationLocked = Boolean.parseBoolean(store.text("rotationLocked", "false"));
    if (rotationLocked)
      setRequestedOrientation(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LOCKED);
    videoVolume = store.level("videoVolume", .75f);
    boolean customBrightness = Boolean.parseBoolean(store.text("customBrightness", "false"));
    brightnessLevel =
        customBrightness ? store.level("brightness", systemBrightness()) : systemBrightness();
    if (customBrightness) applyBrightness(brightnessLevel);
    reading = Boolean.parseBoolean(store.text("reading", "false"));
    readingVolume = store.level("readingVolume", .85f);
    wantLive = "usb".equals(store.text("source", "file"));
    selectedUsb = store.text("usb", "");
    queue.addAll(store.list("playlist", 200));
    speech = new OfflineSpeech(this, main);
    ocrConsent = new OcrConsent(this, store);
    pickerPlay = state == null || state.getBoolean("pickerPlay", true);
    pickerResumeUri = state == null ? null : state.getString("pickerResumeUri");
    files =
        registerForActivityResult(
            new ActivityResultContracts.OpenMultipleDocuments(),
            uris -> {
              if (uris.isEmpty()) {
                resumeAfterPicker();
                return;
              }
              for (Uri uri : uris) retainReadPermission(uri);
              importFiles(uris, pickerPlay);
            });
    folders =
        registerForActivityResult(
            new ActivityResultContracts.OpenDocumentTree(),
            uri -> {
              if (uri == null) {
                resumeAfterPicker();
                return;
              }
              retainReadPermission(uri);
              importFolder(uri, pickerPlay);
            });
    buildUi();
    if (state != null && state.getBoolean("playlistVisible", false)) playlist();
    getOnBackPressedDispatcher()
        .addCallback(
            this,
            new androidx.activity.OnBackPressedCallback(true) {
              public void handleOnBackPressed() {
                if (playlistPanel != null
                    && playlistPanel.getVisibility() == View.VISIBLE
                    && (browser == null
                        || browser.getVisibility() != View.VISIBLE
                        || root.indexOfChild(playlistPanel) > root.indexOfChild(browser))) {
                  playlistPanel.setVisibility(View.GONE);
                  updateToggleStates();
                  return;
                }
                if (browser != null && browser.getVisibility() == View.VISIBLE) {
                  if (audioPanel != null && audioPanel.getVisibility() == View.VISIBLE)
                    audioPanel.setVisibility(View.GONE);
                  else if (!browser.back()) browser.toggle();
                  return;
                }
                if (fullscreen && menu.getVisibility() == View.VISIBLE) {
                  menu.setVisibility(View.GONE);
                  layoutSourceChooser();
                  return;
                }
                if (fullscreen) {
                  setFullscreen(false);
                  return;
                }
                setEnabled(false);
                getOnBackPressedDispatcher().onBackPressed();
              }
            });
    main.post(tick);
    if (getIntent().getData() != null) openIncoming(getIntent().getData());
    else if (wantLive) requestCamera(this::startUsb);
  }

  private void buildUi() {
    WindowCompat.setDecorFitsSystemWindows(getWindow(), false);
    getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    root =
        new FrameLayout(this) {
          @Override
          public boolean dispatchGenericMotionEvent(MotionEvent event) {
            if (event.getAction() == MotionEvent.ACTION_SCROLL) {
              if (browser != null && browser.getVisibility() == View.VISIBLE) {
                android.graphics.Rect bounds = new android.graphics.Rect();
                browser.getHitRect(bounds);
                if (bounds.contains((int) event.getX(), (int) event.getY()))
                  return super.dispatchGenericMotionEvent(event);
              }
              if (menu != null && menu.getVisibility() == View.VISIBLE) {
                android.graphics.Rect bounds = new android.graphics.Rect();
                menu.getHitRect(bounds);
                if (bounds.contains((int) event.getX(), (int) event.getY())) {
                  menu.setAlpha(
                      Math.max(
                          .35f,
                          Math.min(
                              1,
                              menu.getAlpha()
                                  + event.getAxisValue(MotionEvent.AXIS_VSCROLL) * .05f)));
                  store.putLevel("menuOpacity", menu.getAlpha());
                  return true;
                }
              }
              if (adjustWheel(event)) return true;
            }
            return super.dispatchGenericMotionEvent(event);
          }
        };
    root.setBackgroundColor(0xff111514);
    root.addOnLayoutChangeListener(
        (v, l, t, r, b, ol, ot, or, ob) -> {
          if (browser != null) browser.layoutWithinParent();
          if (playlistPanel != null) playlistPanel.layoutWithinParent();
          fitCapture();
          layoutSourceChooser();
          if (menu != null)
            menu.setTranslationY(
                Math.min(
                    0,
                    Math.max(
                        -Math.max(
                            0,
                            root.getHeight()
                                - root.getPaddingTop()
                                - root.getPaddingBottom()
                                - menu.getHeight()),
                        menu.getTranslationY())));
        });
    setContentView(root);
    ViewCompat.setOnApplyWindowInsetsListener(
        root,
        (view, insets) -> {
          androidx.core.graphics.Insets safe =
              insets.getInsets(
                  WindowInsetsCompat.Type.systemBars() | WindowInsetsCompat.Type.ime());
          root.setPadding(safe.left, safe.top, safe.right, safe.bottom);
          root.post(
              () -> {
                if (browser != null) browser.layoutWithinParent();
                if (playlistPanel != null) playlistPanel.layoutWithinParent();
              });
          return insets;
        });
    videoLayer = new FrameLayout(this);
    root.addView(videoLayer, new FrameLayout.LayoutParams(-1, -1));
    playerView = (PlayerView) getLayoutInflater().inflate(R.layout.player, videoLayer, false);
    playerView.setResizeMode(PictureMode.read(store).resizeMode);
    videoLayer.addView(playerView);
    usbView = new TextureView(this);
    videoLayer.addView(usbView, new FrameLayout.LayoutParams(-1, -1));
    usbView.setVisibility(View.GONE);
    usbView.setSurfaceTextureListener(
        new TextureView.SurfaceTextureListener() {
          public void onSurfaceTextureAvailable(SurfaceTexture texture, int w, int h) {
            usbSurface = new Surface(texture);
            if (usb != null) usb.surface(usbSurface);
            fitCapture();
          }

          public void onSurfaceTextureSizeChanged(SurfaceTexture texture, int w, int h) {}

          public boolean onSurfaceTextureDestroyed(SurfaceTexture texture) {
            usbFrameReady = false;
            if (usb != null) usb.surface(null);
            if (usbSurface != null) {
              usbSurface.release();
              usbSurface = null;
            }
            return true;
          }

          public void onSurfaceTextureUpdated(SurfaceTexture texture) {
            previewFrames++;
            if (live && usb != null) usbFrameReady = true;
          }
        });
    gesturesView = new View(this);
    gesturesView.setContentDescription(
        "Video gestures: double-tap for menu, hold for browser, tap to hide menu. Swipe up or down"
            + " on the left for video volume, center for reading volume, right for brightness.");
    videoLayer.addView(gesturesView, new FrameLayout.LayoutParams(-1, -1));
    videoGestures =
        new VideoGestures(
            gesturesView,
            main,
            new VideoGestures.Actions() {
              public void showMenu() {
                menu.setTranslationY(0);
                ((ScrollView) ((PlayerControls) menu).content.getParent()).scrollTo(0, 0);
                menu.setVisibility(View.VISIBLE);
                menu.bringToFront();
                layoutSourceChooser();
              }

              public void hideMenu() {
                menu.setVisibility(View.GONE);
                layoutSourceChooser();
              }

              public void toggleBrowser() {
                toggleBrowserPanel();
              }

              public float level(VideoGestures.Zone zone) {
                return switch (zone) {
                  case VIDEO -> videoVolume;
                  case READING -> readingVolume;
                  case BRIGHTNESS -> brightnessLevel;
                };
              }

              public void level(VideoGestures.Zone zone, float value) {
                switch (zone) {
                  case VIDEO -> setVideoVolume(value);
                  case READING -> setReadingVolume(value);
                  case BRIGHTNESS -> applyBrightness(value);
                }
                String label =
                    switch (zone) {
                      case VIDEO -> "Video volume";
                      case READING -> "Reading volume";
                      case BRIGHTNESS -> "Brightness";
                    };
                float level = level(zone);
                gestureFeedback.setText(label + "  " + Math.round(level * 100) + "%");
                gestureFeedback.setVisibility(View.VISIBLE);
                main.removeCallbacks(hideGestureFeedback);
                main.postDelayed(hideGestureFeedback, 900);
              }
            });
    status =
        Ui.text(this, "VIDEOCAPTURE  ·  Double-tap for menu · Hold for browser", 12, 0xff91a0b7);
    status.setPadding(dp(18), dp(12), dp(18), dp(10));
    FrameLayout.LayoutParams sp = new FrameLayout.LayoutParams(-1, -2, Gravity.TOP);
    root.addView(status, sp);
    gestureFeedback = Ui.text(this, "", 18, PlayerSkin.TEXT);
    gestureFeedback.setGravity(Gravity.CENTER);
    gestureFeedback.setPadding(dp(20), dp(12), dp(20), dp(12));
    gestureFeedback.setBackground(PlayerSkin.surface(dp(12), true));
    gestureFeedback.setVisibility(View.GONE);
    root.addView(gestureFeedback, new FrameLayout.LayoutParams(-2, -2, Gravity.CENTER));
    sourceChooser = new SourceChooser(this, this::chooseUsb, this::pickFiles);
    root.addView(
        sourceChooser, new FrameLayout.LayoutParams(1, 1, Gravity.TOP | Gravity.CENTER_HORIZONTAL));
    status.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) -> layoutSourceChooser());
    PlayerControls controls = new PlayerControls(this);
    menu = controls;
    LinearLayout menuBody = controls.content;
    menu.setPadding(dp(10), dp(8), dp(10), dp(10));
    menu.setBackground(PlayerSkin.surface(dp(16), false));
    menu.setAlpha(Math.max(.35f, store.level("menuOpacity", .96f)));
    FrameLayout.LayoutParams mp = new FrameLayout.LayoutParams(-1, -2, Gravity.BOTTOM);
    mp.setMargins(dp(12), 0, dp(12), dp(12));
    root.addView(menu, mp);
    menu.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) -> layoutSourceChooser());
    LinearLayout top = Ui.row(this);
    TextView grip = Ui.text(this, "VIDEOCAPTURE · Double-tap for menu", 11, PlayerSkin.MUTED);
    grip.setMaxLines(2);
    grip.setEllipsize(android.text.TextUtils.TruncateAt.END);
    top.addView(grip, new LinearLayout.LayoutParams(0, dp(48), 1));
    fullscreenButton =
        MenuIcons.icon(
            this, MenuIcons.Kind.FULLSCREEN, "Fullscreen", () -> setFullscreen(!fullscreen));
    top.addView(fullscreenButton);
    rotationLockButton =
        MenuIcons.icon(this, MenuIcons.Kind.UNLOCK, "Lock rotation", this::toggleRotationLock);
    top.addView(rotationLockButton);
    hideMenuButton =
        MenuIcons.icon(
            this,
            MenuIcons.Kind.HIDE,
            "Hide menu",
            () -> {
              menu.setVisibility(View.GONE);
              layoutSourceChooser();
            });
    top.addView(hideMenuButton);
    menu.addView(top, 0);
    grip.setOnTouchListener(
        new View.OnTouchListener() {
          float sy, start;

          public boolean onTouch(View v, android.view.MotionEvent e) {
            if (e.getActionMasked() == MotionEvent.ACTION_DOWN) {
              sy = e.getRawY();
              start = menu.getTranslationY();
              return true;
            }
            if (e.getActionMasked() == MotionEvent.ACTION_MOVE) {
              menu.setTranslationY(
                  Math.min(
                      0,
                      Math.max(
                          -root.getHeight() + menu.getHeight() + dp(30),
                          start + e.getRawY() - sy)));
              return true;
            }
            if (e.getActionMasked() == MotionEvent.ACTION_UP) v.performClick();
            return true;
          }
        });
    LinearLayout progress = Ui.row(this);
    mediaTitle = Ui.text(this, "READY TO PLAY", 13, PlayerSkin.TEXT);
    mediaTitle.setSingleLine(true);
    mediaTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
    mediaTitle.setGravity(Gravity.CENTER);
    mediaTitle.setPadding(dp(8), dp(8), dp(8), dp(4));
    menuBody.addView(mediaTitle);
    seek = new PlayerSeekBar(this);
    seek.setContentDescription("Video progress");
    seek.setMax(1000);
    progress.addView(seek, new LinearLayout.LayoutParams(0, dp(48), 1));
    position = Ui.text(this, "00:00 / 00:00", 11, 0xff91a0b7);
    progress.addView(position);
    menuBody.addView(progress);
    seek.setOnSeekBarChangeListener(
        new SeekBar.OnSeekBarChangeListener() {
          public void onStartTrackingTouch(SeekBar bar) {
            draggingSeek = true;
          }

          public void onStopTrackingTouch(SeekBar bar) {
            draggingSeek = false;
            if (player != null && !live && !sourceLocked())
              player.seekTo(player.getDuration() * bar.getProgress() / 1000);
          }

          public void onProgressChanged(SeekBar bar, int value, boolean user) {}
        });
    LinearLayout actions = Ui.row(this);
    actions.addView(MenuIcons.action(this, "Open", MenuIcons.Kind.OPEN, this::pickFiles));
    playlistButton = MenuIcons.action(this, "Playlist", MenuIcons.Kind.PLAYLIST, this::playlist);
    actions.addView(playlistButton);
    actions.addView(
        MenuIcons.action(this, "Open folder", MenuIcons.Kind.OPEN, () -> pickFolder(true)));
    actions.addView(MenuIcons.action(this, "USB", MenuIcons.Kind.USB, this::chooseUsb));
    LinearLayout transport = Ui.row(this);
    transport.setGravity(Gravity.CENTER);
    transport.setPadding(dp(6), dp(6), dp(6), dp(6));
    transport.setBackground(PlayerSkin.surface(dp(36), false));
    transport.addView(
        PlayerSkin.transport(this, PlayerSkin.Icon.Kind.STOP, "Stop video", this::stop, false));
    transport.addView(
        PlayerSkin.transport(
            this,
            PlayerSkin.Icon.Kind.PREVIOUS,
            "Previous video",
            () -> playIndex(selected - 1),
            false));
    playButton =
        PlayerSkin.transport(this, PlayerSkin.Icon.Kind.PLAY, "Play video", this::playPause, true);
    transport.addView(playButton);
    transport.addView(
        PlayerSkin.transport(
            this, PlayerSkin.Icon.Kind.NEXT, "Next video", () -> playIndex(selected + 1), false));
    recordButton =
        PlayerSkin.transport(
            this, PlayerSkin.Icon.Kind.RECORD, "Record video", this::record, false);
    transport.addView(recordButton);
    menuBody.addView(transport);
    actions.addView(MenuIcons.action(this, "Copy frame", MenuIcons.Kind.FRAME, () -> frame(false)));
    actions.addView(MenuIcons.action(this, "Save frame", MenuIcons.Kind.SAVE, () -> frame(true)));
    actions.addView(
        MenuIcons.action(this, "Copy recording", MenuIcons.Kind.COPY, this::copyRecording));
    menuBody.addView(Ui.scrollRow(this, actions));
    LinearLayout more = Ui.row(this);
    more.addView(MenuIcons.action(this, "Mode / FPS", MenuIcons.Kind.SETTINGS, this::chooseMode));
    more.addView(MenuIcons.action(this, "Audio input", MenuIcons.Kind.MIC, this::chooseAudio));
    more.addView(
        MenuIcons.action(this, "Audio track", MenuIcons.Kind.MUSIC, this::chooseFileAudio));
    pictureModeButton =
        MenuIcons.action(
            this,
            "Picture: " + PictureMode.read(store).label,
            MenuIcons.Kind.PICTURE,
            this::choosePictureMode);
    more.addView(pictureModeButton);
    addToggle(
        more,
        "Browser",
        MenuIcons.Kind.BROWSER,
        () -> browser != null && browser.getVisibility() == View.VISIBLE,
        this::toggleBrowserPanel);
    more.addView(
        MenuIcons.action(this, "Aa Copy text", MenuIcons.Kind.TEXT, () -> recognize(false)));
    more.addView(
        MenuIcons.action(this, "Subtitle track", MenuIcons.Kind.SUBTITLE, this::chooseSubtitles));
    addToggle(more, "Read subtitles", MenuIcons.Kind.READ, () -> reading, this::toggleReading);
    more.addView(MenuIcons.action(this, "Voice", MenuIcons.Kind.AUDIO, this::chooseVoice));
    more.addView(MenuIcons.action(this, "Licenses", MenuIcons.Kind.INFO, this::licenses));
    more.addView(MenuIcons.action(this, "Privacy", MenuIcons.Kind.INFO, this::showPrivacy));
    addToggle(
        more,
        "Audio to text",
        MenuIcons.Kind.MIC,
        () ->
            browser != null
                && browser.getVisibility() == View.VISIBLE
                && audioPanel != null
                && audioPanel.getVisibility() == View.VISIBLE,
        this::toggleAudioPanel);
    addToggle(
        more,
        "Timeline",
        MenuIcons.Kind.TIMELINE,
        () -> progress.getVisibility() == View.VISIBLE,
        () -> {
          progress.setVisibility(
              progress.getVisibility() == View.VISIBLE ? View.GONE : View.VISIBLE);
          store.put("timeline", progress.getVisibility() == View.VISIBLE ? "true" : "false");
        });
    more.addView(
        MenuIcons.action(this, "Brightness", MenuIcons.Kind.BRIGHTNESS, this::chooseBrightness));
    menuBody.addView(Ui.scrollRow(this, more));
    progress.setVisibility(
        Boolean.parseBoolean(store.text("timeline", "true")) ? View.VISIBLE : View.GONE);
    LinearLayout levels = Ui.row(this);
    addLevel(levels, "Video", videoVolume, this::setVideoVolume);
    addLevel(levels, "Reading", readingVolume, this::setReadingVolume);
    addLevel(
        levels,
        "Opacity",
        menu.getAlpha(),
        v -> {
          menu.setAlpha(Math.max(.35f, v));
          store.putLevel("menuOpacity", menu.getAlpha());
        });
    menuBody.addView(levels);
    menu.setVisibility(View.GONE);
    root.setOnGenericMotionListener((v, e) -> adjustWheel(e));
    menu.setOnGenericMotionListener(
        (v, e) -> {
          if (e.getAction() == MotionEvent.ACTION_SCROLL) {
            menu.setAlpha(
                Math.max(
                    .35f,
                    Math.min(
                        1, menu.getAlpha() + e.getAxisValue(MotionEvent.AXIS_VSCROLL) * .05f)));
            store.putLevel("menuOpacity", menu.getAlpha());
            return true;
          }
          return false;
        });
    gesturesView.setOnGenericMotionListener((v, e) -> adjustWheel(e));
    root.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) -> fitCapture());
    setFullscreen(false);
  }

  private void addToggle(
      LinearLayout row,
      String label,
      MenuIcons.Kind icon,
      java.util.function.BooleanSupplier state,
      Runnable action) {
    Button button =
        MenuIcons.action(
            this,
            label,
            icon,
            () -> {
              action.run();
              updateToggleStates();
            });
    toggleStates.put(button, state);
    row.addView(button);
  }

  private void toggleRotationLock() {
    rotationLocked = !rotationLocked;
    setRequestedOrientation(
        rotationLocked
            ? android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LOCKED
            : android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);
    store.put("rotationLocked", Boolean.toString(rotationLocked));
    updateToggleStates();
  }

  private void setVideoVolume(float value) {
    videoVolume = Math.max(0, Math.min(1, value));
    store.putLevel("videoVolume", videoVolume);
    if (player != null)
      player.setVolume(
          (audioCapturing && capturingMic || browserUsingMicrophone) ? 0 : videoVolume);
    if (usbAudio != null)
      usbAudio.monitor(!(audioCapturing && capturingMic || browserUsingMicrophone), videoVolume);
    updateLevel("Video", videoVolume);
  }

  private void setReadingVolume(float value) {
    readingVolume = Math.max(0, Math.min(1, value));
    store.putLevel("readingVolume", readingVolume);
    updateLevel("Reading", readingVolume);
  }

  private float systemBrightness() {
    return Math.max(
        .05f,
        Math.min(
            1,
            Settings.System.getInt(getContentResolver(), Settings.System.SCREEN_BRIGHTNESS, 128)
                / 255f));
  }

  private void applyBrightness(float value) {
    brightnessLevel = Math.max(.05f, Math.min(1, value));
    WindowManager.LayoutParams attributes = getWindow().getAttributes();
    attributes.screenBrightness = brightnessLevel;
    getWindow().setAttributes(attributes);
    store.putLevel("brightness", brightnessLevel);
    store.put("customBrightness", "true");
  }

  private void chooseBrightness() {
    SeekBar slider = Ui.slider(this, brightnessLevel, this::applyBrightness);
    slider.setMin(50);
    slider.setContentDescription("VideoCapture screen brightness");
    new android.app.AlertDialog.Builder(this)
        .setTitle("Screen brightness")
        .setView(slider)
        .setPositiveButton("Done", null)
        .setNeutralButton(
            "Use system",
            (dialog, which) -> {
              store.put("customBrightness", "false");
              brightnessLevel = systemBrightness();
              WindowManager.LayoutParams attributes = getWindow().getAttributes();
              attributes.screenBrightness = WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE;
              getWindow().setAttributes(attributes);
            })
        .show();
  }

  private void updateToggleStates() {
    if (closed) return;
    Ui.active(
        playlistButton, playlistPanel != null && playlistPanel.getVisibility() == View.VISIBLE);
    if (playlistPanel != null) {
      String current = live ? "usb" : fileUri == null ? "" : fileUri.toString();
      if (!current.equals(playlistCurrent)) refreshPlaylist();
      else
        playlistPanel.update(
            playlistRows, !sourceLocked() && !importingPlaylist, importingPlaylist);
    }
    updateLevel("Video", videoVolume);
    updateLevel("Reading", readingVolume);
    updateLevel("Opacity", menu.getAlpha());
    for (Map.Entry<Button, java.util.function.BooleanSupplier> entry : toggleStates.entrySet())
      Ui.active(entry.getKey(), entry.getValue().getAsBoolean());
    if (pictureModeButton != null) {
      PictureMode mode = PictureMode.read(store);
      String label = "Picture: " + mode.label;
      if (!label.contentEquals(pictureModeButton.getText())) pictureModeButton.setText(label);
      Ui.active(pictureModeButton, mode != PictureMode.REGULAR, mode.label);
    }
    Ui.active(fullscreenButton, fullscreen);
    Ui.active(rotationLockButton, rotationLocked);
    MenuIcons.setIcon(
        rotationLockButton, rotationLocked ? MenuIcons.Kind.LOCK : MenuIcons.Kind.UNLOCK);
    rotationLockButton.setContentDescription(rotationLocked ? "Unlock rotation" : "Lock rotation");
    boolean playing = player != null && !live && player.isPlaying();
    Ui.active(playButton, playing);
    PlayerSkin.icon(
        playButton, playing ? PlayerSkin.Icon.Kind.PAUSE : PlayerSkin.Icon.Kind.PLAY, 30);
    playButton.setContentDescription(playing ? "Pause video" : "Play video");
    playButton.setEnabled(player != null && !live);
    String title = live ? "USB CAPTURE" : fileUri != null ? name(fileUri) : "READY TO PLAY";
    if (!title.contentEquals(mediaTitle.getText())) mediaTitle.setText(title);
    boolean recording = recorder != null || pendingLocalClip != null;
    Ui.active(recordButton, recording);
    Ui.active(audioStart, audioCapturing || transcribing);
    if (browser != null)
      browser.captureStates(
          recording, audioPanel != null && audioPanel.getVisibility() == View.VISIBLE);
  }

  private void showSourceChooser(boolean visible) {
    sourceChooser.setVisibility(visible ? View.VISIBLE : View.GONE);
    layoutSourceChooser();
  }

  private void layoutSourceChooser() {
    if (sourceChooser == null || menu == null) return;
    PlayerControls controls = (PlayerControls) menu;
    if (sourceChooser.getVisibility() != View.VISIBLE) {
      controls.heightLimit(Integer.MAX_VALUE);
      return;
    }
    int width = root.getWidth() - root.getPaddingLeft() - root.getPaddingRight();
    int height = root.getHeight() - root.getPaddingTop() - root.getPaddingBottom();
    if (width <= 0 || height <= 0) return;
    int top = (status.getVisibility() == View.VISIBLE ? status.getHeight() : 0) + dp(8);
    controls.heightLimit(Math.max(dp(52), height - top - dp(26) - dp(64)));
    int reserved = menu.getVisibility() == View.VISIBLE ? menu.getHeight() + dp(26) : dp(8);
    FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) sourceChooser.getLayoutParams();
    int newWidth = Math.max(1, Math.min(dp(480), width - dp(24)));
    int newHeight = Math.max(0, height - top - reserved);
    if (p.width != newWidth || p.height != newHeight || p.topMargin != top) {
      p.width = newWidth;
      p.height = newHeight;
      p.topMargin = top;
      sourceChooser.setLayoutParams(p);
    }
  }

  private int dp(float value) {
    return Ui.dp(this, value);
  }

  private void addLevel(LinearLayout row, String label, float value, Ui.Level changed) {
    LinearLayout box = Ui.column(this);
    box.setPadding(dp(6), dp(8), dp(6), dp(2));
    box.setBackground(PlayerSkin.surface(dp(8), false));
    TextView caption =
        Ui.text(
            this,
            label.toUpperCase(Locale.ROOT) + "  " + Math.round(value * 100) + "%",
            10,
            PlayerSkin.MUTED);
    caption.setSingleLine(true);
    box.addView(caption);
    SeekBar slider =
        Ui.slider(
            this,
            value,
            v -> {
              caption.setText(label.toUpperCase(Locale.ROOT) + "  " + Math.round(v * 100) + "%");
              changed.changed(v);
            });
    slider.setContentDescription(label + " level");
    if ("Opacity".equals(label)) slider.setMin(350);
    levelSliders.put(label, slider);
    levelCaptions.put(label, caption);
    box.addView(slider);
    LinearLayout.LayoutParams bounds = new LinearLayout.LayoutParams(0, -2, 1);
    bounds.setMargins(dp(2), dp(8), dp(2), 0);
    row.addView(box, bounds);
  }

  private void updateLevel(String label, float value) {
    SeekBar slider = levelSliders.get(label);
    if (slider == null) return;
    int progress = Math.round(value * 1000);
    if (slider.getProgress() != progress) slider.setProgress(progress);
    String text = label.toUpperCase(Locale.ROOT) + "  " + Math.round(value * 100) + "%";
    TextView caption = levelCaptions.get(label);
    if (!text.contentEquals(caption.getText())) caption.setText(text);
  }

  private boolean adjustWheel(MotionEvent e) {
    if (e.getAction() != MotionEvent.ACTION_SCROLL) return false;
    float change = e.getAxisValue(MotionEvent.AXIS_VSCROLL) * .05f;
    if (browser != null && browser.getVisibility() == View.VISIBLE)
      browser.opacity(browser.getAlpha() + change);
    else if (menu.getVisibility() == View.VISIBLE) {
      menu.setAlpha(Math.max(.35f, Math.min(1, menu.getAlpha() + change)));
      store.putLevel("menuOpacity", menu.getAlpha());
    } else return false;
    return true;
  }

  private void setFullscreen(boolean value) {
    fullscreen = value;
    updateStatusVisibility();
    menu.setVisibility(value ? View.GONE : View.VISIBLE);
    hideMenuButton.setVisibility(View.VISIBLE);
    MenuIcons.setIcon(fullscreenButton, value ? MenuIcons.Kind.RESTORE : MenuIcons.Kind.FULLSCREEN);
    fullscreenButton.setContentDescription(value ? "Exit fullscreen" : "Fullscreen");
    layoutSourceChooser();
    updateToggleStates();
    WindowInsetsControllerCompat controller =
        WindowCompat.getInsetsController(getWindow(), getWindow().getDecorView());
    if (value) {
      controller.hide(WindowInsetsCompat.Type.systemBars());
      controller.setSystemBarsBehavior(
          WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
    } else controller.show(WindowInsetsCompat.Type.systemBars());
  }

  private void message(String value) {
    main.post(
        () -> {
          if (!closed) {
            status.setText(value);
            updateStatusVisibility();
          }
        });
  }

  private void updateStatusVisibility() {
    String caption = status.getText().toString();
    boolean sourceCaption = caption.startsWith("LIVE") || caption.startsWith("LOCAL");
    status.setVisibility(fullscreen && sourceCaption ? View.GONE : View.VISIBLE);
    layoutSourceChooser();
  }

  private boolean sourceLocked() {
    return recorder != null || pendingLocalClip != null || audioCapturing && !capturingMic || busy;
  }

  private void pickFiles() {
    pickFiles(true);
  }

  private void pickFiles(boolean play) {
    if (sourceLocked()
        || importingPlaylist
        || audioCapturing
        || transcribing
        || browserUsingMicrophone) {
      message("Finish the current capture, voice session or import first.");
      return;
    }
    pickerPlay = play;
    pickerResumeUri =
        player != null && player.isPlaying() && fileUri != null ? fileUri.toString() : null;
    try {
      files.launch(new String[] {"video/*", "audio/*"});
    } catch (ActivityNotFoundException error) {
      message("No document picker is installed.");
    }
  }

  private void pickFolder(boolean play) {
    if (sourceLocked()
        || importingPlaylist
        || audioCapturing
        || transcribing
        || browserUsingMicrophone) {
      message("Finish the current capture, voice session or import first.");
      return;
    }
    pickerPlay = play;
    pickerResumeUri =
        player != null && player.isPlaying() && fileUri != null ? fileUri.toString() : null;
    try {
      folders.launch(null);
    } catch (ActivityNotFoundException error) {
      message("No folder picker is installed.");
    }
  }

  private void retainReadPermission(Uri uri) {
    try {
      getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
    } catch (SecurityException ignored) {
      /* Some document providers grant session-only access. */
    }
  }

  private void resumeAfterPicker() {
    if (pickerResumeUri != null
        && player != null
        && !live
        && fileUri != null
        && pickerResumeUri.equals(fileUri.toString())
        && !sourceLocked()) player.play();
    pickerResumeUri = null;
  }

  private void importFiles(List<Uri> uris, boolean play) {
    cancelPlaylistImport();
    if (playlistNamesScan != null) playlistNamesScan.cancel();
    CancellationSignal signal = new CancellationSignal();
    playlistScan = signal;
    importingPlaylist = true;
    refreshPlaylist();
    playlistIo.execute(
        () -> {
          List<FolderMedia.Entry> found = new ArrayList<>();
          try {
            for (Uri uri : uris) {
              signal.throwIfCanceled();
              if (found.size() == 200) break;
              if (uri.toString().length() > 8192 || !"content".equals(uri.getScheme())) continue;
              String title = uri.getLastPathSegment();
              try (Cursor row =
                  getContentResolver()
                      .query(
                          uri,
                          new String[] {OpenableColumns.DISPLAY_NAME},
                          null,
                          null,
                          null,
                          signal)) {
                if (row != null && row.moveToFirst() && row.getString(0) != null)
                  title = row.getString(0);
              } catch (SecurityException error) {
                continue;
              }
              found.add(new FolderMedia.Entry(uri, title == null ? "Video" : title));
            }
            main.post(() -> acceptPlaylistImport(signal, found, play, uris.size() > 200, 0));
          } catch (OperationCanceledException ignored) {
          } catch (Exception error) {
            main.post(() -> failPlaylistImport(signal, error.getMessage()));
          }
        });
  }

  private void importFolder(Uri tree, boolean play) {
    cancelPlaylistImport();
    if (playlistNamesScan != null) playlistNamesScan.cancel();
    CancellationSignal signal = new CancellationSignal();
    playlistScan = signal;
    importingPlaylist = true;
    refreshPlaylist();
    playlistIo.execute(
        () -> {
          try {
            FolderMedia.Result result = FolderMedia.scan(getContentResolver(), tree, signal);
            main.post(
                () ->
                    acceptPlaylistImport(
                        signal, result.entries(), play, result.limited(), result.skipped()));
          } catch (OperationCanceledException ignored) {
          } catch (Exception error) {
            main.post(() -> failPlaylistImport(signal, error.getMessage()));
          }
        });
  }

  private void acceptPlaylistImport(
      CancellationSignal signal,
      List<FolderMedia.Entry> found,
      boolean play,
      boolean limited,
      int skipped) {
    if (closed || playlistScan != signal || signal.isCanceled()) return;
    playlistScan = null;
    importingPlaylist = false;
    int added = 0;
    String first = null;
    Map<String, String> existing = new HashMap<>();
    for (String value : queue) existing.putIfAbsent(FolderMedia.identity(Uri.parse(value)), value);
    for (FolderMedia.Entry entry : found) {
      String uri = entry.uri().toString();
      String key = FolderMedia.identity(entry.uri());
      String saved = existing.get(key);
      if (saved != null) {
        fileNames.put(saved, entry.name());
        if (first == null) first = saved;
        continue;
      }
      if (queue.size() == 200) {
        limited = true;
        continue;
      }
      queue.add(uri);
      existing.put(key, uri);
      fileNames.put(uri, entry.name());
      added++;
      if (first == null) first = uri;
    }
    store.list("playlist", queue);
    refreshPlaylist();
    if (play && first != null) {
      pickerResumeUri = null;
      playIndex(queue.indexOf(first));
    } else resumeAfterPicker();
    message(
        added
            + " item(s) added"
            + (limited ? " · playlist/import limit reached (200 items)" : "")
            + (skipped > 0 ? " · " + skipped + " inaccessible folder(s) skipped" : "")
            + (found.isEmpty() ? " · no supported media found" : ""));
  }

  private void failPlaylistImport(CancellationSignal signal, String error) {
    if (closed || playlistScan != signal) return;
    playlistScan = null;
    importingPlaylist = false;
    refreshPlaylist();
    resumeAfterPicker();
    message("Could not read media: " + error);
  }

  private void cancelPlaylistImport() {
    if (playlistScan != null) playlistScan.cancel();
    playlistScan = null;
    importingPlaylist = false;
    if (playlistPanel != null) refreshPlaylist();
  }

  private void openIncoming(Uri uri) {
    if (!queue.contains(uri.toString())) {
      if (queue.size() >= 200) {
        message("Playlist full. Remove an item before opening another video.");
        return;
      }
      queue.add(uri.toString());
    }
    store.list("playlist", queue);
    playIndex(queue.indexOf(uri.toString()));
  }

  private void ensurePlayer() {
    if (player != null) return;
    player = new ExoPlayer.Builder(this).build();
    playerView.setPlayer(player);
    player.setVolume(browserUsingMicrophone || audioCapturing && capturingMic ? 0 : videoVolume);
    player.setTrackSelectionParameters(
        player
            .getTrackSelectionParameters()
            .buildUpon()
            .setSelectUndeterminedTextLanguage(true)
            .build());
    player.addListener(
        new Player.Listener() {
          @Override
          public void onPlaybackStateChanged(int state) {
            if (state == Player.STATE_ENDED && !live) {
              bookmark(true);
              if (pendingLocalClip != null) finishLocalClip();
              if (audioCapturing) finishAudio();
              if (selected + 1 < queue.size()) playIndex(selected + 1);
            }
          }

          @Override
          public void onRenderedFirstFrame() {
            fileFrameReady = true;
          }

          @Override
          public void onMediaItemTransition(MediaItem item, int reason) {
            fileFrameReady = false;
          }

          @Override
          public void onPlayerError(PlaybackException error) {
            fileFrameReady = false;
            showSourceChooser(true);
            message("Playback: " + error.getMessage());
          }

          @Override
          public void onCues(CueGroup group) {
            StringBuilder text = new StringBuilder();
            for (Cue cue : group.cues) {
              if (cue.text != null) {
                if (text.length() > 0) text.append('\n');
                text.append(cue.text);
              }
            }
            embeddedCue = text.toString();
            if (reading && !embeddedCue.isBlank()) speak(embeddedCue);
            else if (reading)
              for (Cue cue : group.cues)
                if (cue.bitmap != null) {
                  recognizeSubtitleBitmap(cue.bitmap);
                  break;
                }
          }
        });
  }

  private void playIndex(int index) {
    if (index < 0 || index >= queue.size() || sourceLocked()) return;
    Uri uri = Uri.parse(queue.get(index));
    if (!"content".equals(uri.getScheme()) && !"file".equals(uri.getScheme())) {
      message("Media must be a local file or document.");
      return;
    }
    bookmark(false);
    fileFrameReady = false;
    wantLive = false;
    store.put("source", "file");
    stopUsb();
    live = false;
    usbView.setVisibility(View.GONE);
    playerView.setVisibility(View.VISIBLE);
    ensurePlayer();
    int token = ++generation;
    busy = true;
    io.execute(
        () -> {
          long[] meta = metadata(uri);
          main.post(
              () -> {
                if (closed || token != generation) return;
                busy = false;
                selected = index;
                fileUri = uri;
                fileSize = meta[0];
                fileModified = meta[1];
                embeddedCue = "";
                lastRead = "";
                player.setMediaItem(
                    MediaItem.fromUri(uri), store.resume(uri.toString(), fileSize, fileModified));
                player.prepare();
                player.play();
                showSourceChooser(false);
                message("LOCAL · " + name(uri));
              });
        });
  }

  private long[] metadata(Uri uri) {
    long size = -1, modified = -1;
    try (Cursor cursor =
        getContentResolver()
            .query(
                uri,
                new String[] {
                  OpenableColumns.DISPLAY_NAME,
                  OpenableColumns.SIZE,
                  DocumentsContract.Document.COLUMN_LAST_MODIFIED
                },
                null,
                null,
                null)) {
      if (cursor != null && cursor.moveToFirst()) {
        int n = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
        if (n >= 0) fileNames.put(uri.toString(), cursor.getString(n));
        int s = cursor.getColumnIndex(OpenableColumns.SIZE),
            m = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_LAST_MODIFIED);
        if (s >= 0) size = cursor.getLong(s);
        if (m >= 0) modified = cursor.getLong(m);
      }
    } catch (Exception ignored) {
    }
    if ("file".equals(uri.getScheme())) {
      File file = new File(uri.getPath());
      size = file.length();
      modified = file.lastModified();
    }
    return new long[] {size, modified};
  }

  private String name(Uri uri) {
    String cached = fileNames.get(uri.toString());
    return cached != null
        ? cached
        : uri.getLastPathSegment() == null ? "Video" : uri.getLastPathSegment();
  }

  private void bookmark(boolean completed) {
    if (player != null && !live && fileUri != null) {
      long duration = Math.max(0, player.getDuration());
      store.bookmark(
          fileUri.toString(),
          completed ? duration : player.getCurrentPosition(),
          duration,
          fileSize,
          fileModified);
    }
  }

  private void playPause() {
    if (player != null && !live) {
      if (player.isPlaying()) {
        bookmark(false);
        player.pause();
      } else player.play();
    }
  }

  private void stop() {
    if (audioCapturing) finishAudio();
    if (recorder != null) finishCaptureRecording();
    if (pendingLocalClip != null) finishLocalClip();
    bookmark(false);
    if (player != null) player.pause();
    if (live) {
      wantLive = false;
      stopUsb();
      showSourceChooser(true);
    }
    message("Stopped.");
  }

  private void playlist() {
    if (playlistPanel == null) {
      playlistPanel =
          new PlaylistOverlay(
              this,
              new PlaylistOverlay.Host() {
                public void files() {
                  pickFiles(false);
                }

                public void folder() {
                  pickFolder(false);
                }

                public void play(String uri) {
                  playIndex(queue.indexOf(uri));
                }

                public void remove(String uri) {
                  removePlaylistItem(uri);
                }

                public void move(String uri, int offset) {
                  movePlaylistItem(uri, offset);
                }

                public void clear() {
                  if (sourceLocked() || importingPlaylist) return;
                  new android.app.AlertDialog.Builder(MainActivity.this)
                      .setTitle("Clear playlist?")
                      .setMessage(
                          "Remove all playlist entries? Your original media files will remain on"
                              + " the device.")
                      .setNegativeButton("Cancel", null)
                      .setPositiveButton("Clear", (dialog, which) -> clearPlaylist())
                      .show();
                }

                public void cancelImport() {
                  cancelPlaylistImport();
                  resumeAfterPicker();
                  message("Folder scan cancelled.");
                }
              });
      root.addView(playlistPanel, new FrameLayout.LayoutParams(1, 1));
      playlistPanel.layoutWithinParent();
    }
    refreshPlaylist();
    playlistPanel.toggle();
    updateToggleStates();
    if (playlistPanel.getVisibility() == View.VISIBLE) loadPlaylistNames();
  }

  private void refreshPlaylist() {
    playlistCurrent = live ? "usb" : fileUri == null ? "" : fileUri.toString();
    if (playlistPanel == null) return;
    List<PlaylistOverlay.Entry> rows = new ArrayList<>();
    for (String value : queue) {
      Uri uri = Uri.parse(value);
      rows.add(
          new PlaylistOverlay.Entry(
              value, name(uri), playlistLocation(uri), !live && value.equals(playlistCurrent)));
    }
    playlistRows = List.copyOf(rows);
    playlistPanel.update(playlistRows, !sourceLocked() && !importingPlaylist, importingPlaylist);
  }

  private void loadPlaylistNames() {
    if (playlistNamesScan != null && !playlistNamesScan.isCanceled()) return;
    List<String> pending = new ArrayList<>();
    for (String uri : queue) if (!fileNames.containsKey(uri)) pending.add(uri);
    if (pending.isEmpty()) return;
    CancellationSignal signal = new CancellationSignal();
    playlistNamesScan = signal;
    playlistIo.execute(
        () -> {
          try {
            for (String value : pending) {
              signal.throwIfCanceled();
              try (Cursor row =
                  getContentResolver()
                      .query(
                          Uri.parse(value),
                          new String[] {OpenableColumns.DISPLAY_NAME},
                          null,
                          null,
                          null,
                          signal)) {
                if (row != null && row.moveToFirst() && row.getString(0) != null)
                  fileNames.put(value, row.getString(0));
              } catch (SecurityException | IllegalArgumentException ignored) {
              }
            }
          } catch (OperationCanceledException ignored) {
          } finally {
            main.post(
                () -> {
                  if (playlistNamesScan == signal) playlistNamesScan = null;
                  if (!closed) refreshPlaylist();
                });
          }
        });
  }

  private String playlistLocation(Uri uri) {
    String segment = uri.getLastPathSegment();
    if (segment != null && segment.contains("/")) {
      String parent = segment.substring(0, segment.lastIndexOf('/'));
      if (parent.contains(":")) parent = parent.substring(parent.indexOf(':') + 1);
      if (!parent.isBlank()) return parent;
    }
    return "Media file";
  }

  private void removePlaylistItem(String uri) {
    if (sourceLocked() || importingPlaylist || !queue.contains(uri)) return;
    if (fileUri != null && uri.equals(fileUri.toString())) {
      bookmark(false);
      if (!live && player != null) player.stop();
      fileUri = null;
      fileFrameReady = false;
    }
    queue.remove(uri);
    fileNames.remove(uri);
    selected = fileUri == null ? -1 : queue.indexOf(fileUri.toString());
    store.list("playlist", queue);
    refreshPlaylist();
    if (fileUri == null && !live) showSourceChooser(true);
  }

  private void movePlaylistItem(String uri, int offset) {
    if (sourceLocked() || importingPlaylist || Math.abs(offset) != 1) return;
    int index = queue.indexOf(uri), next = index + offset;
    if (index < 0 || next < 0 || next >= queue.size()) return;
    Collections.swap(queue, index, next);
    selected = fileUri == null ? -1 : queue.indexOf(fileUri.toString());
    store.list("playlist", queue);
    refreshPlaylist();
  }

  private void clearPlaylist() {
    if (sourceLocked() || importingPlaylist) return;
    bookmark(false);
    if (!live && player != null) player.stop();
    queue.clear();
    fileNames.clear();
    selected = -1;
    fileUri = null;
    fileFrameReady = false;
    store.list("playlist", queue);
    refreshPlaylist();
    if (!live) showSourceChooser(true);
  }

  @Override
  protected void onSaveInstanceState(Bundle state) {
    state.putBoolean("pickerPlay", pickerPlay);
    state.putString("pickerResumeUri", pickerResumeUri);
    state.putBoolean(
        "playlistVisible", playlistPanel != null && playlistPanel.getVisibility() == View.VISIBLE);
    super.onSaveInstanceState(state);
  }

  private void chooseUsb() {
    if (sourceLocked() || usbRetiring) return;
    requestCamera(
        () -> {
          if (usb == null) createUsb();
          List<UsbDevice> devices = usb.devices();
          String[] labels =
              devices.stream()
                  .map(
                      d ->
                          (d.getProductName() == null ? "USB video" : d.getProductName())
                              + " · "
                              + d.getVendorId()
                              + ":"
                              + d.getProductId())
                  .toArray(String[]::new);
          if (labels.length == 0) {
            new android.app.AlertDialog.Builder(this)
                .setTitle("No USB capture device found")
                .setMessage(
                    "Connect the HDMI capture card through USB-C/OTG, then select USB again.")
                .setPositiveButton("Close", null)
                .show();
            return;
          }
          new android.app.AlertDialog.Builder(this)
              .setTitle("USB video device")
              .setItems(
                  labels,
                  (d, index) -> {
                    wantLive = true;
                    selectedUsb = usbKey(devices.get(index));
                    store.put("usb", selectedUsb);
                    store.put("source", "usb");
                    bookmark(false);
                    if (player != null) player.pause();
                    fileUri = null;
                    playerView.setVisibility(View.GONE);
                    usbView.setVisibility(View.VISIBLE);
                    message("Opening USB capture device…");
                    usb.select(devices.get(index));
                  })
              .show();
        });
  }

  private static String usbKey(UsbDevice device) {
    return device.getVendorId() + ":" + device.getProductId();
  }

  private void createUsb() {
    int token = ++usbGeneration;
    restoredMode = false;
    usb =
        new UsbCapture(
            new UsbCapture.Listener() {
              public void attached(UsbDevice device) {
                main.post(
                    () -> {
                      if (token == usbGeneration
                          && !closed
                          && foreground
                          && wantLive
                          && usb != null
                          && (selectedUsb.isEmpty() || selectedUsb.equals(usbKey(device))))
                        usb.select(device);
                    });
              }

              public void opened(Size mode) {
                main.post(
                    () -> {
                      if (closed || token != usbGeneration || usb == null) return;
                      captureSize = mode;
                      usbFrameReady = false;
                      live = true;
                      showSourceChooser(false);
                      if (!restoredMode) {
                        restoredMode = true;
                        String[] saved = store.text("usbMode", "").split(",");
                        if (saved.length == 4)
                          try {
                            for (Size candidate : usb.modes())
                              if (candidate.width == Integer.parseInt(saved[0])
                                  && candidate.height == Integer.parseInt(saved[1])
                                  && candidate.type == Integer.parseInt(saved[3])) {
                                Size restore = candidate.clone();
                                int fps = Integer.parseInt(saved[2]);
                                if (restore.fpsList != null && restore.fpsList.contains(fps))
                                  restore.fps = fps;
                                captureSize = restore;
                                usb.mode(restore);
                                break;
                              }
                          } catch (NumberFormatException ignored) {
                          }
                      }
                      usbView.setVisibility(View.VISIBLE);
                      playerView.setVisibility(View.GONE);
                      if (usbSurface != null) usb.surface(usbSurface);
                      fitCapture();
                      message(
                          mode == null
                              ? "USB preview connected"
                              : "LIVE · "
                                  + mode.width
                                  + " × "
                                  + mode.height
                                  + " · "
                                  + mode.fps
                                  + " fps");
                      restoreUsbAudio();
                    });
              }

              public void disconnected() {
                main.post(
                    () -> {
                      if (closed || token != usbGeneration) return;
                      live = false;
                      showSourceChooser(true);
                      finishCaptureRecording();
                      if (audioCapturing && !microphoneSelected()) cancelAudio();
                      closeUsbAudio();
                      message("USB disconnected. Reconnect the selected capture device.");
                    });
              }

              public void error(String value) {
                if (!closed && token == usbGeneration) message(value);
              }
            });
  }

  private void startUsb() {
    if (usbRetiring) return;
    if (usb == null) createUsb();
    usbView.setVisibility(View.VISIBLE);
    for (UsbDevice device : usb.devices())
      if (selectedUsb.isEmpty() || selectedUsb.equals(usbKey(device))) {
        usb.select(device);
        return;
      }
    message("Waiting for saved USB capture device.");
  }

  private void stopUsb() {
    ++usbGeneration;
    live = false;
    usbFrameReady = false;
    if (usb != null) {
      UsbCapture old = usb;
      usb = null;
      usbRetiring = true;
      io.execute(
          () -> {
            old.close();
            main.post(
                () -> {
                  usbRetiring = false;
                  if (!closed && foreground && wantLive && usb == null) startUsb();
                });
          });
    }
    closeUsbAudio();
  }

  private void chooseMode() {
    if (usb == null || !usb.isOpen()) {
      message("Open a USB capture device first.");
      return;
    }
    if (sourceLocked()) return;
    List<Size> modes = usb.modes();
    String[] labels =
        modes.stream()
            .map(s -> s.width + " × " + s.height + " · " + s.fps + " fps · format " + s.type)
            .toArray(String[]::new);
    new android.app.AlertDialog.Builder(this)
        .setTitle("Advertised USB capture modes")
        .setItems(
            labels,
            (d, index) -> {
              Size size = modes.get(index);
              List<Integer> rates = size.fpsList == null ? List.of(size.fps) : size.fpsList;
              String[] fps = rates.stream().map(v -> v + " fps").toArray(String[]::new);
              new android.app.AlertDialog.Builder(this)
                  .setTitle("Frame rate")
                  .setItems(
                      fps,
                      (dialog, f) -> {
                        Size selectedMode = size.clone();
                        selectedMode.fps = rates.get(f);
                        captureSize = selectedMode;
                        usb.mode(selectedMode);
                        store.put(
                            "usbMode",
                            selectedMode.width
                                + ","
                                + selectedMode.height
                                + ","
                                + selectedMode.fps
                                + ","
                                + selectedMode.type);
                        fitCapture();
                      })
                  .show();
            })
        .show();
  }

  private void fitCapture() {
    if (captureSize == null || videoLayer == null || usbView == null) return;
    int width = videoLayer.getWidth(), height = videoLayer.getHeight();
    if (width <= 0 || height <= 0) return;
    int[] size =
        PictureMode.read(store).dimensions(captureSize.width, captureSize.height, width, height);
    FrameLayout.LayoutParams p = new FrameLayout.LayoutParams(size[0], size[1], Gravity.CENTER);
    if (usbView.getLayoutParams().width != p.width || usbView.getLayoutParams().height != p.height)
      usbView.setLayoutParams(p);
  }

  private void choosePictureMode() {
    new android.app.AlertDialog.Builder(this)
        .setTitle("Picture mode")
        .setSingleChoiceItems(
            new String[] {
              "Regular · whole picture",
              "Fill · crop edges",
              "Stretch · fill without preserving proportions"
            },
            PictureMode.read(store).ordinal(),
            (dialog, which) -> {
              setPictureMode(PictureMode.values()[which]);
              dialog.dismiss();
            })
        .setNegativeButton("Cancel", null)
        .show();
  }

  private void setPictureMode(PictureMode mode) {
    store.put("pictureMode", mode.name());
    store.put("fill", Boolean.toString(mode == PictureMode.FILL));
    playerView.setResizeMode(mode.resizeMode);
    fitCapture();
    updateToggleStates();
  }

  private void chooseAudio() {
    if (sourceLocked()) return;
    requestMic(
        ok -> {
          if (!ok) return;
          AudioManager manager = (AudioManager) getSystemService(AUDIO_SERVICE);
          List<AudioDeviceInfo> devices = new ArrayList<>();
          for (AudioDeviceInfo device : manager.getDevices(AudioManager.GET_DEVICES_INPUTS))
            if (device.getType() == AudioDeviceInfo.TYPE_USB_DEVICE
                || device.getType() == AudioDeviceInfo.TYPE_USB_HEADSET) devices.add(device);
          String[] labels = new String[devices.size() + 1];
          labels[0] = "No source audio (silent)";
          for (int i = 0; i < devices.size(); i++)
            labels[i + 1] = devices.get(i).getProductName().toString();
          new android.app.AlertDialog.Builder(this)
              .setTitle("USB source audio · microphone fallback disabled")
              .setItems(
                  labels,
                  (d, index) -> {
                    usbAudioDevice = index == 0 ? null : devices.get(index - 1);
                    store.put(
                        "usbAudio",
                        usbAudioDevice == null ? "" : usbAudioDevice.getProductName().toString());
                    closeUsbAudio();
                    if (usbAudioDevice != null) openUsbAudio();
                  })
              .show();
        });
  }

  private void restoreUsbAudio() {
    if (!live
        || browserUsingMicrophone
        || audioCapturing && capturingMic
        || checkSelfPermission(Manifest.permission.RECORD_AUDIO)
            != PackageManager.PERMISSION_GRANTED) return;
    AudioManager manager = (AudioManager) getSystemService(AUDIO_SERVICE);
    String saved = store.text("usbAudio", "");
    for (AudioDeviceInfo device : manager.getDevices(AudioManager.GET_DEVICES_INPUTS))
      if ((device.getType() == AudioDeviceInfo.TYPE_USB_DEVICE
              || device.getType() == AudioDeviceInfo.TYPE_USB_HEADSET)
          && device.getProductName().toString().equals(saved)) {
        usbAudioDevice = device;
        openUsbAudio();
        return;
      }
  }

  private void openUsbAudio() {
    if (!live
        || browserUsingMicrophone
        || audioCapturing && capturingMic
        || usbAudioDevice == null
        || usbAudio != null) return;
    AudioInput input = new AudioInput();
    usbAudio = input;
    AudioDeviceInfo device = usbAudioDevice;
    io.execute(
        () -> {
          try {
            if (usbAudio != input || !foreground) {
              input.close();
              return;
            }
            input.start(
                this,
                device,
                false,
                new AudioInput.Listener() {
                  public void pcm(short[] data, int count) {
                    if (usbAudio != input) return;
                    AudioInput.Samples target = samples;
                    if (audioCapturing && !capturingMic && target != null) target.add(data, count);
                    CaptureRecorder recording = recorder;
                    if (recording != null) recording.audio(data, count);
                  }

                  public void failed(String value) {
                    main.post(
                        () -> {
                          closeUsbAudio();
                          finishCaptureRecording();
                          if (audioCapturing && !capturingMic) cancelAudio();
                          message(value);
                        });
                  }
                });
            input.monitor(true, videoVolume);
          } catch (Exception error) {
            message("USB audio: " + error.getMessage());
            if (usbAudio == input) usbAudio = null;
            input.close();
          }
        });
  }

  private void closeUsbAudio() {
    AudioInput old = usbAudio;
    usbAudio = null;
    if (old != null) io.execute(old::close);
  }

  private void chooseFileAudio() {
    if (player == null || live) return;
    List<Runnable> choices = new ArrayList<>();
    List<String> names = new ArrayList<>();
    for (Tracks.Group group : player.getCurrentTracks().getGroups())
      if (group.getType() == C.TRACK_TYPE_AUDIO)
        for (int i = 0; i < group.length; i++) {
          final int index = i;
          Format format = group.getTrackFormat(i);
          names.add(
              (group.isTrackSelected(i) ? "✓ " : "")
                  + (format.label != null
                      ? format.label
                      : format.language != null ? format.language : "Audio " + (i + 1)));
          choices.add(
              () ->
                  player.setTrackSelectionParameters(
                      player
                          .getTrackSelectionParameters()
                          .buildUpon()
                          .setOverrideForType(
                              new TrackSelectionOverride(group.getMediaTrackGroup(), index))
                          .build()));
        }
    new android.app.AlertDialog.Builder(this)
        .setTitle("Audio track")
        .setItems(names.toArray(new String[0]), (d, i) -> choices.get(i).run())
        .show();
  }

  private void licenses() {
    io.execute(
        () -> {
          try {
            List<String> files = new ArrayList<>();
            files.add("THIRD_PARTY_NOTICES.txt");
            for (String asset : getAssets().list(""))
              if (asset.startsWith("LICENSE.")) files.add(asset);
            main.post(
                () -> {
                  if (closed) return;
                  new android.app.AlertDialog.Builder(this)
                      .setTitle("Open-source notices")
                      .setItems(
                          files.toArray(new String[0]),
                          (dialog, index) -> showLicense(files.get(index)))
                      .show();
                });
          } catch (IOException error) {
            message("Notices: " + error.getMessage());
          }
        });
  }

  private void showLicense(String asset) {
    io.execute(
        () -> {
          try (InputStream input = getAssets().open(asset);
              ByteArrayOutputStream text = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.read(buffer)) != -1) text.write(buffer, 0, count);
            String value = text.toString("UTF-8");
            main.post(
                () -> {
                  if (!closed)
                    new android.app.AlertDialog.Builder(this)
                        .setTitle(asset)
                        .setMessage(value)
                        .setPositiveButton("Close", null)
                        .show();
                });
          } catch (IOException error) {
            message("Notices: " + error.getMessage());
          }
        });
  }

  private void chooseSubtitles() {
    if (player == null || live) {
      message("Play a local file to choose embedded subtitles.");
      return;
    }
    List<Runnable> choices = new ArrayList<>();
    List<String> names = new ArrayList<>();
    names.add("Off (use frame OCR when reading)");
    choices.add(
        () ->
            player.setTrackSelectionParameters(
                player
                    .getTrackSelectionParameters()
                    .buildUpon()
                    .clearOverridesOfType(C.TRACK_TYPE_TEXT)
                    .setTrackTypeDisabled(C.TRACK_TYPE_TEXT, true)
                    .build()));
    for (Tracks.Group group : player.getCurrentTracks().getGroups()) {
      if (group.getType() != C.TRACK_TYPE_TEXT) continue;
      for (int i = 0; i < group.length; i++) {
        final int index = i;
        Format format = group.getTrackFormat(i);
        names.add(
            (group.isTrackSelected(i) ? "✓ " : "")
                + (format.label != null
                    ? format.label
                    : format.language != null ? format.language : "Subtitle " + (i + 1))
                + (group.isTrackSupported(i) ? "" : " (unsupported)"));
        choices.add(
            () ->
                player.setTrackSelectionParameters(
                    player
                        .getTrackSelectionParameters()
                        .buildUpon()
                        .setTrackTypeDisabled(C.TRACK_TYPE_TEXT, false)
                        .setOverrideForType(
                            new TrackSelectionOverride(group.getMediaTrackGroup(), index))
                        .build()));
      }
    }
    new android.app.AlertDialog.Builder(this)
        .setTitle("Embedded subtitles")
        .setItems(names.toArray(new String[0]), (d, i) -> choices.get(i).run())
        .show();
  }

  private void setRecordingLabel(String label) {
    recordButton.setText("");
    PlayerSkin.icon(
        recordButton,
        label.startsWith("■") ? PlayerSkin.Icon.Kind.STOP : PlayerSkin.Icon.Kind.RECORD,
        20);
    recordButton.setContentDescription(label);
    recordButton.setTooltipText(label);
    updateToggleStates();
  }

  private void record() {
    if (recorder != null) {
      finishCaptureRecording();
      return;
    }
    if (pendingLocalClip != null) {
      finishLocalClip();
      return;
    }
    if (browserUsingMicrophone) {
      message("Finish browser voice capture before starting a recording.");
      return;
    }
    if (audioCapturing || busy) {
      message("Finish audio capture before recording video.");
      return;
    }
    if (live && usb != null && usb.isOpen()) {
      busy = true;
      File output = exportFile("Capture", "mp4");
      Size mode = captureSize;
      if (mode == null) {
        busy = false;
        return;
      }
      UsbCapture source = usb;
      boolean audio = usbAudio != null && usbAudio.active();
      io.execute(
          () -> {
            try {
              CaptureRecorder recording =
                  new CaptureRecorder(
                      source,
                      output,
                      mode.width,
                      mode.height,
                      mode.fps,
                      audio,
                      new CaptureRecorder.Result() {
                        public void complete(File file) {
                          saveVideo(file);
                        }

                        public void error(String error) {
                          message(error);
                          main.post(
                              () -> {
                                if (recorder != null) finishCaptureRecording();
                              });
                        }
                      });
              boolean accepted;
              synchronized (recordingGate) {
                accepted = foreground && !closed;
                if (accepted) recorder = recording;
              }
              if (!accepted) {
                main.post(() -> busy = false);
                recording.close();
                return;
              }
              main.post(
                  () -> {
                    busy = false;
                    if (closed || recorder != recording) return;
                    setRecordingLabel("■ Stop recording");
                    message(
                        audio
                            ? "Recording USB video and selected USB audio."
                            : "Recording video only. Select a USB audio input for source sound.");
                  });
            } catch (Exception error) {
              main.post(
                  () -> {
                    busy = false;
                    message("Encoder: " + error.getMessage());
                  });
            }
          });
    } else if (player != null && fileUri != null && player.isPlaying()) {
      pendingLocalClip = exportFile("Clip", "mp4");
      localClipStart = player.getCurrentPosition();
      setRecordingLabel("■ Stop recording");
      message("Marking a local video segment. Stop recording to export it.");
    } else message("Play a video or connect capture before recording.");
  }

  private File exportFile(String prefix, String extension) {
    File directory = new File(getFilesDir(), "exports");
    if (!directory.exists()) directory.mkdirs();
    return new File(
        directory,
        prefix + "-" + System.currentTimeMillis() + "-" + UUID.randomUUID() + "." + extension);
  }

  private void finishCaptureRecording() {
    CaptureRecorder recording;
    synchronized (recordingGate) {
      recording = recorder;
      recorder = null;
    }
    if (recording != null) {
      setRecordingLabel("Saving…");
      io.execute(
          () -> {
            recording.close();
            main.post(
                () -> {
                  if (!closed) setRecordingLabel("● Record");
                });
          });
    }
  }

  private void finishLocalClip() {
    File output = pendingLocalClip;
    pendingLocalClip = null;
    if (output == null || fileUri == null || player == null) return;
    long end = player.getCurrentPosition();
    if (end - localClipStart < 250) {
      message("Record a longer passage before saving.");
      setRecordingLabel("● Record");
      return;
    }
    busy = true;
    MediaItem item =
        new MediaItem.Builder()
            .setUri(fileUri)
            .setClippingConfiguration(
                new MediaItem.ClippingConfiguration.Builder()
                    .setStartPositionMs(localClipStart)
                    .setEndPositionMs(end)
                    .build())
            .build();
    clipExportFile = output;
    clipExport =
        new Transformer.Builder(this)
            .addListener(
                new Transformer.Listener() {
                  @Override
                  public void onCompleted(Composition composition, ExportResult result) {
                    clipExport = null;
                    clipExportFile = null;
                    if (closed) {
                      output.delete();
                      return;
                    }
                    busy = false;
                    setRecordingLabel("● Record");
                    saveVideo(output);
                  }

                  @Override
                  public void onError(
                      Composition composition, ExportResult result, ExportException error) {
                    clipExport = null;
                    clipExportFile = null;
                    if (closed) {
                      output.delete();
                      return;
                    }
                    busy = false;
                    setRecordingLabel("● Record");
                    output.delete();
                    message("Clip export: " + error.getMessage());
                  }
                })
            .build();
    try {
      clipExport.start(new EditedMediaItem.Builder(item).build(), output.getAbsolutePath());
    } catch (Exception error) {
      clipExport = null;
      clipExportFile = null;
      output.delete();
      busy = false;
      setRecordingLabel("● Record");
      message("Clip export: " + error.getMessage());
    }
  }

  private void saveVideo(File file) {
    MediaExports.save(
        this,
        file,
        (uri, error) ->
            main.post(
                () -> {
                  if (closed) return;
                  if (uri != null) {
                    lastRecording = uri;
                    message("Saved to Movies/VideoCapture.");
                  } else message(error);
                }));
  }

  private void copyRecording() {
    if (lastRecording == null) {
      message("Finish a recording first.");
      return;
    }
    ((ClipboardManager) getSystemService(CLIPBOARD_SERVICE))
        .setPrimaryClip(
            ClipData.newUri(getContentResolver(), "VideoCapture recording", lastRecording));
    message("Recording copied to clipboard.");
  }

  private TextureView captureTexture() {
    return live
        ? usbView
        : playerView.getVideoSurfaceView() instanceof TextureView
            ? (TextureView) playerView.getVideoSurfaceView()
            : null;
  }

  private boolean frameReady() {
    return live
        ? usb != null && usbFrameReady && usbView.getVisibility() == View.VISIBLE
        : player != null
            && fileUri != null
            && fileFrameReady
            && playerView.getVisibility() == View.VISIBLE
            && !busy
            && player.getPlayerError() == null;
  }

  private Bitmap snapshot(boolean full) {
    if (!frameReady()) return null;
    TextureView texture = captureTexture();
    if (texture == null || !texture.isAvailable()) return null;
    int[] size = captureDimensions(texture, full);
    return size == null ? null : FrameCapture.read(texture, size[0], size[1]);
  }

  private int[] captureDimensions(TextureView texture, boolean full) {
    int width = texture.getWidth(), height = texture.getHeight();
    if (full && live && captureSize != null) {
      width = captureSize.width;
      height = captureSize.height;
    } else if (full && player != null && player.getVideoSize().width > 0) {
      width = player.getVideoSize().width;
      height = player.getVideoSize().height;
    }
    if (!full && width > 1280) {
      height = Math.max(1, height * 1280 / width);
      width = 1280;
    }
    if (width <= 0 || height <= 0) return null;
    if ((long) width * height > 8294400L) {
      double scale = Math.sqrt(8294400d / ((long) width * height));
      width = Math.max(1, (int) (width * scale));
      height = Math.max(1, (int) (height * scale));
    }
    return new int[] {width, height};
  }

  private void frame(boolean save) {
    if (frameBusy) {
      message("A frame capture is already in progress.");
      return;
    }
    TextureView texture = captureTexture();
    if (!frameReady() || texture == null || !texture.isAvailable()) {
      message("No video picture is ready. Start playback or USB preview before capturing.");
      return;
    }
    int[] size = captureDimensions(texture, true);
    if (size == null) {
      message("The video picture is not available yet.");
      return;
    }
    int fileToken = generation, usbToken = usbGeneration;
    boolean wasLive = live;
    frameBusy = true;
    FrameCapture.capture(
        texture,
        size[0],
        size[1],
        main,
        image -> {
          if (closed
              || !foreground
              || wasLive != live
              || fileToken != generation
              || usbToken != usbGeneration) {
            if (image != null) image.recycle();
            frameBusy = false;
            return;
          }
          if (image == null) {
            frameBusy = false;
            message(
                "Video frame readback failed. Clipboard unchanged; wait for a picture and try"
                    + " again.");
            return;
          }
          saveCapturedFrame(image, save);
        });
  }

  private void saveCapturedFrame(Bitmap image, boolean save) {
    io.execute(
        () -> {
          try {
            Uri uri = GalleryFrames.save(getApplicationContext(), image);
            main.post(
                () -> {
                  if (closed) return;
                  if (!save) {
                    try {
                      GalleryFrames.copy(MainActivity.this, uri);
                    } catch (RuntimeException error) {
                      message(
                          "Frame saved to Gallery, but clipboard copy failed: "
                              + error.getMessage());
                      return;
                    }
                  }
                  message(
                      save
                          ? "Frame saved to Pictures/VideoCapture."
                          : "Frame saved to Gallery and copied. Paste it into the browser.");
                });
          } catch (Exception error) {
            message("Frame capture: " + error.getMessage());
          } finally {
            image.recycle();
            main.post(() -> frameBusy = false);
          }
        });
  }

  private void recognize(boolean subtitle) {
    if (ocrBusy) return;
    Bitmap image = snapshot(false);
    if (image == null) {
      if (!subtitle) message("Play a video before copying text.");
      return;
    }
    if (!ocrConsent.ready(
        !subtitle,
        () -> {
          if (!closed && foreground) recognize(subtitle);
        })) {
      image.recycle();
      return;
    }
    Bitmap input =
        subtitle
            ? Bitmap.createBitmap(
                image,
                0,
                image.getHeight() * 3 / 5,
                image.getWidth(),
                image.getHeight() - image.getHeight() * 3 / 5)
            : image;
    ocrBusy = true;
    if (ocr == null) ocr = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
    ocr.process(InputImage.fromBitmap(input, 0))
        .addOnSuccessListener(
            result -> {
              if (closed || !foreground) return;
              String text = result.getText();
              if (!text.isBlank()) {
                if (subtitle) speak(text);
                else {
                  ((ClipboardManager) getSystemService(CLIPBOARD_SERVICE))
                      .setPrimaryClip(ClipData.newPlainText("Video text", text));
                  message("Recognized text copied. Check OCR for errors.");
                }
              } else if (!subtitle) message("No readable text found. Clipboard kept.");
            })
        .addOnFailureListener(error -> message("OCR: " + error.getMessage()))
        .addOnCompleteListener(
            task -> {
              ocrBusy = false;
              if (input != image) input.recycle();
              image.recycle();
            });
  }

  private void recognizeSubtitleBitmap(Bitmap source) {
    if (!ocrConsent.ready(
        false,
        () -> {
          if (!closed && reading && player != null)
            for (Cue cue : player.getCurrentCues().cues)
              if (cue.bitmap != null && !cue.bitmap.isRecycled()) {
                recognizeSubtitleBitmap(cue.bitmap);
                break;
              }
        })) return;
    if (ocrBusy || !foreground) return;
    Bitmap image = source.copy(Bitmap.Config.ARGB_8888, false);
    int token = generation;
    ocrBusy = true;
    if (ocr == null) ocr = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
    ocr.process(InputImage.fromBitmap(image, 0))
        .addOnSuccessListener(
            result -> {
              if (!closed && foreground && reading && token == generation) speak(result.getText());
            })
        .addOnCompleteListener(
            task -> {
              ocrBusy = false;
              image.recycle();
            });
  }

  private void toggleReading() {
    reading = !reading;
    store.put("reading", Boolean.toString(reading));
    if (reading) {
      ensureSpeaker();
      message(
          "Subtitle reading enabled. Embedded cues are preferred; USB/burned-in subtitles use"
              + " offline OCR.");
    } else if (speaker != null) {
      speaker.stop();
      lastRead = "";
    }
  }

  private void ensureSpeaker() {
    if (speaker != null) return;
    speaker =
        new TextToSpeech(
            this,
            result -> {
              if (result != TextToSpeech.SUCCESS) {
                message("Text-to-speech engine unavailable.");
                return;
              }
              Set<Voice> voices = speaker.getVoices();
              if (voices == null) {
                message("No offline reading voices installed.");
                return;
              }
              String saved = store.text("voice", "");
              Voice chosen = null;
              for (Voice voice : voices)
                if (!voice.isNetworkConnectionRequired()) {
                  if (voice.getName().equals(saved)) {
                    chosen = voice;
                    break;
                  }
                  if (chosen == null
                      || voice.getLocale().getLanguage().equals(Locale.getDefault().getLanguage()))
                    chosen = voice;
                }
              if (chosen == null) {
                message("Install an offline voice in Android text-to-speech settings.");
                return;
              }
              speaker.setVoice(chosen);
              ttsReady = true;
            });
  }

  private void speak(String value) {
    String text = value.replaceAll("\\s+", " ").trim();
    if (!reading
        || !foreground
        || audioCapturing && capturingMic
        || browserUsingMicrophone
        || text.isEmpty()
        || text.equals(lastRead)) return;
    ensureSpeaker();
    if (!ttsReady) return;
    lastRead = text;
    Bundle params = new Bundle();
    params.putFloat(TextToSpeech.Engine.KEY_PARAM_VOLUME, readingVolume);
    speaker.speak(text, TextToSpeech.QUEUE_FLUSH, params, "subtitle");
  }

  private void chooseVoice() {
    ensureSpeaker();
    if (!ttsReady) {
      message("Wait for the offline speech engine, then select Voice again.");
      return;
    }
    List<Voice> voices = new ArrayList<>();
    for (Voice voice : speaker.getVoices())
      if (!voice.isNetworkConnectionRequired()) voices.add(voice);
    new android.app.AlertDialog.Builder(this)
        .setTitle("Installed offline voices")
        .setItems(
            voices.stream()
                .map(v -> v.getLocale().getDisplayName() + " · " + v.getName())
                .toArray(String[]::new),
            (d, index) -> {
              speaker.stop();
              speaker.setVoice(voices.get(index));
              store.put("voice", voices.get(index).getName());
              lastRead = "";
            })
        .setNeutralButton(
            "Settings", (d, w) -> startActivity(new Intent("com.android.settings.TTS_SETTINGS")))
        .show();
  }

  private void toggleBrowserPanel() {
    if (clearingBrowserData) {
      message("Browser data is being cleared. Please wait.");
      return;
    }
    if (browser == null) {
      browser =
          new BrowserOverlay(
              this,
              store,
              new BrowserOverlay.Host() {
                public void stateChanged() {
                  updateToggleStates();
                }

                public boolean chooseFiles(Intent intent, Consumer<Uri[]> result) {
                  if (closed || browserFilesResult != null) return false;
                  browserFilesResult = result;
                  try {
                    browserFiles.launch(intent);
                    return true;
                  } catch (android.content.ActivityNotFoundException
                      | SecurityException
                      | IllegalStateException error) {
                    browserFilesResult = null;
                    return false;
                  }
                }

                public void permission(Consumer<Boolean> result) {
                  prepareBrowserMicrophone(result);
                }

                public void microphoneReleased() {
                  if (closed || !browserUsingMicrophone) return;
                  main.removeCallbacks(restoreBrowserAudio);
                  main.postDelayed(restoreBrowserAudio, 500);
                }

                public void feedback(String value) {
                  message(value);
                }

                public void frame() {
                  MainActivity.this.frame(false);
                }

                public void record() {
                  MainActivity.this.record();
                }

                public void ocr() {
                  recognize(false);
                }

                public void audio() {
                  toggleAudioPanel();
                }
              });
      root.addView(browser, new FrameLayout.LayoutParams(1, 1));
      browser.layoutWithinParent();
    }
    browser.toggle();
  }

  private void toggleAudioPanel() {
    if (browser == null) toggleBrowserPanel();
    else if (browser.getVisibility() != View.VISIBLE) browser.toggle();
    if (audioPanel == null) buildAudioPanel();
    audioPanel.setVisibility(audioPanel.getVisibility() == View.VISIBLE ? View.GONE : View.VISIBLE);
    updateToggleStates();
  }

  private void buildAudioPanel() {
    audioPanel = Ui.column(this);
    audioPanel.setPadding(dp(4), dp(6), dp(4), dp(4));
    audioPanel.setBackground(Ui.panel(0xff101923, dp(8)));
    LinearLayout row = Ui.row(this);
    audioSource = new Spinner(this);
    audioSource.setAdapter(
        new ArrayAdapter<>(
            this,
            android.R.layout.simple_spinner_dropdown_item,
            new String[] {"Video audio", "Microphone"}));
    audioSource.setSelection(
        "microphone".equals(store.text("transcriptionSource", "video")) ? 1 : 0);
    row.addView(audioSource, new LinearLayout.LayoutParams(dp(160), dp(48)));
    audioStart = Ui.button(this, "Start capture", this::toggleAudio);
    row.addView(audioStart);
    row.addView(Ui.button(this, "Cancel", this::cancelAudio));
    row.addView(
        Ui.button(
            this,
            "Copy",
            () -> {
              if (!transcript.getText().toString().isBlank()) {
                ((ClipboardManager) getSystemService(CLIPBOARD_SERVICE))
                    .setPrimaryClip(ClipData.newPlainText("Transcript", transcript.getText()));
                message("Transcript copied.");
              }
            }));
    audioPanel.addView(Ui.scrollRow(this, row));
    LinearLayout settings = Ui.row(this);
    settings.addView(Ui.text(this, "Whisper · offline · English", 11, 0xff91a0b7));
    settings.addView(
        Ui.button(
            this,
            "Language",
            () ->
                new android.app.AlertDialog.Builder(this)
                    .setTitle("Transcription language")
                    .setItems(
                        new String[] {
                          "Auto detect",
                          "English",
                          "Filipino / Tagalog",
                          "Spanish",
                          "French",
                          "Chinese",
                          "Japanese"
                        },
                        (d, index) ->
                            store.put(
                                "speechLanguage",
                                new String[] {"auto", "en", "tl", "es", "fr", "zh", "ja"}[index]))
                    .show()));
    levelMeter = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
    levelMeter.setMax(1000);
    levelMeter.setProgressTintList(android.content.res.ColorStateList.valueOf(0xff5ce0c1));
    settings.addView(levelMeter, new LinearLayout.LayoutParams(0, dp(25), 1));
    audioPanel.addView(settings);
    transcript = Ui.input(this, "Review final transcript, then copy");
    transcript.setMinLines(2);
    transcript.setMaxLines(3);
    audioPanel.addView(transcript, new LinearLayout.LayoutParams(-1, dp(70)));
    transcriptStatus =
        Ui.text(
            this,
            "Offline model included. Final text appears after Stop & transcribe.",
            10,
            0xff91a0b7);
    audioPanel.addView(transcriptStatus);
    browser.setTranscriptPanel(audioPanel);
    audioPanel.setVisibility(View.GONE);
  }

  private boolean microphoneSelected() {
    return audioSource != null && audioSource.getSelectedItemPosition() == 1;
  }

  private void toggleAudio() {
    if (audioCapturing) {
      finishAudio();
      return;
    }
    if (browserUsingMicrophone) {
      message("Finish browser voice capture before starting app Audio to text.");
      return;
    }
    if (transcribing || recorder != null || pendingLocalClip != null || busy) {
      message("Finish the current capture or recording first.");
      return;
    }
    requestMic(
        ok -> {
          if (!ok) return;
          boolean mic = microphoneSelected();
          capturingMic = mic;
          store.put("transcriptionSource", mic ? "microphone" : "video");
          if (!mic && !live && (player == null || fileUri == null || !player.isPlaying())) {
            message("Play a video with audio before capturing source speech.");
            return;
          }
          if (!mic && live && (usbAudio == null || !usbAudio.active())) {
            message("Select the USB source audio input first.");
            return;
          }
          samples = new AudioInput.Samples();
          audioJob++;
          audioCapturing = true;
          audioStarted = SystemClock.elapsedRealtime();
          audioFileStart = player == null ? 0 : player.getCurrentPosition();
          audioSource.setEnabled(false);
          audioStart.setText("Stop & transcribe");
          transcript.setEnabled(false);
          transcriptStatus.setText(
              mic
                  ? "Listening to device microphone. Final transcription after Stop."
                  : "Capturing only video source audio.");
          if (mic) {
            if (player != null) player.setVolume(0);
            if (speaker != null) speaker.stop();
            closeUsbAudio();
            AudioManager manager = (AudioManager) getSystemService(AUDIO_SERVICE);
            AudioDeviceInfo preferred = null;
            for (AudioDeviceInfo device : manager.getDevices(AudioManager.GET_DEVICES_INPUTS))
              if (device.getType() == AudioDeviceInfo.TYPE_BUILTIN_MIC) {
                preferred = device;
                break;
              }
            AudioDeviceInfo selectedDevice = preferred;
            AudioInput input = new AudioInput();
            microphone = input;
            io.execute(
                () -> {
                  try {
                    if (!audioCapturing || microphone != input || !foreground) {
                      input.close();
                      return;
                    }
                    input.start(
                        this,
                        selectedDevice,
                        true,
                        new AudioInput.Listener() {
                          public void pcm(short[] data, int count) {
                            if (microphone != input) return;
                            AudioInput.Samples target = samples;
                            if (target != null) target.add(data, count);
                            int peak = 0;
                            for (int i = 0; i < count; i++)
                              peak = Math.max(peak, Math.abs((int) data[i]));
                            final int level = Math.min(1000, peak * 1000 / 32768);
                            main.post(
                                () -> {
                                  if (audioCapturing && levelMeter != null)
                                    levelMeter.setProgress(level);
                                });
                          }

                          public void failed(String value) {
                            main.post(
                                () -> {
                                  cancelAudio();
                                  message(value);
                                });
                          }
                        });
                  } catch (Exception error) {
                    main.post(
                        () -> {
                          cancelAudio();
                          message("Microphone: " + error.getMessage());
                        });
                  }
                });
          }
        });
  }

  private void finishAudio() {
    if (!audioCapturing) return;
    audioCapturing = false;
    if (player != null) player.setVolume(browserUsingMicrophone ? 0 : videoVolume);
    transcribing = true;
    audioStart.setText("Transcribing…");
    audioStart.setEnabled(false);
    transcriptStatus.setText("Transcribing locally with Whisper…");
    boolean mic = microphoneSelected();
    AudioInput input = microphone;
    microphone = null;
    AudioInput.Samples captured = samples;
    samples = null;
    Uri source = fileUri;
    long start = audioFileStart, end = player == null ? 0 : player.getCurrentPosition();
    boolean usbSource = live;
    int audioOrdinal = selectedAudioOrdinal();
    final int job = ++audioJob;
    AtomicBoolean cancelled = new AtomicBoolean();
    decoderCancelled = cancelled;
    io.execute(
        () -> {
          if (input != null) input.close();
          try {
            short[] data =
                mic || usbSource
                    ? captured.array()
                    : AudioDecoder.range(this, source, start, end, cancelled, audioOrdinal);
            if (cancelled.get()) return;
            speech.transcribe(
                data,
                store.text("speechLanguage", "en"),
                new OfflineSpeech.Result() {
                  public void complete(String value) {
                    if (job != audioJob) return;
                    if (!value.isBlank()) transcript.setText(value);
                    finishAudioUi(
                        value.isBlank()
                            ? "No clear speech found. Previous transcript kept."
                            : "Ready. Review recognition before copying.");
                  }

                  public void error(String value) {
                    if (job != audioJob) return;
                    finishAudioUi(value);
                  }
                });
          } catch (Exception error) {
            main.post(
                () -> {
                  if (job == audioJob) finishAudioUi("Audio: " + error.getMessage());
                });
          } finally {
            main.post(
                () -> {
                  if (foreground && mic && job == audioJob) restoreUsbAudio();
                });
          }
        });
  }

  private int selectedAudioOrdinal() {
    if (player == null) return 0;
    int ordinal = 0;
    for (Tracks.Group group : player.getCurrentTracks().getGroups())
      if (group.getType() == C.TRACK_TYPE_AUDIO)
        for (int i = 0; i < group.length; i++) {
          if (group.isTrackSelected(i)) return ordinal;
          ordinal++;
        }
    return 0;
  }

  private void finishAudioUi(String value) {
    if (closed) return;
    transcribing = false;
    audioStart.setEnabled(true);
    audioStart.setText("Start capture");
    audioSource.setEnabled(true);
    transcript.setEnabled(true);
    transcriptStatus.setText(value);
    levelMeter.setProgress(0);
  }

  private void cancelAudio() {
    audioJob++;
    audioCapturing = false;
    if (player != null) player.setVolume(browserUsingMicrophone ? 0 : videoVolume);
    decoderCancelled.set(true);
    speech.cancel();
    AudioInput input = microphone;
    microphone = null;
    samples = null;
    if (input != null) io.execute(input::close);
    if (audioPanel != null) finishAudioUi("Cancelled. Previous transcript and clipboard kept.");
    if (foreground) restoreUsbAudio();
  }

  private void showPrivacy() {
    TextView content =
        Ui.text(
            this,
            """
            Media and files
            You choose individual files or folders using Android's picker. Folder access is limited to the selected tree. Playlist entries, playback positions, settings and browser tab addresses are kept in app storage. Removing a playlist item does not delete its file.

            Captures and speech
            Saved images/recordings remain in Pictures/VideoCapture and Movies/VideoCapture until you delete them in Gallery or Files. Copy frame also saves an image. Clipboard contents are readable by paste targets you choose. App audio transcription uses the bundled offline Whisper model. Website voice audio is handled separately by the website.

            OCR SDK
            OCR images/text are processed on-device. Google's ML Kit SDK collects technical device/app information, installation identifiers and diagnostic/performance metrics that may be transmitted to Google when online. OCR requires a separate disclosure and agreement before SDK initialization. Google documents this at developers.google.com/ml-kit/android-data-disclosure.

            Browser
            Websites receive information you enter, upload or transmit using their microphone feature, under their own policies. Cookies/site storage retain logins. Microphone access is restricted to the exact ChatGPT origins and is stopped when the browser is hidden or its tab changes. Google sign-in opens an external browser; sessions are not copied between browsers.

            Your controls
            Deny/revoke camera or microphone access in Android settings. Use Clear browser data below to sign out and erase browser storage. Clear Android app storage or uninstall to remove local app preferences/history; Gallery media stays until you delete it separately. VideoCapture has no native user account, advertising or developer-operated upload service. The public publisher privacy policy and contact details must be completed before Play Store release.
            """,
            13,
            PlayerSkin.TEXT);
    content.setPadding(dp(20), dp(8), dp(20), dp(8));
    ScrollView scroll = new ScrollView(this);
    scroll.addView(content);
    new android.app.AlertDialog.Builder(this)
        .setTitle("Privacy & data")
        .setView(scroll)
        .setPositiveButton("Close", null)
        .setNeutralButton(
            "Clear browser data",
            (dialog, which) ->
                new android.app.AlertDialog.Builder(this)
                    .setTitle("Clear browser data?")
                    .setMessage(
                        "This closes all browser tabs, signs you out, clears website data and"
                            + " cancels app audio transcription. Saved media and the playlist"
                            + " remain.")
                    .setNegativeButton("Cancel", null)
                    .setPositiveButton("Clear", (confirm, action) -> clearBrowserData())
                    .show())
        .show();
  }

  private void clearBrowserData() {
    if (clearingBrowserData) return;
    boolean full =
        androidx.webkit.WebViewFeature.isFeatureSupported(
            androidx.webkit.WebViewFeature.DELETE_BROWSING_DATA);
    clearingBrowserData = true;
    if (audioCapturing || transcribing) cancelAudio();
    if (browser != null) {
      browser.clearCache();
      browser.destroy();
      root.removeView(browser);
      browser = null;
      audioPanel = null;
      audioStart = null;
    }
    store.list("browserTabs", List.of());
    store.put("browserSelected", "0");
    Runnable complete =
        () -> {
          clearingBrowserData = false;
          if (!closed) {
            updateToggleStates();
            message("Browser cookies, website storage and cache cleared.");
          }
        };
    if (full)
      androidx.webkit.WebStorageCompat.deleteBrowsingData(
          android.webkit.WebStorage.getInstance(), complete);
    else {
      android.webkit.WebStorage.getInstance().deleteAllData();
      android.webkit.CookieManager.getInstance()
          .removeAllCookies(
              removed -> {
                android.webkit.CookieManager.getInstance().flush();
                complete.run();
              });
    }
  }

  private void requestCamera(Runnable action) {
    if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
      action.run();
      return;
    }
    if (pendingPermission != null) {
      message("Finish the current permission request first.");
      return;
    }
    new android.app.AlertDialog.Builder(this)
        .setTitle("USB video access")
        .setMessage(
            "VideoCapture needs Android camera permission to open your selected USB capture device."
                + " Preview stays on this device. Images and recordings are saved only when you"
                + " choose capture actions.")
        .setPositiveButton(
            "Continue",
            (dialog, which) -> {
              pendingPermission =
                  ok -> {
                    if (ok) action.run();
                  };
              requestPermissions(new String[] {Manifest.permission.CAMERA}, 201);
            })
        .setNegativeButton("Not now", null)
        .show();
  }

  private void restoreBrowserAudioNow() {
    browserUsingMicrophone = false;
    browserAudioReady = false;
    if (closed) return;
    if (player != null) player.setVolume(audioCapturing && capturingMic ? 0 : videoVolume);
    if (foreground) restoreUsbAudio();
    updateToggleStates();
  }

  private void prepareBrowserMicrophone(Consumer<Boolean> ready) {
    if (closed || !foreground || recorder != null) {
      message("Finish the current recording before using the browser microphone.");
      ready.accept(false);
      return;
    }
    requestMicRaw(
        ok -> {
          if (!ok || closed || !foreground) {
            ready.accept(false);
            return;
          }
          main.removeCallbacks(restoreBrowserAudio);
          if (browserUsingMicrophone && browserAudioReady) {
            ready.accept(true);
            return;
          }
          browserMicrophoneWaiters.add(ready);
          if (browserUsingMicrophone) return;
          browserUsingMicrophone = true;
          browserAudioReady = false;
          if (audioCapturing) finishAudio();
          if (player != null) player.setVolume(0);
          if (speaker != null) speaker.stop();
          closeUsbAudio();
          // The serial IO barrier runs after every owned AudioRecord has stopped/released.
          io.execute(
              () ->
                  main.post(
                      () -> {
                        boolean allowed = !closed && foreground && browserUsingMicrophone;
                        browserAudioReady = allowed;
                        List<Consumer<Boolean>> waiters = List.copyOf(browserMicrophoneWaiters);
                        browserMicrophoneWaiters.clear();
                        for (Consumer<Boolean> waiter : waiters) waiter.accept(allowed);
                      }));
        });
  }

  private void requestMic(Consumer<Boolean> action) {
    if (checkSelfPermission(Manifest.permission.RECORD_AUDIO)
        == PackageManager.PERMISSION_GRANTED) {
      action.accept(true);
      return;
    }
    new android.app.AlertDialog.Builder(this)
        .setTitle("Audio access")
        .setMessage(
            "VideoCapture uses the microphone or your selected USB audio input for recording and"
                + " offline transcription. Audio is processed on this device; browser voice access"
                + " is requested separately by website origin.")
        .setPositiveButton("Continue", (dialog, which) -> requestMicRaw(action))
        .setNegativeButton("Not now", (dialog, which) -> action.accept(false))
        .setOnCancelListener(dialog -> action.accept(false))
        .show();
  }

  private void requestMicRaw(Consumer<Boolean> action) {
    if (checkSelfPermission(Manifest.permission.RECORD_AUDIO)
        == PackageManager.PERMISSION_GRANTED) {
      action.accept(true);
      return;
    }
    if (pendingPermission != null) {
      action.accept(false);
      return;
    }
    pendingPermission = action;
    requestingMicrophonePermission = true;
    requestPermissions(new String[] {Manifest.permission.RECORD_AUDIO}, 202);
  }

  @Override
  public void onRequestPermissionsResult(int code, String[] permissions, int[] results) {
    super.onRequestPermissionsResult(code, permissions, results);
    Consumer<Boolean> callback = pendingPermission;
    pendingPermission = null;
    requestingMicrophonePermission = false;
    boolean ok = results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED;
    if (callback != null && !closed) {
      Runnable result = () -> callback.accept(ok);
      if (foreground) result.run();
      else permissionResultOnResume = result;
    }
    if (!ok) message("Permission denied. Enable camera/microphone access in Android app settings.");
  }

  private final Runnable tick =
      new Runnable() {
        public void run() {
          if (closed) return;
          updateToggleStates();
          if (player != null && !live) {
            long duration = Math.max(0, player.getDuration()),
                time = Math.max(0, player.getCurrentPosition());
            position.setText(time(time) + " / " + time(duration));
            if (!draggingSeek) seek.setProgress(duration > 0 ? (int) (time * 1000 / duration) : 0);
            seek.setEnabled(duration > 0 && !sourceLocked());
          } else {
            if (live && SystemClock.elapsedRealtime() - previewStamp >= 1000) {
              long now = SystemClock.elapsedRealtime();
              liveCaption =
                  String.format(
                      Locale.ROOT,
                      "LIVE · %.1f preview fps",
                      (previewFrames - previewCount) * 1000.0 / Math.max(1, now - previewStamp));
              previewStamp = now;
              previewCount = previewFrames;
            }
            position.setText(live ? liveCaption : "00:00 / 00:00");
            seek.setEnabled(false);
          }
          if (SystemClock.elapsedRealtime() - lastBookmark > 5000) {
            bookmark(false);
            lastBookmark = SystemClock.elapsedRealtime();
          }
          if (audioCapturing && SystemClock.elapsedRealtime() - audioStarted >= 120000)
            finishAudio();
          if (reading && embeddedCue.isBlank() && SystemClock.elapsedRealtime() - lastOcr >= 1500) {
            lastOcr = SystemClock.elapsedRealtime();
            recognize(true);
          }
          main.postDelayed(this, 300);
        }
      };

  private static String time(long millis) {
    long seconds = millis / 1000;
    return String.format(Locale.ROOT, "%02d:%02d", seconds / 60, seconds % 60);
  }

  @Override
  protected void onNewIntent(Intent intent) {
    super.onNewIntent(intent);
    setIntent(intent);
    if (intent.getData() != null && !sourceLocked()) openIncoming(intent.getData());
  }

  @Override
  protected void onResume() {
    super.onResume();
    main.removeCallbacks(tick);
    main.post(tick);
    foreground = true;
    if (browser != null) browser.resume();
    Runnable permissionResult = permissionResultOnResume;
    permissionResultOnResume = null;
    if (permissionResult != null) permissionResult.run();
    if (wantLive
        && usb == null
        && checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
      startUsb();
  }

  @Override
  protected void onPause() {
    main.removeCallbacks(tick);
    if (videoGestures != null) videoGestures.cancel();
    bookmark(false);
    if (player != null) player.pause();
    foreground = false;
    if (audioCapturing || transcribing) cancelAudio();
    if (speaker != null) speaker.stop();
    if (browser != null) browser.pause(requestingMicrophonePermission);
    super.onPause();
  }

  @Override
  protected void onStop() {
    if (browser != null) browser.stopMicrophone();
    finishCaptureRecording();
    if (pendingLocalClip != null) finishLocalClip();
    stopUsb();
    super.onStop();
  }

  @Override
  protected void onDestroy() {
    closed = true;
    permissionResultOnResume = null;
    cancelPlaylistImport();
    if (playlistNamesScan != null) playlistNamesScan.cancel();
    playlistIo.shutdownNow();
    if (clipExport != null) {
      clipExport.cancel();
      clipExport = null;
      if (clipExportFile != null) clipExportFile.delete();
      clipExportFile = null;
    }
    main.removeCallbacks(tick);
    main.removeCallbacks(hideGestureFeedback);
    main.removeCallbacks(restoreBrowserAudio);
    speech.close();
    if (browser != null) browser.destroy();
    browserFilesResult = null;
    if (player != null) {
      playerView.setPlayer(null);
      player.release();
      player = null;
    }
    if (ocrConsent != null) ocrConsent.close();
    if (ocr != null) ocr.close();
    if (speaker != null) speaker.shutdown();
    io.execute(io::shutdown);
    super.onDestroy();
  }
}
