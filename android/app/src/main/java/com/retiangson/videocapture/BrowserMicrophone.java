package com.retiangson.videocapture;

import android.app.*;
import android.net.Uri;
import android.webkit.*;
import androidx.webkit.*;
import java.util.*;
import java.util.function.Consumer;
import org.json.JSONObject;

/** Exact-origin bridge carrying microphone lifecycle signals only, never browser audio/content. */
final class BrowserMicrophone {
  interface Host {
    void prepare(Consumer<Boolean> ready);

    void released();

    void feedback(String message);

    boolean active(WebView view);
  }

  private static final Set<String> ORIGINS =
      Set.of("https://chatgpt.com", "https://chat.openai.com");
  private final Activity activity;
  private final Host host;
  private final Set<WebView> installed = Collections.newSetFromMap(new IdentityHashMap<>());
  private final Set<WebView> approved = Collections.newSetFromMap(new IdentityHashMap<>());
  private final Map<WebView, Set<String>> requests = new IdentityHashMap<>();
  private final Map<WebView, AlertDialog> dialogs = new IdentityHashMap<>();

  BrowserMicrophone(Activity activity, Host host) {
    this.activity = activity;
    this.host = host;
  }

  void install(WebView view) {
    if (!WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)
        || !WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) return;
    WebViewCompat.addWebMessageListener(
        view,
        "__vcMicGate",
        ORIGINS,
        (source, message, origin, mainFrame, reply) -> {
          if (!host.active(view)
              || !BrowserOverlay.trusted(origin.toString())
              || !BrowserOverlay.trusted(view.getUrl())
              || !origin.getHost().equalsIgnoreCase(Uri.parse(view.getUrl()).getHost())) return;
          try {
            if (message.getType() != WebMessageCompat.TYPE_STRING) return;
            String data = message.getData();
            if (data == null || data.length() > 256) return;
            JSONObject value = new JSONObject(data);
            String id = value.optString("id"), type = value.optString("type");
            if (!id.matches("[a-zA-Z0-9.-]{1,80}")) return;
            if ("end".equals(type)) {
              end(view, id);
              return;
            }
            if (!"request".equals(type)) return;
            Set<String> pending = requests.computeIfAbsent(view, key -> new HashSet<>());
            if (pending.size() >= 16 || !pending.add(id)) {
              reply.postMessage("{\"id\":" + JSONObject.quote(id) + ",\"allowed\":false}");
              return;
            }
            permission(
                view,
                allowed -> {
                  boolean valid =
                      allowed
                          && host.active(view)
                          && requests.getOrDefault(view, Set.of()).contains(id);
                  try {
                    reply.postMessage(
                        "{\"id\":" + JSONObject.quote(id) + ",\"allowed\":" + valid + "}");
                  } catch (IllegalStateException ignored) {
                    valid = false;
                  }
                  if (!valid) end(view, id);
                });
          } catch (org.json.JSONException | IllegalArgumentException ignored) {
          }
        });
    WebViewCompat.addDocumentStartJavaScript(view, SCRIPT, ORIGINS);
    installed.add(view);
  }

  void permission(WebView view, Consumer<Boolean> ready) {
    if (!installed.contains(view) || !host.active(view) || !BrowserOverlay.trusted(view.getUrl())) {
      host.feedback("Update Android System WebView to use secure browser microphone handoff.");
      ready.accept(false);
      return;
    }
    if (approved.contains(view)) {
      host.prepare(ready);
      return;
    }
    if (dialogs.containsKey(view)) {
      ready.accept(false);
      return;
    }
    AlertDialog dialog =
        new AlertDialog.Builder(activity)
            .setTitle("Allow browser microphone?")
            .setMessage(
                Uri.parse(view.getUrl()).getHost()
                    + " can send your microphone audio to its service when you start voice capture."
                    + " Its privacy policy applies. VideoCapture will temporarily release its audio"
                    + " inputs and pause video sound/subtitle speech to avoid conflicts and echo.")
            .setPositiveButton(
                "Allow microphone",
                (d, which) -> {
                  dialogs.remove(view);
                  if (!host.active(view)) {
                    ready.accept(false);
                    return;
                  }
                  approved.add(view);
                  host.prepare(ready);
                })
            .setNegativeButton(
                "Not now",
                (d, which) -> {
                  dialogs.remove(view);
                  ready.accept(false);
                })
            .setOnCancelListener(
                d -> {
                  dialogs.remove(view);
                  ready.accept(false);
                })
            .create();
    dialogs.put(view, dialog);
    dialog.show();
  }

  private void end(WebView view, String id) {
    Set<String> pending = requests.get(view);
    if (pending != null) {
      pending.remove(id);
      if (pending.isEmpty()) requests.remove(view);
    }
    if (requests.isEmpty()) host.released();
  }

  void stop(WebView view) {
    if (installed.contains(view)) view.evaluateJavascript("window.__vcStopMic?.()", null);
    navigation(view);
  }

  void navigation(WebView view) {
    approved.remove(view);
    AlertDialog dialog = dialogs.remove(view);
    if (dialog != null) dialog.cancel();
    requests.remove(view);
    if (requests.isEmpty()) host.released();
  }

  void stopAll() {
    for (WebView view : List.copyOf(installed)) stop(view);
  }

  void remove(WebView view) {
    stop(view);
    installed.remove(view);
  }

  private static final String SCRIPT =
      """
      (()=>{
        const media=navigator.mediaDevices, bridge=window.__vcMicGate;
        if(!media||!bridge||window.__vcStopMic)return;
        const original=media.getUserMedia.bind(media), pending=new Map(), streams=new Map();
        const prefix=Array.from(crypto.getRandomValues(new Uint32Array(3))).map(v=>v.toString(16)).join('-');
        let next=0;
        const post=(type,id)=>bridge.postMessage(JSON.stringify({type,id}));
        bridge.onmessage=e=>{try{const m=JSON.parse(e.data),p=pending.get(m.id);
          if(p){clearTimeout(p.timer);pending.delete(m.id);p.resolve(m.allowed===true)}}catch{}};
        const finish=id=>{if(streams.delete(id))post('end',id)};
        media.getUserMedia=async constraints=>{
          if(!constraints?.audio)return original(constraints);
          const id=prefix+'.'+(++next);
          const allowed=await new Promise(resolve=>{
            const timer=setTimeout(()=>{pending.delete(id);post('end',id);resolve(false)},30000);
            pending.set(id,{resolve,timer});post('request',id);
          });
          if(!allowed)throw new DOMException('Microphone access was not granted','NotAllowedError');
          try{
            const stream=await original(constraints);streams.set(id,stream);
            const ended=()=>{if(stream.getAudioTracks().every(t=>t.readyState==='ended'))finish(id)};
            for(const track of stream.getAudioTracks()){
              track.addEventListener('ended',ended);
              const stop=track.stop.bind(track);
              track.stop=()=>{stop();ended()};
            }
            return stream;
          }catch(error){post('end',id);throw error}
        };
        Object.defineProperty(window,'__vcStopMic',{value:()=>{
          for(const [id,stream] of [...streams]){for(const track of stream.getAudioTracks())track.stop();finish(id)}
          for(const [id,p] of [...pending]){clearTimeout(p.timer);pending.delete(id);post('end',id);p.resolve(false)}
          for(let i=0;i<window.frames.length;i++){try{window.frames[i].__vcStopMic?.()}catch{}}
        }});
        addEventListener('pagehide',()=>window.__vcStopMic());
      })();
      """;
}
