package com.retiangson.videocapture;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.drawable.*;
import android.view.*;
import android.widget.SeekBar;

/** A native accessible SeekBar that also jumps directly to a tapped value inside scroll views. */
@android.annotation.SuppressLint("AppCompatCustomView")
final class PlayerSeekBar extends SeekBar {
  private OnSeekBarChangeListener listener;
  private boolean tracking;

  PlayerSeekBar(Context context) {
    super(context);
    setMinHeight(Ui.dp(context, 48));
    setPadding(Ui.dp(context, 10), 0, Ui.dp(context, 10), 0);
    setProgressTintList(ColorStateList.valueOf(PlayerSkin.ACCENT));
    setProgressBackgroundTintList(ColorStateList.valueOf(0xff101514));
    GradientDrawable thumb = PlayerSkin.surface(Ui.dp(context, 4), true);
    thumb.setSize(Ui.dp(context, 18), Ui.dp(context, 14));
    setThumb(thumb);
    setThumbOffset(Ui.dp(context, 9));
  }

  @Override
  public void setOnSeekBarChangeListener(OnSeekBarChangeListener listener) {
    this.listener = listener;
    super.setOnSeekBarChangeListener(listener);
  }

  @Override
  public boolean onTouchEvent(MotionEvent event) {
    if (!isEnabled()) {
      if (tracking) finishTouch(false);
      return false;
    }
    int action = event.getActionMasked();
    if (action == MotionEvent.ACTION_DOWN) {
      tracking = true;
      setPressed(true);
      requestFocus();
      if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
      if (listener != null) listener.onStartTrackingTouch(this);
    }
    if (!tracking) return false;
    if (action != MotionEvent.ACTION_CANCEL) {
      float width = Math.max(1, getWidth() - getPaddingLeft() - getPaddingRight());
      float fraction = Math.max(0, Math.min(1, (event.getX() - getPaddingLeft()) / width));
      if (getLayoutDirection() == View.LAYOUT_DIRECTION_RTL) fraction = 1 - fraction;
      int value = getMin() + Math.round(fraction * (getMax() - getMin()));
      setProgress(value);
      if (listener != null) listener.onProgressChanged(this, value, true);
    }
    if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) {
      finishTouch(action == MotionEvent.ACTION_UP);
    }
    return true;
  }

  private void finishTouch(boolean clicked) {
    tracking = false;
    setPressed(false);
    if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(false);
    if (listener != null) listener.onStopTrackingTouch(this);
    if (clicked) performClick();
  }

  @Override
  public boolean performClick() {
    return super.performClick();
  }
}
