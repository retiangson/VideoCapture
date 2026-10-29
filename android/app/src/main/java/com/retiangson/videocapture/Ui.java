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

  static Button button(Context c, String value, Runnable action) {
    Button b = new Button(c);
    b.setText(value);
    b.setAllCaps(false);
    b.setTextSize(12);
    b.setTextColor(0xffe8eef7);
    b.setMinHeight(dp(c, 36));
    b.setMinimumHeight(dp(c, 36));
    b.setPadding(dp(c, 10), dp(c, 4), dp(c, 10), dp(c, 4));
    b.setBackground(panel(0xff253246, dp(c, 9)));
    LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-2, dp(c, 38));
    p.setMargins(dp(c, 2), dp(c, 2), dp(c, 2), dp(c, 2));
    b.setLayoutParams(p);
    b.setOnClickListener(v -> action.run());
    return b;
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
    s.setHorizontalScrollBarEnabled(false);
    s.addView(row);
    return s;
  }

  interface Level {
    void changed(float value);
  }

  static SeekBar slider(Context c, float value, Level action) {
    SeekBar s = new SeekBar(c);
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
