using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VideoCapture.Services;

internal sealed record MicrophoneFeedback(int Level, string Text, string Status);

internal static class MicrophoneRecording
{
    public static Task<string> DefaultDeviceNameAsync() => Task.Run(() =>
    {
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        return device.FriendlyName;
    });
    // Capture only. No playback device, monitor, or speaker stream is opened.
    public static Task CaptureAsync(string wave, CancellationToken stop, CancellationToken cancel,
        IProgress<MicrophoneFeedback>? feedback = null) => Task.Run(async () =>
    {
        cancel.ThrowIfCancellationRequested();
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        string deviceName = device.FriendlyName;
        var format = new WaveFormat(16000, 16, 1);
        using var stream = new FileStream(wave, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new WaveFileWriter(stream, format);
        using var capture = new WasapiCapture(device) { WaveFormat = format };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
            int peak = 0;
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(e.Buffer, i)));
            feedback?.Report(new(Math.Min(100, peak * 100 / 32768), "Final text appears after Stop & transcribe.",
                "Listening · " + deviceName + " · offline Whisper transcription after Stop."));
        };
        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is { } error) completed.TrySetException(error);
            else completed.TrySetResult();
        };
        capture.StartRecording();
        using var stopRegistration = stop.Register(capture.StopRecording);
        using var cancelRegistration = cancel.Register(capture.StopRecording);
        try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(AudioTranscription.MaximumSeconds + 10)); }
        catch (TimeoutException) { capture.StopRecording(); throw; }
        cancel.ThrowIfCancellationRequested();
    }, cancel);
}
