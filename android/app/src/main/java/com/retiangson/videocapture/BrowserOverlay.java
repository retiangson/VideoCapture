package com.retiangson.videocapture;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Message;
import android.view.*;
import android.webkit.*;
import android.widget.*;
import java.util.*;
import java.util.function.Consumer;

final class BrowserOverlay extends LinearLayout {
  interface Host {
    default void stateChanged() {}

    default boolean chooseFiles(Intent intent, Consumer<Uri[]> result) {
      return false;
    }

    default void microphoneReleased() {}

    void permission(Consumer<Boolean> result);

    void feedback(String value);

    void frame();

    void saveFrame();

    void copyRecording();

    void record();

    void ocr();

    void audio();
  }

  private static final String HOME = "https://chatgpt.com/";
  private final Activity activity;
  private final PlayerStore store;
  private final Host host;
  private final BrowserAttachments attachments;
  private final BrowserMicrophone microphone;
  private final LinearLayout tabs = Ui.row(getContext()), navigation = Ui.row(getContext());
  private final FrameLayout page;
  private final LinearLayout tools;
  private final ScrollView toolsScroll;
  private boolean positioned;
  private int parentWidth, parentHeight;
  private final EditText address;
  private final List<WebView> views = new ArrayList<>();
  private final List<String> titles = new ArrayList<>();
  private final Map<WebView, String> destinations = new IdentityHashMap<>();
  private final Set<PermissionRequest> requests =
      Collections.newSetFromMap(new IdentityHashMap<>());
  private int selected;
  private boolean disposed, maximized;
  private int oldWidth, oldHeight;
  private float oldX, oldY;
  private float downX, downY, startX, startY;
  private long lastTap;
  private SeekBar opacity;
  private Button maximizeButton, navigationButton, recordingButton, transcriptionButton;

