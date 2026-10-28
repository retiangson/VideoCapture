using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoCapture.Services;

internal static class VideoClipExporter
{
    public static async Task ExportAsync(PlaybackFile source, long startMs, long endMs, string destination, CancellationToken token = default)
    {
        if (startMs < 0 || endMs - startMs < 100) throw new InvalidOperationException("The video segment is too short to save. Play at least a moment before stopping recording.");
        var current = await Task.Run(() => PlaybackFile.Read(source.Path), token);
        if (current != source) throw new IOException("The source file changed while recording; the clip was not saved.");
        string executable = Path.Combine(AppContext.BaseDirectory, "Tools", "Media", "ffmpeg.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The bundled video clip exporter is missing. Restore the complete app folder.", executable);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial.mkv";
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        string Seconds(long value) => (value / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
        foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-protocol_whitelist", "file,pipe,crypto,data",
            "-ss", Seconds(startMs), "-i", source.Path, "-t", Seconds(endMs - startMs), "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn",
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", temporary }) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            using var process = await Task.Run(() => Process.Start(start) ?? throw new IOException("Could not start the video exporter."), token);
            var errors = DrainErrorsAsync(process.StandardError);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch
            {
                if (!process.HasExited)
                {
                    try { process.Kill(true); } catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync();
                }
                await errors;
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Video clip export exceeded 30 minutes and was stopped.");
            }
            string diagnostics = await errors;
            if (process.ExitCode != 0 || !RecordingStore.HasOutput(temporary)) throw new IOException("Could not save the video clip. " + diagnostics);
            if (await Task.Run(() => PlaybackFile.Read(source.Path), token) != source) throw new IOException("The source file changed during clip export; the clip was not saved.");
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<string> DrainErrorsAsync(StreamReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory());
            if (count == 0) return text.ToString();
            if (text.Length < 32768) text.Append(buffer, 0, Math.Min(count, 32768 - text.Length));
        }
    }
}
