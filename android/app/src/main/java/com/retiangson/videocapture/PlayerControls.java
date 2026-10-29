package com.retiangson.videocapture;

import android.content.Context;
import android.widget.*;

/** Keep controls reachable in short windows without covering the entire video. */
final class PlayerControls extends LinearLayout {
  final LinearLayout content;

  PlayerControls(Context context) {
    super(context);
    setOrientation(VERTICAL);
    content = Ui.column(context);
    ScrollView scroll = new ScrollView(context);
    scroll.setFillViewport(false);
    scroll.addView(content, new ScrollView.LayoutParams(-1, -2));
    addView(scroll, new LinearLayout.LayoutParams(-1, -2));
  }

  @Override
  protected void onMeasure(int widthSpec, int heightSpec) {
    if (MeasureSpec.getMode(heightSpec) != MeasureSpec.UNSPECIFIED) {
      int limit = Math.max(1, Math.round(MeasureSpec.getSize(heightSpec) * .75f));
      heightSpec = MeasureSpec.makeMeasureSpec(limit, MeasureSpec.AT_MOST);
    }
    super.onMeasure(widthSpec, heightSpec);
  }
}
