package com.retiangson.videocapture;

import android.app.Activity;
import android.graphics.Color;
import android.net.Uri;
import android.os.Message;
import android.view.*;
import android.webkit.*;
import android.widget.*;
import java.util.*;
import java.util.function.Consumer;

final class BrowserOverlay extends LinearLayout {
  interface Host {
    void permission(Consumer<Boolean> result);

    void feedback(String value);

    void frame();

    void record();

    void ocr();

    void audio();
  }

  private static final String HOME = "https://chatgpt.com/";
  private final Activity activity;
  private final PlayerStore store;
  private final Host host;
  private final LinearLayout tabs = Ui.row(getContext()), navigation = Ui.row(getContext());
  private final FrameLayout page;
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

  BrowserOverlay(Activity activity, PlayerStore store, Host host) {
    super(activity);
    this.activity = activity;
    this.store = store;
    this.host = host;
    setOrientation(VERTICAL);
    setPadding(Ui.dp(activity, 8), Ui.dp(activity, 6), Ui.dp(activity, 8), Ui.dp(activity, 6));
    setBackground(Ui.panel(0xff18212c, Ui.dp(activity, 12)));
    setElevation(Ui.dp(activity, 12));
    setAlpha(Math.max(.3f, store.level("browserOpacity", 1)));
    LinearLayout title = Ui.row(activity);
    TextView grip = Ui.text(activity, "⋮⋮  Browser", 13, Color.WHITE);
    title.addView(grip, new LinearLayout.LayoutParams(0, Ui.dp(activity, 38), 1));
    title.addView(Ui.text(activity, "Opacity", 10, 0xff91a0b7));
    opacity = Ui.slider(activity, getAlpha(), v -> opacity(v));
    opacity.setMin(300);
    title.addView(opacity, new LinearLayout.LayoutParams(Ui.dp(activity, 100), -2));
    title.addView(Ui.button(activity, "—", () -> setVisibility(GONE)));
    title.addView(Ui.button(activity, "□", this::maximize));
    title.addView(Ui.button(activity, "×", () -> setVisibility(GONE)));
    addView(title);
    grip.setOnTouchListener(
        (v, e) -> {
          if (e.getActionMasked() == MotionEvent.ACTION_DOWN) {
            downX = e.getRawX();
            downY = e.getRawY();
            startX = getX();
            startY = getY();
            return true;
          }
          if (e.getActionMasked() == MotionEvent.ACTION_MOVE && !maximized) {
            setX(Math.max(0, startX + e.getRawX() - downX));
            setY(Math.max(0, startY + e.getRawY() - downY));
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
        Ui.scrollRow(activity, tabs), new LinearLayout.LayoutParams(0, Ui.dp(activity, 34), 1));
    tabRow.addView(Ui.button(activity, "+", () -> add(HOME)));
    addView(tabRow);
    page = new FrameLayout(activity);
    addView(page, new LinearLayout.LayoutParams(-1, 0, 1));
    Button nav =
        Ui.button(
            activity,
            "⌕  Navigation",
            () -> navigation.setVisibility(navigation.getVisibility() == VISIBLE ? GONE : VISIBLE));
    addView(nav);
    navigation.addView(
        Ui.button(
            activity,
            "‹",
            () -> {
              WebView w = current();
              if (w != null && w.canGoBack()) w.goBack();
            }));
    navigation.addView(
        Ui.button(
            activity,
            "›",
            () -> {
              WebView w = current();
              if (w != null && w.canGoForward()) w.goForward();
            }));
    navigation.addView(
        Ui.button(
            activity,
            "↻",
            () -> {
              if (current() != null) current().reload();
            }));
    address = Ui.input(activity, "https:// address");
    address.setSingleLine(true);
    navigation.addView(address, new LinearLayout.LayoutParams(0, Ui.dp(activity, 38), 1));
    navigation.addView(Ui.button(activity, "Go", () -> navigate(address.getText().toString())));
    addView(navigation);
    navigation.setVisibility(GONE);
    LinearLayout actions = Ui.row(activity);
    actions.addView(Ui.button(activity, "▣ Frame", host::frame));
    actions.addView(Ui.button(activity, "● Record", host::record));
    actions.addView(Ui.button(activity, "Aa Text", host::ocr));
    actions.addView(Ui.button(activity, "♫ Audio to text", host::audio));
    addView(Ui.scrollRow(activity, actions));
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
    CookieManager.getInstance().setAcceptCookie(true);
    view.setWebViewClient(
        new WebViewClient() {
          @Override
          public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest request) {
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
                  host.permission(
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
    if (!"about:blank".equals(url)) view.loadUrl(url);
  }

  private void navigate(String url) {
    if (!url.contains("://")) url = "https://" + url;
    if (valid(url) && current() != null) current().loadUrl(url);
    else host.feedback("Enter a valid HTTPS address.");
  }

  private WebView current() {
    return selected >= 0 && selected < views.size() ? views.get(selected) : null;
  }

  private void select(int index) {
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
      LinearLayout tab = Ui.row(activity);
      Button title = Ui.button(activity, titles.get(i), () -> select(index));
      title.setTextSize(10);
      title.setSingleLine(true);
      title.setEllipsize(android.text.TextUtils.TruncateAt.END);
      title.setMaxWidth(Ui.dp(activity, 150));
      title.setMinHeight(Ui.dp(activity, 28));
      tab.addView(title);
      Button close = Ui.button(activity, "×", () -> closeTab(index));
      close.setPadding(0, 0, 0, 0);
      tab.addView(close, new LinearLayout.LayoutParams(Ui.dp(activity, 26), Ui.dp(activity, 28)));
      tabs.addView(tab);
    }
  }

  private void closeTab(int index) {
    if (index < 0 || index >= views.size()) return;
    WebView view = views.remove(index);
    destinations.remove(view);
    titles.remove(index);
    page.removeView(view);
    view.destroy();
    if (views.isEmpty()) add(HOME);
    else select(Math.min(selected, views.size() - 1));
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

  private void maximize() {
    FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) getLayoutParams();
    if (!maximized) {
      oldWidth = getWidth();
      oldHeight = getHeight();
      oldX = getX();
      oldY = getY();
      p.width = -1;
      p.height = -1;
      setX(0);
      setY(0);
    } else {
      p.width = oldWidth;
      p.height = oldHeight;
      setX(oldX);
      setY(oldY);
    }
    maximized = !maximized;
    setLayoutParams(p);
  }

  void pause() {
    CookieManager.getInstance().flush();
    for (WebView view : views) view.onPause();
  }

  void resume() {
    for (WebView view : views) view.onResume();
  }

  void destroy() {
    save();
    disposed = true;
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
