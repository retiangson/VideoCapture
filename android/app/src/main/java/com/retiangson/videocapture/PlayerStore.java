package com.retiangson.videocapture;

import android.content.Context;
import android.content.SharedPreferences;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import org.json.JSONArray;
import org.json.JSONObject;

final class PlayerStore {
  private final SharedPreferences preferences;
  private JSONObject history;

  PlayerStore(Context context) {
    preferences = context.getSharedPreferences("player", Context.MODE_PRIVATE);
    try {
      String value = preferences.getString("history", "{}");
      history = value.length() <= 1_000_000 ? new JSONObject(value) : new JSONObject();
    } catch (Exception error) {
      history = new JSONObject();
    }
  }

  float level(String name, float fallback) {
    float value = preferences.getFloat(name, fallback);
    return Float.isFinite(value) ? Math.max(0, Math.min(1, value)) : fallback;
  }

  String text(String name, String fallback) {
    return preferences.getString(name, fallback);
  }

  void put(String name, String value) {
    preferences.edit().putString(name, value).apply();
  }

  void putLevel(String name, float value) {
    preferences.edit().putFloat(name, value).apply();
  }

  List<String> list(String name, int max) {
    List<String> values = new ArrayList<>();
    try {
      JSONArray array = new JSONArray(text(name, "[]"));
      for (int i = 0; i < Math.min(max, array.length()); i++) {
        String value = array.optString(i);
        if (!value.isEmpty() && value.length() <= 8192) values.add(value);
      }
    } catch (Exception ignored) {
    }
    return values;
  }

  void list(String name, List<String> values) {
    put(name, new JSONArray(values).toString());
  }

  long resume(String uri, long size, long modified) {
    JSONObject item = history.optJSONObject(uri);
    if (item == null
        || item.optLong("size", -1) != size
        || item.optLong("modified", -1) != modified) return 0;
    long position = item.optLong("position"), duration = item.optLong("duration");
    return duration > 0 && position >= duration - Math.min(10000, duration / 50)
        ? 0
        : Math.max(0, position);
  }

  void bookmark(String uri, long position, long duration, long size, long modified) {
    if (uri == null || position < 0) return;
    try {
      JSONObject item = new JSONObject();
      item.put("position", position);
      item.put("duration", duration);
      item.put("size", size);
      item.put("modified", modified);
      item.put("access", System.currentTimeMillis());
      history.put(uri, item);
      while (history.length() > 1000) {
        String oldest = null;
        long time = Long.MAX_VALUE;
        Iterator<String> keys = history.keys();
        while (keys.hasNext()) {
          String key = keys.next();
          long stamp = history.optJSONObject(key).optLong("access");
          if (stamp < time) {
            oldest = key;
            time = stamp;
          }
        }
        if (oldest != null) history.remove(oldest);
        else break;
      }
      put("history", history.toString());
    } catch (Exception ignored) {
    }
  }
}
