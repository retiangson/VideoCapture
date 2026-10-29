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

    void toggleFullscreen();
  }

  private final Actions actions;
  private final Handler handler;
  private final float slop, doubleTapSlop;
  private long lastTap;
  private float lastX, lastY;
  private boolean secondTap;
  private long started;
  private float x, y;
  private boolean multiple, moved, held, invalid;
  private final Runnable hold = this::showHeldMenu;

  private void showHeldMenu() {
    if (!multiple && !moved && !invalid) {
      held = true;
      lastTap = 0;
      actions.showMenu();
    }
  }

  VideoGestures(View view, Handler handler, Actions actions) {
    this.actions = actions;
    this.handler = handler;
    slop = ViewConfiguration.get(view.getContext()).getScaledTouchSlop();
    doubleTapSlop = ViewConfiguration.get(view.getContext()).getScaledDoubleTapSlop();
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
        secondTap =
            lastTap > 0
                && started - lastTap <= ViewConfiguration.getDoubleTapTimeout()
                && Math.abs(x - lastX) <= doubleTapSlop
                && Math.abs(y - lastY) <= doubleTapSlop;
        multiple = moved = held = invalid = false;
        handler.postDelayed(hold, 450);
        return true;
      case MotionEvent.ACTION_POINTER_DOWN:
        multiple = true;
        lastTap = 0;
        secondTap = false;
        handler.removeCallbacks(hold);
        if (event.getPointerCount() > 2) invalid = true;
        return true;
      case MotionEvent.ACTION_MOVE:
        if (Math.abs(event.getX() - x) > slop || Math.abs(event.getY() - y) > slop) {
          moved = true;
          lastTap = 0;
          secondTap = false;
          handler.removeCallbacks(hold);
        }
        return true;
      case MotionEvent.ACTION_UP:
        handler.removeCallbacks(hold);
        if (!invalid && !moved) {
          if (multiple && event.getEventTime() - started <= 400) actions.toggleBrowser();
          else if (!multiple && !held) {
            if (secondTap
                && event.getEventTime() - started <= ViewConfiguration.getDoubleTapTimeout()) {
              lastTap = 0;
              actions.toggleFullscreen();
            } else {
              actions.hideMenu();
              lastTap = event.getEventTime();
              lastX = x;
              lastY = y;
            }
          } else lastTap = 0;
        }
        view.performClick();
        return true;
      case MotionEvent.ACTION_CANCEL:
        lastTap = 0;
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
    lastTap = 0;
  }
}
