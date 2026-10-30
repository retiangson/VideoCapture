package com.retiangson.videocapture;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.*;
import android.graphics.drawable.*;
import android.view.Gravity;
import android.widget.*;

/** Resolution-independent player surfaces and transport icons; no bitmap assets or render loop. */
final class PlayerSkin {
  static final int ACCENT = 0xff25e685, TEXT = 0xfff1f4f2, MUTED = 0xff9ba8a2;

  static GradientDrawable surface(int radius, boolean active) {
    GradientDrawable d =
        new GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            active ? new int[] {0xff254337, 0xff14291f} : new int[] {0xff343838, 0xff202323});
    d.setCornerRadius(radius);
    d.setStroke(1, active ? ACCENT : 0xff515857);
    return d;
  }

  static Button transport(
      Context c, Icon.Kind kind, String description, Runnable action, boolean hero) {
    Button button = Ui.iconButton(c, "", description, action);
    int size = Ui.dp(c, hero ? 72 : 48);
    GradientDrawable face =
        new GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            hero ? new int[] {0xff0b3c29, 0xff0f201c} : new int[] {0xff3a3e3d, 0xff202323});
    face.setShape(hero ? GradientDrawable.OVAL : GradientDrawable.RECTANGLE);
    face.setCornerRadius(Ui.dp(c, 12));
    face.setStroke(Ui.dp(c, hero ? 2 : 1), hero ? ACCENT : 0xff636b68);
    GradientDrawable lit =
        new GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM, new int[] {0xff128b51, 0xff092e23});
    lit.setShape(hero ? GradientDrawable.OVAL : GradientDrawable.RECTANGLE);
    lit.setCornerRadius(Ui.dp(c, 12));
    lit.setStroke(Ui.dp(c, 2), ACCENT);
    StateListDrawable states = new StateListDrawable();
    states.addState(new int[] {android.R.attr.state_selected}, lit);
    states.addState(new int[] {}, face);
    button.setBackground(new RippleDrawable(ColorStateList.valueOf(0x38ffffff), states, null));
    button.setLayoutParams(new LinearLayout.LayoutParams(size, size));
    button.setElevation(Ui.dp(c, hero ? 5 : 1));
    icon(button, kind, hero ? 30 : 20);
    return button;
  }

  static void icon(Button button, Icon.Kind kind, int dpSize) {
    if (button.getTag() == kind) return;
    button.setTag(kind);
    int size = Ui.dp(button.getContext(), dpSize);
    Icon icon = new Icon(kind, size);
    button.setForeground(icon);
    button.setForegroundGravity(Gravity.CENTER);
    button.setGravity(Gravity.CENTER);
  }

  static final class Icon extends Drawable {
    enum Kind {
      PLAY,
      PAUSE,
      PREVIOUS,
      NEXT,
      STOP,
      RECORD
    }

    private final Kind kind;
    private final int size;
    private int alpha = 255;
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path path = new Path();

    Icon(Kind kind, int size) {
      this.kind = kind;
      this.size = size;
    }

    @Override
    public int getIntrinsicWidth() {
      return size;
    }

    @Override
    public int getIntrinsicHeight() {
      return size;
    }

    @Override
    public void draw(Canvas canvas) {
      int save = canvas.save();
      canvas.translate(getBounds().left, getBounds().top);
      canvas.scale(getBounds().width() / 24f, getBounds().height() / 24f);
      paint.setColor(kind == Kind.RECORD ? 0xffff6474 : TEXT);
      paint.setAlpha(alpha);
      switch (kind) {
        case PAUSE -> {
          canvas.drawRect(5, 3, 10, 21, paint);
          canvas.drawRect(14, 3, 19, 21, paint);
        }
        case STOP -> canvas.drawRoundRect(5, 5, 19, 19, 1, 1, paint);
        case RECORD -> canvas.drawCircle(12, 12, 7, paint);
        case PLAY, PREVIOUS, NEXT -> {
          boolean previous = kind == Kind.PREVIOUS;
          path.rewind();
          path.moveTo(previous ? 18 : 6, 3);
          path.lineTo(previous ? 5 : 19, 12);
          path.lineTo(previous ? 18 : 6, 21);
          path.close();
          canvas.drawPath(path, paint);
          if (kind == Kind.PREVIOUS) canvas.drawRect(2, 3, 4, 21, paint);
          if (kind == Kind.NEXT) canvas.drawRect(20, 3, 22, 21, paint);
        }
      }
      canvas.restoreToCount(save);
    }

    @Override
    public void setAlpha(int alpha) {
      this.alpha = alpha;
      invalidateSelf();
    }

    @Override
    public void setColorFilter(ColorFilter filter) {
      paint.setColorFilter(filter);
      invalidateSelf();
    }

    @Override
    public int getOpacity() {
      return PixelFormat.TRANSLUCENT;
    }
  }
}
