package com.retiangson.videocapture;

import android.os.Handler;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;

final class VideoGestures implements View.OnTouchListener {
  interface Actions {
    void showMenu();

    void hideMenu();

    void toggleBrowser();
  }

  private final Actions actions;
  private final Handler handler;
  private final float slop;
  private long started;
  private float x, y;
  private boolean multiple, moved, held, invalid;
  private final Runnable hold = this::showHeldMenu;

  private void showHeldMenu() {
    if (!multiple && !moved && !invalid) {
      held = true;
      actions.showMenu();
    }
  }

  VideoGestures(View view, Handler handler, Actions actions) {
    this.actions = actions;
    this.handler = handler;
    slop = ViewConfiguration.get(view.getContext()).getScaledTouchSlop();
    view.setOnTouchListener(this);
  }

  @Override
  public boolean onTouch(View view, MotionEvent event) {
    switch (event.getActionMasked()) {
      case MotionEvent.ACTION_DOWN:
        handler.removeCallbacks(hold);
        started = event.getEventTime();
        x = event.getX();
        y = event.getY();
        multiple = moved = held = invalid = false;
        handler.postDelayed(hold, 450);
        return true;
      case MotionEvent.ACTION_POINTER_DOWN:
        multiple = true;
        handler.removeCallbacks(hold);
        if (event.getPointerCount() > 2) invalid = true;
        return true;
      case MotionEvent.ACTION_MOVE:
        if (Math.abs(event.getX() - x) > slop || Math.abs(event.getY() - y) > slop) {
          moved = true;
          handler.removeCallbacks(hold);
        }
        return true;
      case MotionEvent.ACTION_UP:
        handler.removeCallbacks(hold);
        if (!invalid && !moved) {
          if (multiple && event.getEventTime() - started <= 400) actions.toggleBrowser();
          else if (!multiple && !held) actions.hideMenu();
        }
        view.performClick();
        return true;
      case MotionEvent.ACTION_CANCEL:
        invalid = true;
        handler.removeCallbacks(hold);
        return true;
      default:
        return true;
    }
  }

  void cancel() {
    handler.removeCallbacks(hold);
    invalid = true;
  }
}
