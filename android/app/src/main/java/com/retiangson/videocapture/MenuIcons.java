package com.retiangson.videocapture;

import android.content.Context;
import android.graphics.*;
import android.graphics.drawable.Drawable;
import android.view.Gravity;
import android.widget.Button;
import androidx.core.graphics.PathParser;

/** Small consistent outline icons with accessible text labels supplied by their buttons. */
final class MenuIcons extends Drawable {
  enum Kind {
    OPEN("M3,7H10L12,4H21V19H3Z M3,10H21"),
    PLAYLIST("M4,6H16 M4,11H16 M4,16H11 M18,13V21 M14,17H22"),
    DELETE("M5,7H19 M9,4H15 M7,7V21H17V7 M10,10V18 M14,10V18"),
    RECORD("M12,4A8,8,0,1,1,12,20A8,8,0,1,1,12,4"),
    USB("M12,21V3 M9,6L12,3L15,6 M12,15L6,11V8 M12,12L18,8V5 M4,6H8V8H4Z M16,3H20V5H16Z"),
    FRAME("M3,7H8L10,4H15L17,7H21V20H3Z M16,13A4,4,0,1,1,8,13A4,4,0,1,1,16,13Z"),
    SAVE("M4,3H18L21,6V21H3V3Z M7,3V9H17V3 M7,14H17V21H7Z"),
    COPY("M8,7H21V21H8Z M16,7V3H3V16H8"),
    SETTINGS("M4,6H20 M4,12H20 M4,18H20 M8,3V9 M16,9V15 M10,15V21"),
    AUDIO("M4,9H8L13,4V20L8,15H4Z M17,8Q22,12,17,16"),
    MUSIC(
        "M9,18V5L20,3V16 M9,8L20,6 M9,18A3,2,0,1,1,3,18A3,2,0,1,1,9,18"
            + " M20,16A3,2,0,1,1,14,16A3,2,0,1,1,20,16"),
    PICTURE("M3,4H21V20H3Z M3,17L9,10L14,15L17,11L21,16"),
    BROWSER("M3,4H21V20H3Z M3,9H21 M6,6H7 M10,6H11"),
    TEXT("M3,6H21 M12,6V20 M8,20H16"),
    SUBTITLE("M3,4H21V19H3Z M6,13H11 M14,13H18 M6,16H9 M12,16H18"),
    READ("M3,4Q8,2,12,5Q17,2,21,4V20Q16,18,12,21Q8,18,3,20Z M12,5V21"),
    MIC("M9,5A3,3,0,0,1,15,5V12A3,3,0,0,1,9,12Z M6,11V12A6,6,0,0,0,18,12V11 M12,18V22 M9,22H15"),
    INFO("M12,2A10,10,0,1,1,12,22A10,10,0,1,1,12,2 M12,10V17 M12,6V7"),
    TIMELINE("M3,6H21V18H3Z M6,10H18 M6,14H18 M10,8V16"),
    FULLSCREEN("M3,9V3H9 M15,3H21V9 M21,15V21H15 M9,21H3V15"),
    RESTORE("M3,8H8V3 M16,3V8H21 M21,16H16V21 M8,21V16H3"),
    LOCK("M7,10V7A5,5,0,0,1,17,7V10 M5,10H19V22H5Z M12,14V18"),
    UNLOCK("M7,10V7A5,5,0,0,1,17,7 M5,10H19V22H5Z M12,14V18"),
    HIDE("M6,8L12,14L18,8"),
    BRIGHTNESS(
        "M12,7A5,5,0,1,1,12,17A5,5,0,1,1,12,7 M12,1V3 M12,21V23 M1,12H3 M21,12H23 M4,4L6,6"
            + " M18,18L20,20 M4,20L6,18 M18,6L20,4");

    final String path;

    Kind(String path) {
      this.path = path;
    }
  }

  private final Path path;
  private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
  private final int size;

  MenuIcons(Context context, Kind kind) {
    size = Ui.dp(context, 20);
    path = PathParser.createPathFromPathData(kind.path);
    paint.setColor(kind == Kind.RECORD ? 0xffff6474 : PlayerSkin.TEXT);
    paint.setStyle(Paint.Style.STROKE);
    paint.setStrokeWidth(1.6f);
    paint.setStrokeCap(Paint.Cap.ROUND);
    paint.setStrokeJoin(Paint.Join.ROUND);
  }

  static Button action(Context context, String label, Kind icon, Runnable action) {
    Button button = Ui.button(context, label, action);
    MenuIcons image = new MenuIcons(context, icon);
    image.setBounds(0, 0, image.size, image.size);
    button.setCompoundDrawablesRelative(image, null, null, null);
    button.setCompoundDrawablePadding(Ui.dp(context, 7));
    return button;
  }

  static Button icon(Context context, Kind kind, String label, Runnable action) {
    Button button = Ui.iconButton(context, "", label, action);
    setIcon(button, kind);
    return button;
  }

  static void setIcon(Button button, Kind kind) {
    if (button.getTag() == kind) return;
    button.setTag(kind);
    button.setForeground(new MenuIcons(button.getContext(), kind));
    button.setForegroundGravity(Gravity.CENTER);
  }

  @Override
  public void draw(Canvas canvas) {
    int save = canvas.save();
    canvas.translate(getBounds().left, getBounds().top);
    canvas.scale(getBounds().width() / 24f, getBounds().height() / 24f);
    canvas.drawPath(path, paint);
    canvas.restoreToCount(save);
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
  public void setAlpha(int alpha) {
    paint.setAlpha(alpha);
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
