package com.retiangson.videocapture;

import android.hardware.usb.UsbDevice;
import android.view.Surface;
import com.herohan.uvcapp.CameraException;
import com.herohan.uvcapp.CameraHelper;
import com.herohan.uvcapp.ICameraHelper;
import com.serenegiant.usb.Size;
import java.util.Collections;
import java.util.List;

final class UsbCapture implements AutoCloseable, VideoSurfaceSource {
  interface Listener {
    void attached(UsbDevice device);

    void opened(Size mode);

    void disconnected();

    void error(String message);
  }

  private final CameraHelper camera = new CameraHelper();
  private final Listener listener;
  private volatile java.util.concurrent.CountDownLatch released =
      new java.util.concurrent.CountDownLatch(1);
  private Surface preview;
  private UsbDevice selected;
  private Size requested;
  private volatile boolean closed;
  private boolean opening;

  UsbCapture(Listener listener) {
    this.listener = listener;
    camera.setStateCallback(
        new ICameraHelper.StateCallback() {
          public void onAttach(UsbDevice device) {
            if (!closed) listener.attached(device);
          }

          public void onDeviceOpen(UsbDevice device, boolean first) {
            if (!closed && selected != null && device.equals(selected)) {
              if (requested != null) camera.openCamera(requested);
              else camera.openCamera();
            }
          }

          public void onCameraOpen(UsbDevice device) {
            if (closed) return;
            opening = false;
            camera.startPreview();
            if (preview != null) camera.addSurface(preview, false);
            listener.opened(camera.getPreviewSize());
          }

          public void onCameraClose(UsbDevice device) {
            released.countDown();
            if (preview != null) camera.removeSurface(preview);
            if (!closed) listener.disconnected();
          }

          public void onDeviceClose(UsbDevice device) {
            released.countDown();
            if (!closed) listener.disconnected();
          }

          public void onDetach(UsbDevice device) {
            if (!closed && selected != null && selected.equals(device)) {
              opening = false;
              listener.disconnected();
            }
          }

          public void onCancel(UsbDevice device) {
            if (closed) return;
            opening = false;
            listener.error("USB permission was not granted.");
          }

          public void onError(UsbDevice device, CameraException error) {
            if (closed) return;
            opening = false;
            listener.error("USB capture: " + error.getMessage());
          }
        });
  }

  List<UsbDevice> devices() {
    List<UsbDevice> devices = camera.getDeviceList();
    return devices == null ? Collections.emptyList() : devices;
  }

  void select(UsbDevice device) {
    if (closed || opening) return;
    selected = device;
    opening = true;
    camera.selectDevice(device);
  }

  boolean isOpen() {
    return !closed && camera.isCameraOpened();
  }

  List<Size> modes() {
    List<Size> modes = camera.getSupportedSizeList();
    return modes == null ? Collections.emptyList() : modes;
  }

  void mode(Size mode) {
    requested = mode.clone();
    if (isOpen()) camera.setPreviewSize(requested);
  }

  void surface(Surface surface) {
    if (preview != null) camera.removeSurface(preview);
    preview = surface;
    if (surface != null && isOpen()) camera.addSurface(surface, false);
  }

  public void encoderSurface(Surface surface, boolean add) {
    if (add) camera.addSurface(surface, true);
    else camera.removeSurface(surface);
  }

  @Override
  public void close() {
    if (closed) return;
    released = new java.util.concurrent.CountDownLatch(1);
    closed = true;
    opening = false;
    boolean opened = camera.isCameraOpened();
    camera.release();
    if (opened)
      try {
        released.await(3, java.util.concurrent.TimeUnit.SECONDS);
      } catch (InterruptedException e) {
        Thread.currentThread().interrupt();
      }
    preview = null;
  }
}
