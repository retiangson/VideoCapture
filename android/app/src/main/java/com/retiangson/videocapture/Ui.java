package com.retiangson.videocapture;

import android.content.Context;
import android.graphics.drawable.GradientDrawable;
import android.view.View;
import android.widget.*;

final class Ui {
  static int dp(Context c, float value) {
    return Math.round(value * c.getResources().getDisplayMetrics().density);
  }

  static GradientDrawable panel(int color, int radius) {
    GradientDrawable d = new GradientDrawable();
    d.setColor(color);
    d.setCornerRadius(radius);
    d.setStroke(1, 0xff344157);
    return d;
  }

  static TextView text(Context c, String value, int size, int color) {
    TextView t = new TextView(c);
    t.setText(value);
    t.setTextSize(size);
    t.setTextColor(color);
    return t;
  }

  // This app intentionally uses framework Theme.Material widgets, with explicit drawables/tints.
  @android.annotation.SuppressLint("AppCompatCustomView")
  private static final class AppButton extends Button {
    AppButton(Context context) {
      super(context);
    }

    @Override
    public void setEnabled(boolean enabled) {
      super.setEnabled(enabled);
      setAlpha(enabled ? 1f : .35f);
    }
  }

  static Button button(Context c, String value, Runnable action) {
    Button b = new AppButton(c);
    b.setText(value);
    b.setAllCaps(false);
    b.setTextSize(12);
    b.setTextColor(PlayerSkin.TEXT);
    b.setMinHeight(dp(c, 48));
    b.setMinimumHeight(dp(c, 48));
    b.setPadding(dp(c, 10), dp(c, 4), dp(c, 10), dp(c, 4));
    android.graphics.drawable.StateListDrawable states =
        new android.graphics.drawable.StateListDrawable();
    GradientDrawable active = PlayerSkin.surface(dp(c, 7), true);
    active.setStroke(dp(c, 2), PlayerSkin.ACCENT);
    GradientDrawable mark = new GradientDrawable();
    mark.setShape(GradientDrawable.OVAL);
    mark.setColor(PlayerSkin.ACCENT);
    android.graphics.drawable.LayerDrawable selected =
        new android.graphics.drawable.LayerDrawable(
            new android.graphics.drawable.Drawable[] {active, mark});
    selected.setLayerSize(1, dp(c, 5), dp(c, 5));
    selected.setLayerGravity(1, android.view.Gravity.RIGHT | android.view.Gravity.BOTTOM);
    selected.setLayerInset(1, dp(c, 4), dp(c, 4), dp(c, 4), dp(c, 4));
    states.addState(new int[] {android.R.attr.state_selected}, selected);
    states.addState(new int[] {}, PlayerSkin.surface(dp(c, 7), false));
    b.setBackground(
        new android.graphics.drawable.RippleDrawable(
            android.content.res.ColorStateList.valueOf(0x28ffffff), states, null));
    LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-2, dp(c, 48));
    p.setMargins(dp(c, 2), dp(c, 2), dp(c, 2), dp(c, 2));
    b.setLayoutParams(p);
    b.setOnClickListener(v -> action.run());
    return b;
  }

  static void active(Button button, boolean on) {
    active(button, on, on ? "On" : "Off");
  }

  static void active(Button button, boolean on, String state) {
    if (button == null) return;
    button.setSelected(on);
    CharSequence previous = androidx.core.view.ViewCompat.getStateDescription(button);
    if (!state.contentEquals(previous == null ? "" : previous))
      androidx.core.view.ViewCompat.setStateDescription(button, state);
  }

  static Button iconButton(Context c, String glyph, String description, Runnable action) {
    Button button = button(c, glyph, action);
    button.setMinWidth(0);
    button.setMinimumWidth(0);
    button.setPadding(0, 0, 0, 0);
    button.setContentDescription(description);
    button.setTooltipText(description);
    button.setLayoutParams(new LinearLayout.LayoutParams(dp(c, 48), dp(c, 48)));
    return button;
  }

  static LinearLayout row(Context c) {
    LinearLayout l = new LinearLayout(c);
    l.setOrientation(LinearLayout.HORIZONTAL);
    l.setGravity(android.view.Gravity.CENTER_VERTICAL);
    return l;
  }

  static LinearLayout column(Context c) {
    LinearLayout l = new LinearLayout(c);
    l.setOrientation(LinearLayout.VERTICAL);
    return l;
  }

  static HorizontalScrollView scrollRow(Context c, View row) {
    HorizontalScrollView s = new HorizontalScrollView(c);
    s.setHorizontalScrollBarEnabled(true);
    s.addView(row);
    return s;
  }

  interface Level {
    void changed(float value);
  }

  static SeekBar slider(Context c, float value, Level action) {
    SeekBar s = new PlayerSeekBar(c);
    s.setMinHeight(dp(c, 48));
    s.setMax(1000);
    s.setProgress(Math.round(value * 1000));
    s.setOnSeekBarChangeListener(
        new SeekBar.OnSeekBarChangeListener() {
          public void onStartTrackingTouch(SeekBar b) {}

          public void onStopTrackingTouch(SeekBar b) {}

          public void onProgressChanged(SeekBar b, int v, boolean user) {
            if (user) action.changed(v / 1000f);
          }
        });
    return s;
  }

  static EditText input(Context c, String hint) {
    EditText t = new EditText(c);
    t.setTextSize(13);
    t.setTextColor(0xffe8eef7);
    t.setHintTextColor(0xff91a0b7);
    t.setHint(hint);
    t.setBackground(panel(0xff101923, dp(c, 6)));
    t.setPadding(dp(c, 8), dp(c, 5), dp(c, 8), dp(c, 5));
    return t;
  }
}