  BrowserOverlay(Activity activity, PlayerStore store, Host host) {
    super(activity);
    this.activity = activity;
    this.store = store;
    this.host = host;
    attachments = new BrowserAttachments(activity, host::chooseFiles, host::feedback);
    microphone =
        new BrowserMicrophone(
            activity,
            new BrowserMicrophone.Host() {
              public void prepare(Consumer<Boolean> ready) {
                host.permission(ready);
              }

              public void released() {
                host.microphoneReleased();
              }

              public void feedback(String message) {
                host.feedback(message);
              }

              public boolean active(WebView view) {
                return !disposed
                    && getVisibility() == VISIBLE
                    && view == current()
                    && !activity.isFinishing();
              }
            });
    setOrientation(VERTICAL);
    setPadding(Ui.dp(activity, 8), Ui.dp(activity, 6), Ui.dp(activity, 8), Ui.dp(activity, 6));
    setBackground(PlayerSkin.surface(Ui.dp(activity, 12), false));
    setElevation(Ui.dp(activity, 12));
    setAlpha(Math.max(.3f, store.level("browserOpacity", 1)));
    LinearLayout title = Ui.row(activity);
    TextView grip = Ui.text(activity, "BROWSER", 11, PlayerSkin.TEXT);
    grip.setSingleLine(true);
    grip.setEllipsize(android.text.TextUtils.TruncateAt.END);
    grip.setGravity(Gravity.CENTER_VERTICAL);
    title.addView(grip, new LinearLayout.LayoutParams(0, Ui.dp(activity, 48), 1));

    opacity = Ui.slider(activity, getAlpha(), v -> opacity(v));
    opacity.setMin(300);
    opacity.setContentDescription("Browser panel opacity");
    title.addView(opacity, new LinearLayout.LayoutParams(Ui.dp(activity, 60), Ui.dp(activity, 48)));
    title.addView(
        Ui.compactIconButton(activity, "—", "Minimize browser", () -> setVisibility(GONE)));
    maximizeButton =
        Ui.compactIconButton(activity, "□", "Maximize or restore browser", this::maximize);
    title.addView(maximizeButton);
    title.addView(Ui.compactIconButton(activity, "×", "Hide browser", () -> setVisibility(GONE)));
    addView(title);
    grip.setOnTouchListener(
        (v, e) -> {
          if (e.getActionMasked() == MotionEvent.ACTION_DOWN) {
            downX = e.getRawX();
            downY = e.getRawY();
            FrameLayout.LayoutParams bounds = (FrameLayout.LayoutParams) getLayoutParams();
            startX = bounds.leftMargin;
            startY = bounds.topMargin;
            return true;
          }
          if (e.getActionMasked() == MotionEvent.ACTION_MOVE && !maximized) {
            moveTo(
                Math.round(startX + e.getRawX() - downX), Math.round(startY + e.getRawY() - downY));
            return true;
          }
          if (e.getActionMasked() == MotionEvent.ACTION_UP) {
            if (Math.abs(e.getRawX() - downX) < 10 && Math.abs(e.getRawY() - downY) < 10) {
              long now = System.currentTimeMillis();
              if (now - lastTap < 350) {
                maximize();
                lastTap = 0;
              } else lastTap = now;
            }
            v.performClick();
            return true;
          }
          return true;
        });
    LinearLayout tabRow = Ui.row(activity);
    tabRow.addView(
        Ui.scrollRow(activity, tabs), new LinearLayout.LayoutParams(0, Ui.dp(activity, 48), 1));
    tabRow.addView(Ui.compactIconButton(activity, "+", "New browser tab", () -> add(HOME)));
    addView(tabRow);
    page = new FrameLayout(activity);
    addView(page, new LinearLayout.LayoutParams(-1, 0, 1));
    tools = Ui.column(activity);
    toolsScroll = new ScrollView(activity);
    toolsScroll.addView(tools, new ScrollView.LayoutParams(-1, -2));
    addView(toolsScroll, new LinearLayout.LayoutParams(-1, -2));
    navigationButton =
        MenuIcons.action(
            activity,
            "Navigation",
            MenuIcons.Kind.BROWSER,
            () -> {
              navigation.setVisibility(navigation.getVisibility() == VISIBLE ? GONE : VISIBLE);
              Ui.active(navigationButton, navigation.getVisibility() == VISIBLE);
            });

    navigation.addView(
        Ui.iconButton(
            activity,
            "‹",
            "Browser back",
            () -> {
              WebView w = current();
              if (w != null && w.canGoBack()) w.goBack();
            }));
    navigation.addView(
        Ui.iconButton(
            activity,
            "›",
            "Browser forward",
            () -> {
              WebView w = current();
              if (w != null && w.canGoForward()) w.goForward();
            }));
    navigation.addView(
        Ui.iconButton(
            activity,
            "↻",
            "Reload browser",
            () -> {
              if (current() != null) current().reload();
            }));
    address = Ui.input(activity, "https:// address");
    address.setSingleLine(true);
    navigation.addView(address, new LinearLayout.LayoutParams(0, Ui.dp(activity, 48), 1));
    address.setImeOptions(android.view.inputmethod.EditorInfo.IME_ACTION_GO);
    address.setOnEditorActionListener(
        (v, action, event) -> {
          if (action != android.view.inputmethod.EditorInfo.IME_ACTION_GO) return false;
          navigate(address.getText().toString());
          return true;
        });
    navigation.addView(
        Ui.iconButton(
            activity, "→", "Open browser address", () -> navigate(address.getText().toString())));
    tools.addView(navigation);
    navigation.setVisibility(GONE);
    LinearLayout actions = Ui.row(activity);
    actions.addView(navigationButton);
    actions.addView(MenuIcons.action(activity, "Copy frame", MenuIcons.Kind.FRAME, host::frame));
    actions.addView(MenuIcons.action(activity, "Save frame", MenuIcons.Kind.SAVE, host::saveFrame));
    recordingButton = MenuIcons.action(activity, "Record", MenuIcons.Kind.RECORD, host::record);
    actions.addView(recordingButton);
    actions.addView(
        MenuIcons.action(activity, "Copy recording", MenuIcons.Kind.COPY, host::copyRecording));
    actions.addView(MenuIcons.action(activity, "Text", MenuIcons.Kind.TEXT, host::ocr));
    transcriptionButton =
        MenuIcons.action(activity, "Audio to text", MenuIcons.Kind.MIC, host::audio);
    actions.addView(transcriptionButton);
    tools.addView(Ui.scrollRow(activity, actions), 0);
    int savedIndex = 0;
    try {
      savedIndex = Integer.parseInt(store.text("browserSelected", "0"));
    } catch (NumberFormatException ignored) {
    }
    List<String> saved = store.list("browserTabs", 32);
    if (saved.isEmpty()) saved = List.of(HOME);
    for (String url : saved) add(valid(url) ? url : HOME);
    select(Math.max(0, Math.min(views.size() - 1, savedIndex)));
    setVisibility(GONE);
  }

