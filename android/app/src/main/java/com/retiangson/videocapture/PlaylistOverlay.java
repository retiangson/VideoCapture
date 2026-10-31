package com.retiangson.videocapture;

import android.content.Context;
import android.graphics.Typeface;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;
import java.util.*;

/** An in-app floating queue. Row actions use URI identity, never stale adapter positions. */
final class PlaylistOverlay extends LinearLayout {
  record Entry(String uri, String name, String location, boolean playing) {}

  interface Host {
    void files();

    void folder();

    void play(String uri);

    void remove(String uri);

    void move(String uri, int offset);

    void clear();

    void cancelImport();
  }

  private final Host host;
  private final TextView count, feedback;
  private final Button files, folder, clear, cancel;
  private final ListView list;
  private final Rows rows = new Rows();
  private List<Entry> entries = List.of();
  private boolean editable = true, positioned, loading, updated;
  private int parentWidth, parentHeight;
  private float downX, downY;
  private int startX, startY;

  PlaylistOverlay(Context context, Host host) {
    super(context);
    this.host = host;
    setOrientation(VERTICAL);
    setPadding(Ui.dp(context, 12), Ui.dp(context, 8), Ui.dp(context, 12), Ui.dp(context, 10));
    setBackground(PlayerSkin.surface(Ui.dp(context, 16), false));
    setElevation(Ui.dp(context, 14));
    LinearLayout header = Ui.row(context);
    TextView title = Ui.text(context, "PLAYLIST  ·  drag to move", 12, PlayerSkin.TEXT);
    title.setTypeface(null, Typeface.BOLD);
    header.addView(title, new LayoutParams(0, Ui.dp(context, 48), 1));
    title.setGravity(Gravity.CENTER_VERTICAL);
    header.addView(
        MenuIcons.icon(context, MenuIcons.Kind.HIDE, "Hide playlist", () -> setVisibility(GONE)));
    addView(header);
    title.setOnTouchListener(
        (view, event) -> {
          if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
            FrameLayout.LayoutParams bounds = (FrameLayout.LayoutParams) getLayoutParams();
            startX = bounds.leftMargin;
            startY = bounds.topMargin;
            downX = event.getRawX();
            downY = event.getRawY();
            return true;
          }
          if (event.getActionMasked() == MotionEvent.ACTION_MOVE) {
            move(
                Math.round(startX + event.getRawX() - downX),
                Math.round(startY + event.getRawY() - downY));
            return true;
          }
          if (event.getActionMasked() == MotionEvent.ACTION_UP) view.performClick();
          return true;
        });
    LinearLayout actions = Ui.row(context);
    files = MenuIcons.action(context, "Add files", MenuIcons.Kind.OPEN, host::files);
    folder = MenuIcons.action(context, "Add folder", MenuIcons.Kind.OPEN, host::folder);
    clear = MenuIcons.icon(context, MenuIcons.Kind.DELETE, "Clear playlist", host::clear);
    actions.addView(files);
    actions.addView(folder);
    actions.addView(clear);
    addView(Ui.scrollRow(context, actions));
    count = Ui.text(context, "0 items", 11, PlayerSkin.ACCENT);
    count.setPadding(Ui.dp(context, 4), Ui.dp(context, 8), 0, Ui.dp(context, 8));
    addView(count);
    FrameLayout content = new FrameLayout(context);
    list = new ListView(context);
    list.setDividerHeight(Ui.dp(context, 5));
    list.setDivider(
        new android.graphics.drawable.ColorDrawable(android.graphics.Color.TRANSPARENT));
    list.setAdapter(rows);
    content.addView(list, new FrameLayout.LayoutParams(-1, -1));
    TextView empty =
        Ui.text(
            context,
            "Your next video goes here.\n\nAdd files or a folder to build your queue.",
            14,
            PlayerSkin.MUTED);
    empty.setGravity(Gravity.CENTER);
    empty.setPadding(Ui.dp(context, 16), 0, Ui.dp(context, 16), 0);
    content.addView(empty, new FrameLayout.LayoutParams(-1, -1));
    list.setEmptyView(empty);
    addView(content, new LayoutParams(-1, 0, 1));
    LinearLayout footer = Ui.row(context);
    feedback =
        Ui.text(
            context,
            "Remove keeps the original file. Hold a title to reorder.",
            11,
            PlayerSkin.MUTED);
    feedback.setMaxLines(2);
    footer.addView(feedback, new LayoutParams(0, -2, 1));
    cancel = Ui.button(context, "Cancel scan", host::cancelImport);
    cancel.setVisibility(GONE);
    footer.addView(cancel);
    addView(footer);
    setVisibility(GONE);
  }

  void update(List<Entry> value, boolean canEdit, boolean scanning) {
    if (updated && entries.equals(value) && editable == canEdit && loading == scanning) return;
    updated = true;
    loading = scanning;
    boolean changed = !entries.equals(value) || editable != canEdit;
    entries = List.copyOf(value);
    editable = canEdit;
    if (changed) rows.notifyDataSetChanged();
    files.setEnabled(canEdit);
    folder.setEnabled(canEdit);
    clear.setEnabled(canEdit && !entries.isEmpty());
    count.setText(entries.size() + " / 200 items");
    cancel.setVisibility(scanning ? VISIBLE : GONE);
    feedback.setText(
        scanning
            ? "Scanning the selected folder…"
            : !canEdit
                ? "Playlist changes are unavailable while capturing or loading media."
                : "Remove keeps the original file. Hold a title to reorder.");
  }

  void toggle() {
    setVisibility(getVisibility() == VISIBLE ? GONE : VISIBLE);
    if (isShown()) bringToFront();
  }

  void layoutWithinParent() {
    if (!(getParent() instanceof FrameLayout parent)) return;
    int width = parent.getWidth() - parent.getPaddingLeft() - parent.getPaddingRight();
    int height = parent.getHeight() - parent.getPaddingTop() - parent.getPaddingBottom();
    if (width < 1 || height < 1) return;
    FrameLayout.LayoutParams bounds = (FrameLayout.LayoutParams) getLayoutParams();
    int margin = Ui.dp(getContext(), width < Ui.dp(getContext(), 600) ? 8 : 18);
    int newWidth = Math.max(1, Math.min(Ui.dp(getContext(), 640), width - 2 * margin));
    int newHeight = Math.max(1, Math.min(Ui.dp(getContext(), 600), height - 2 * margin));
    if (!positioned || width != parentWidth || height != parentHeight) {
      bounds.width = newWidth;
      bounds.height = newHeight;
      if (!positioned) {
        bounds.leftMargin = Math.max(margin, width - newWidth - margin);
        bounds.topMargin = margin;
      }
      bounds.leftMargin = Math.max(0, Math.min(bounds.leftMargin, width - newWidth));
      bounds.topMargin = Math.max(0, Math.min(bounds.topMargin, height - newHeight));
      setLayoutParams(bounds);
    }
    positioned = true;
    parentWidth = width;
    parentHeight = height;
  }

  private void move(int x, int y) {
    FrameLayout.LayoutParams bounds = (FrameLayout.LayoutParams) getLayoutParams();
    bounds.leftMargin = Math.max(0, Math.min(x, parentWidth - bounds.width));
    bounds.topMargin = Math.max(0, Math.min(y, parentHeight - bounds.height));
    setLayoutParams(bounds);
  }

  private final class Rows extends BaseAdapter {
    public int getCount() {
      return entries.size();
    }

    public Entry getItem(int position) {
      return entries.get(position);
    }

    public long getItemId(int position) {
      return position;
    }

    public View getView(int position, View recycled, android.view.ViewGroup parent) {
      Row row = recycled instanceof Row ? (Row) recycled : new Row(getContext());
      row.bind(getItem(position), position);
      return row;
    }
  }

  private final class Row extends LinearLayout {
    private final TextView number, title, location;
    private final LinearLayout text;
    private final Button remove;

    Row(Context context) {
      super(context);
      setOrientation(HORIZONTAL);
      setGravity(Gravity.CENTER_VERTICAL);
      setPadding(Ui.dp(context, 8), Ui.dp(context, 8), Ui.dp(context, 4), Ui.dp(context, 8));
      setMinimumHeight(Ui.dp(context, 72));
      number = Ui.text(context, "", 11, PlayerSkin.MUTED);
      number.setGravity(Gravity.CENTER);
      addView(number, new LayoutParams(Ui.dp(context, 28), -2));
      text = Ui.column(context);
      text.setPadding(Ui.dp(context, 4), 0, Ui.dp(context, 8), 0);
      title = Ui.text(context, "", 14, PlayerSkin.TEXT);
      title.setMaxLines(2);
      title.setEllipsize(TextUtils.TruncateAt.END);
      location = Ui.text(context, "", 10, PlayerSkin.MUTED);
      location.setSingleLine(true);
      location.setEllipsize(TextUtils.TruncateAt.MIDDLE);
      text.addView(title);
      text.addView(location);
      text.setMinimumHeight(Ui.dp(context, 48));
      text.setGravity(Gravity.CENTER_VERTICAL);
      addView(text, new LayoutParams(0, -2, 1));
      remove = MenuIcons.icon(context, MenuIcons.Kind.DELETE, "Remove item", () -> {});
      addView(remove);
    }

    void bind(Entry entry, int position) {
      number.setText(entry.playing ? "▶" : Integer.toString(position + 1));
      number.setTextColor(entry.playing ? PlayerSkin.ACCENT : PlayerSkin.MUTED);
      title.setText(entry.name);
      location.setText(entry.location);
      setBackground(PlayerSkin.surface(Ui.dp(getContext(), 9), entry.playing));
      text.setEnabled(editable);
      remove.setEnabled(editable);
      text.setAlpha(editable ? 1 : .45f);
      text.setContentDescription((entry.playing ? "Now playing: " : "Play: ") + entry.name);
      text.setOnClickListener(view -> host.play(entry.uri));
      text.setOnLongClickListener(
          view -> {
            if (!editable) return true;
            PopupMenu menu = new PopupMenu(getContext(), text);
            menu.getMenu().add(0, 0, 0, "Move up").setEnabled(position > 0);
            menu.getMenu().add(0, 1, 1, "Move down").setEnabled(position + 1 < entries.size());
            menu.setOnMenuItemClickListener(
                item -> {
                  host.move(entry.uri, item.getItemId() == 0 ? -1 : 1);
                  return true;
                });
            menu.show();
            return true;
          });
      remove.setContentDescription("Remove " + entry.name + " from playlist");
      remove.setOnClickListener(view -> host.remove(entry.uri));
    }
  }
}
