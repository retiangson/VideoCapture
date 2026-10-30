package com.retiangson.videocapture;

import android.content.Context;
import android.view.Gravity;
import android.widget.*;

/** Discoverable source actions before a file or USB preview is opened. */
final class SourceChooser extends LinearLayout {
  private final TextView heading, description;

  SourceChooser(Context context, Runnable usb, Runnable file) {
    super(context);
    setOrientation(VERTICAL);
    setGravity(Gravity.CENTER);
    setPadding(Ui.dp(context, 12), Ui.dp(context, 8), Ui.dp(context, 12), Ui.dp(context, 8));
    heading = Ui.text(context, "Choose a video source", 21, 0xffe8eef7);
    heading.setGravity(Gravity.CENTER);
    addView(heading, new LayoutParams(-1, -2));
    description =
        Ui.text(
            context,
            "Connect your HDMI capture card and select USB, or open a local video.",
            13,
            0xff91a0b7);
    description.setGravity(Gravity.CENTER);
    description.setPadding(0, Ui.dp(context, 8), 0, Ui.dp(context, 14));
    addView(description, new LayoutParams(-1, -2));
    LinearLayout actions = Ui.row(context);
    Button select = Ui.button(context, "Select USB", usb);
    select.setContentDescription("Select USB capture device");
    select.setBackground(PlayerSkin.surface(Ui.dp(context, 9), true));
    Button open = Ui.button(context, "Open video", file);
    LayoutParams first = new LayoutParams(0, Ui.dp(context, 48), 1);
    first.setMargins(0, 0, Ui.dp(context, 4), 0);
    LayoutParams second = new LayoutParams(0, Ui.dp(context, 48), 1);
    second.setMargins(Ui.dp(context, 4), 0, 0, 0);
    actions.addView(select, first);
    actions.addView(open, second);
    addView(actions, new LayoutParams(-1, -2));
  }

  @Override
  protected void onMeasure(int widthSpec, int heightSpec) {
    boolean compact = MeasureSpec.getSize(heightSpec) < Ui.dp(getContext(), 170);
    heading.setVisibility(compact ? GONE : VISIBLE);
    description.setVisibility(compact ? GONE : VISIBLE);
    super.onMeasure(widthSpec, heightSpec);
  }
}