  static boolean trusted(String value) {
    try {
      Uri uri = Uri.parse(value);
      return "https".equals(uri.getScheme())
          && (uri.getPort() == -1 || uri.getPort() == 443)
          && uri.getUserInfo() == null
          && ("chatgpt.com".equalsIgnoreCase(uri.getHost())
              || "chat.openai.com".equalsIgnoreCase(uri.getHost()));
    } catch (Exception e) {
      return false;
    }
  }

  static boolean valid(String value) {
    try {
      if (value == null || value.length() > 8192) return false;
      Uri uri = Uri.parse(value);
      return "https".equals(uri.getScheme()) && uri.getHost() != null
          || "about:blank".equals(value);
    } catch (Exception e) {
      return false;
    }
  }

  void add(String url) {
    if (BrowserNavigation.externalSignIn(url)) {
      BrowserNavigation.openSignIn(activity, url, host::feedback);
      url = HOME;
    }
    if (disposed || views.size() >= 32) {
      host.feedback("Browser tab limit reached (32).");
      return;
    }
    WebView view = new WebView(activity);
    views.add(view);
    destinations.put(view, url);
    titles.add("New tab");
    WebSettings settings = view.getSettings();
    settings.setJavaScriptEnabled(true);
    settings.setDomStorageEnabled(true);
    settings.setSupportMultipleWindows(true);
    settings.setAllowFileAccess(false);
    settings.setAllowContentAccess(false);
    settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
    settings.setSafeBrowsingEnabled(true);
    settings.setMediaPlaybackRequiresUserGesture(true);
    microphone.install(view);
    CookieManager.getInstance().setAcceptCookie(true);
    view.setWebViewClient(
        new WebViewClient() {
          @Override
          public void onPageStarted(WebView v, String url, android.graphics.Bitmap icon) {
            if (BrowserNavigation.externalSignIn(url)) {
              v.stopLoading();
              BrowserNavigation.openSignIn(activity, url, host::feedback);
              v.loadUrl(HOME);
              return;
            }
            attachments.cancelFor(v);
            microphone.navigation(v);
          }

          @Override
          public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest request) {
            if (BrowserNavigation.externalSignIn(request.getUrl().toString())) {
              if (request.isForMainFrame())
                BrowserNavigation.openSignIn(activity, request.getUrl().toString(), host::feedback);
              return true;
            }
            if (!valid(request.getUrl().toString())) {
              host.feedback("Only HTTPS browser navigation is supported.");
              return true;
            }
            return false;
          }

          @Override
          public void onPageFinished(WebView v, String uri) {
            if (valid(uri)) destinations.put(v, uri);
            if (v == current()) address.setText(uri);
            save();
          }
        });
    view.setWebChromeClient(
        new WebChromeClient() {
          @Override
          public boolean onShowFileChooser(
              WebView v, ValueCallback<Uri[]> result, FileChooserParams params) {
            if (disposed || getVisibility() != VISIBLE || v != current()) {
              result.onReceiveValue(null);
              return true;
            }
            return attachments.open(v, result, params);
          }

          @Override
          public void onReceivedTitle(WebView v, String text) {
            int i = views.indexOf(v);
            if (i >= 0) {
              titles.set(i, text);
              renderTabs();
            }
          }

          @Override
          public void onPermissionRequest(PermissionRequest request) {
            activity.runOnUiThread(
                () -> {
                  if (disposed
                      || !trusted(request.getOrigin().toString())
                      || !trusted(view.getUrl())
                      || !request
                          .getOrigin()
                          .getHost()
                          .equalsIgnoreCase(Uri.parse(view.getUrl()).getHost())) {
                    request.deny();
                    return;
                  }
                  boolean audio =
                      Arrays.asList(request.getResources())
                          .contains(PermissionRequest.RESOURCE_AUDIO_CAPTURE);
                  if (!audio) {
                    request.deny();
                    return;
                  }
                  String origin = view.getUrl();
                  requests.add(request);
                  microphone.permission(
                      view,
                      ok -> {
                        if (!requests.remove(request)) return;
                        if (ok && !disposed && Objects.equals(view.getUrl(), origin))
                          request.grant(new String[] {PermissionRequest.RESOURCE_AUDIO_CAPTURE});
                        else request.deny();
                      });
                });
          }

          @Override
          public void onPermissionRequestCanceled(PermissionRequest request) {
            requests.remove(request);
          }

          @Override
          public boolean onCreateWindow(
              WebView v, boolean dialog, boolean gesture, Message result) {
            if (!gesture) return false;
            add("about:blank");
            ((WebView.WebViewTransport) result.obj).setWebView(current());
            result.sendToTarget();
            return true;
          }

          @Override
          public void onCloseWindow(WebView w) {
            closeTab(views.indexOf(w));
          }
        });
    view.setDownloadListener(
        (u, ua, cd, mime, len) ->
            host.feedback("Downloads are not supported in the floating browser."));
    page.addView(view, new FrameLayout.LayoutParams(-1, -1));
    select(views.size() - 1);
    if (BrowserNavigation.externalSignIn(url))
      BrowserNavigation.openSignIn(activity, url, host::feedback);
    else if (!"about:blank".equals(url)) view.loadUrl(url);
  }

  private void navigate(String url) {
    if (!url.contains("://")) url = "https://" + url;
    if (BrowserNavigation.externalSignIn(url))
      BrowserNavigation.openSignIn(activity, url, host::feedback);
    else if (valid(url) && current() != null) current().loadUrl(url);
    else host.feedback("Enter a valid HTTPS address.");
  }

  private WebView current() {
    return selected >= 0 && selected < views.size() ? views.get(selected) : null;
  }

  private void select(int index) {
    WebView previous = current();
    if (previous != null && views.indexOf(previous) != index) microphone.stop(previous);
    attachments.cancel();
    selected = index;
    for (int i = 0; i < views.size(); i++) views.get(i).setVisibility(i == index ? VISIBLE : GONE);
    if (current() != null) address.setText(current().getUrl());
    renderTabs();
    save();
  }

  private void renderTabs() {
    tabs.removeAllViews();
    for (int i = 0; i < views.size(); i++) {
      final int index = i;
      FrameLayout tab = new FrameLayout(activity);
      LinearLayout.LayoutParams tabBounds =
          new LinearLayout.LayoutParams(Ui.dp(activity, 152), Ui.dp(activity, 48));
      tabBounds.setMargins(Ui.dp(activity, 2), 0, Ui.dp(activity, 2), 0);
      tab.setLayoutParams(tabBounds);
      Button title = Ui.button(activity, titles.get(i), () -> select(index));
      Ui.active(title, i == selected);
      title.setTextSize(10);
      title.setSingleLine(true);
      title.setEllipsize(android.text.TextUtils.TruncateAt.END);
      title.setMinWidth(0);
      title.setMinimumWidth(0);
      Ui.compactSurface(title);
      title.setPadding(Ui.dp(activity, 10), 0, Ui.dp(activity, 46), 0);
      title.setContentDescription("Browser tab: " + titles.get(i));
      tab.addView(title, new FrameLayout.LayoutParams(-1, -1));
      Button close =
          Ui.iconButton(
              activity, "×", "Close browser tab: " + titles.get(i), () -> closeTab(index));
      close.setBackground(
          new android.graphics.drawable.RippleDrawable(
              android.content.res.ColorStateList.valueOf(0x28ffffff), null, null));
      tab.addView(close, new FrameLayout.LayoutParams(Ui.dp(activity, 48), -1, Gravity.END));
      tabs.addView(tab);
    }
  }

  private void closeTab(int index) {
    if (index < 0 || index >= views.size()) return;
    WebView active = current();
    WebView view = views.remove(index);
    microphone.remove(view);
    attachments.cancelFor(view);
    destinations.remove(view);
    titles.remove(index);
    page.removeView(view);
    view.destroy();
    if (views.isEmpty()) add(HOME);
    else select(active == view ? Math.min(index, views.size() - 1) : views.indexOf(active));
  }

  private void save() {
    if (disposed) return;
    List<String> urls = new ArrayList<>();
    for (WebView view : views) {
      String url = destinations.getOrDefault(view, HOME);
      urls.add(valid(url == null ? "" : url) ? url : HOME);
    }
    store.list("browserTabs", urls);
    store.put("browserSelected", Integer.toString(selected));
  }

  boolean back() {
    WebView view = current();
    if (view != null && view.canGoBack()) {
      view.goBack();
      return true;
    }
    return false;
  }

  void toggle() {
    setVisibility(getVisibility() == VISIBLE ? GONE : VISIBLE);
    if (getVisibility() == VISIBLE) bringToFront();
  }

  void opacity(float value) {
    setAlpha(Math.max(.3f, Math.min(1, value)));
    if (opacity != null) opacity.setProgress(Math.round(getAlpha() * 1000));
    store.putLevel("browserOpacity", getAlpha());
  }

  @Override
  public boolean dispatchGenericMotionEvent(MotionEvent event) {
    if (event.getAction() == MotionEvent.ACTION_SCROLL) {
      float y = event.getY();
      if (event.getX() < page.getLeft()
          || event.getX() > page.getRight()
          || y < page.getTop()
          || y > page.getBottom()) {
        opacity(getAlpha() + event.getAxisValue(MotionEvent.AXIS_VSCROLL) * .05f);
        return true;
      }
    }
    return super.dispatchGenericMotionEvent(event);
  }

  @Override
  protected void onVisibilityChanged(View changedView, int visibility) {
    super.onVisibilityChanged(changedView, visibility);
    if (microphone != null && getVisibility() != VISIBLE) microphone.stopAll();
    if (host != null) host.stateChanged();
  }

  void captureStates(boolean recording, boolean transcriptVisible) {
    Ui.active(recordingButton, recording);
    Ui.active(transcriptionButton, transcriptVisible);
  }

  void setTranscriptPanel(View panel) {
    tools.addView(panel);
  }

  @Override
  protected void onMeasure(int widthSpec, int heightSpec) {
    // Leave space for browser content; tools remain scrollable when the IME or a short window
    // reduces height.
    int height = MeasureSpec.getSize(heightSpec);
    int controls = Math.max(Ui.dp(activity, 48), (height - Ui.dp(activity, 112)) / 2);
    toolsScroll.getLayoutParams().height = Math.min(controls, toolsHeight());
    super.onMeasure(widthSpec, heightSpec);
  }

  private int toolsHeight() {
    int width =
        Math.max(
            1,
            ((FrameLayout.LayoutParams) getLayoutParams()).width
                - getPaddingLeft()
                - getPaddingRight());
    tools.measure(
        MeasureSpec.makeMeasureSpec(width, MeasureSpec.EXACTLY),
        MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED));
    return tools.getMeasuredHeight();
  }

  void layoutWithinParent() {
    if (!(getParent() instanceof FrameLayout)) return;
    FrameLayout parent = (FrameLayout) getParent();
    int width = parent.getWidth() - parent.getPaddingLeft() - parent.getPaddingRight();
    int height = parent.getHeight() - parent.getPaddingTop() - parent.getPaddingBottom();
    if (width < 1 || height < 1) return;
    FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) getLayoutParams();
    int previousWidth = p.width,
        previousHeight = p.height,
        previousLeft = p.leftMargin,
        previousTop = p.topMargin;
    if (maximized) {
      p.width = width;
      p.height = height;
      p.leftMargin = 0;
      p.topMargin = 0;
    } else {
      int margin = Math.min(Ui.dp(activity, 8), Math.min(width, height) / 8);
      boolean compact = width < Ui.dp(activity, 600);
      if (!positioned || width != parentWidth || height != parentHeight) {
        p.width =
            compact
                ? width - 2 * margin
                : Math.min(width - 2 * margin, Math.max(Ui.dp(activity, 400), width * 4 / 5));
        p.height =
            compact
                ? height - 2 * margin
                : Math.min(height - 2 * margin, Math.max(Ui.dp(activity, 280), height * 4 / 5));
        if (!positioned) {
          p.leftMargin = margin;
          p.topMargin = margin;
        }
      }
      p.width = Math.max(1, Math.min(p.width, width));
      p.height = Math.max(1, Math.min(p.height, height));
      p.leftMargin = Math.max(0, Math.min(p.leftMargin, width - p.width));
      p.topMargin = Math.max(0, Math.min(p.topMargin, height - p.height));
    }
    parentWidth = width;
    parentHeight = height;
    positioned = true;
    if (previousWidth != p.width
        || previousHeight != p.height
        || previousLeft != p.leftMargin
        || previousTop != p.topMargin) setLayoutParams(p);
  }

  private void moveTo(int x, int y) {
    FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) getLayoutParams();
    p.leftMargin = Math.max(0, Math.min(x, parentWidth - p.width));
    p.topMargin = Math.max(0, Math.min(y, parentHeight - p.height));
    setLayoutParams(p);
  }

  private void maximize() {
    FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) getLayoutParams();
    if (!maximized) {
      oldWidth = p.width;
      oldHeight = p.height;
      oldX = p.leftMargin;
      oldY = p.topMargin;
    } else {
      p.width = oldWidth;
      p.height = oldHeight;
      p.leftMargin = Math.round(oldX);
      p.topMargin = Math.round(oldY);
    }
    maximized = !maximized;
    Ui.active(maximizeButton, maximized);
    setLayoutParams(p);
    layoutWithinParent();
  }

  void pause(boolean awaitingMicrophonePermission) {
    // Android's first-use permission dialog pauses the host before returning its decision.
    // No stream can exist yet; keep the pending handshake alive until the activity resumes.
    if (!awaitingMicrophonePermission) stopMicrophone();
    CookieManager.getInstance().flush();
    for (WebView view : views) view.onPause();
  }

  void stopMicrophone() {
    microphone.stopAll();
  }

  void clearCache() {
    for (WebView view : views) view.clearCache(true);
  }

  void resume() {
    for (WebView view : views) view.onResume();
  }

  void destroy() {
    save();
    microphone.stopAll();
    disposed = true;
    attachments.cancel();
    for (PermissionRequest request : requests)
      try {
        request.deny();
      } catch (IllegalStateException ignored) {
      }
    requests.clear();
    for (WebView view : views) {
      page.removeView(view);
      view.destroy();
    }
    views.clear();
  }
}
