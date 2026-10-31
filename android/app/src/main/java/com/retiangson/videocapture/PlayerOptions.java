package com.retiangson.videocapture;

import android.content.Context;
import android.view.View;
import android.widget.PopupMenu;
import java.util.List;

/** Short-lived snapshots of configuration state; actions remain owned by their feature. */
final class PlayerOptions {
  record Option(String label, MenuIcons.Kind icon, Runnable action, Boolean checked) {
    Option(String label, MenuIcons.Kind icon, Runnable action) {
      this(label, icon, action, null);
    }
  }

  static PopupMenu show(Context context, View anchor, List<Option> options, Runnable changed) {
    PopupMenu popup = new PopupMenu(context, anchor);
    popup.setForceShowIcon(true);
    for (int index = 0; index < options.size(); index++) {
      Option option = options.get(index);
      android.view.MenuItem item = popup.getMenu().add(0, index, index, option.label());
      item.setIcon(new MenuIcons(context, option.icon()));
      if (option.checked() != null) item.setCheckable(true).setChecked(option.checked());
    }
    popup.setOnMenuItemClickListener(
        item -> {
          options.get(item.getItemId()).action().run();
          changed.run();
          return true;
        });
    popup.show();
    return popup;
  }
}
