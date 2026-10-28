using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VideoCapture.Services;

internal sealed record EmbeddedSubtitleTrack(int StreamIndex, int Ordinal, string Codec, string Language, string Title, bool IsDefault, bool IsForced)
{
    public bool IsText => Codec is "subrip" or "ass" or "ssa" or "mov_text" or "webvtt" or "text";
    public string Name => $"{Language} · {(string.IsNullOrWhiteSpace(Title) ? Codec : Title)}{(IsText ? "" : " · OCR")}";
}

internal sealed class EmbeddedSubtitles
{
    private readonly string _tools;
    public EmbeddedSubtitles(string? tools = null) => _tools = tools ?? Path.Combine(AppContext.BaseDirectory, "Tools", "Media");
    public async Task<IReadOnlyList<EmbeddedSubtitleTrack>> ProbeAsync(string path, CancellationToken token)
    {
        string json = await RunAsync("ffprobe.exe", ["-v", "error", "-protocol_whitelist", "file,pipe,crypto,data", "-select_streams", "s", "-show_entries", "stream=index,codec_name:stream_tags=language,title:stream_disposition=default,forced", "-of", "json", "-i", Path.GetFullPath(path)], 1_000_000, TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var tracks = new List<EmbeddedSubtitleTrack>();
        if (!document.RootElement.TryGetProperty("streams", out var streams)) return tracks;
        foreach (var stream in streams.EnumerateArray().Take(128))
        {
            int index = stream.GetProperty("index").GetInt32();
            if (index < 0 || index > 4096) continue;
            string codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "unknown" : "unknown";
            string language = "und", title = "";
            if (stream.TryGetProperty("tags", out var tags))
            {
                if (tags.TryGetProperty("language", out var l)) language = l.GetString() ?? "und";
                if (tags.TryGetProperty("title", out var t)) title = t.GetString() ?? "";
            }
            bool isDefault = false, forced = false;
            if (stream.TryGetProperty("disposition", out var disposition))
            {
                isDefault = disposition.TryGetProperty("default", out var d) && d.GetInt32() == 1;
                forced = disposition.TryGetProperty("forced", out var f) && f.GetInt32() == 1;
            }
            tracks.Add(new(index, tracks.Count, codec, language, title.Length > 100 ? title[..100] : title, isDefault, forced));
        }
        return tracks;
    }
    public async Task<SubtitleTimeline> ExtractAsync(string path, int streamIndex, CancellationToken token)
    {
        string srt = await RunAsync("ffmpeg.exe", ["-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe,crypto,data", "-i", Path.GetFullPath(path), "-map", $"0:{streamIndex}", "-vn", "-an", "-dn", "-c:s", "srt", "-f", "srt", "pipe:1"], 8_000_000, TimeSpan.FromMinutes(2), token).ConfigureAwait(false);
        return new(SubtitleText.ParseSrt(srt));
    }
    public static EmbeddedSubtitleTrack? Preferred(IReadOnlyList<EmbeddedSubtitleTrack> tracks) => tracks.Where(t => t.IsText)
        .OrderByDescending(t => t.Language is "eng" or "en").ThenBy(t => t.IsForced).ThenByDescending(t => t.IsDefault).FirstOrDefault();

    private async Task<string> RunAsync(string executable, string[] args, int limit, TimeSpan timeout, CancellationToken token)
    {
        string file = Path.Combine(_tools, executable);
        if (!File.Exists(file)) throw new FileNotFoundException("The bundled subtitle reader is missing. Restore the complete app folder.", file);
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = await Task.Run(() => Process.Start(start) ?? throw new IOException("Could not start the subtitle reader."), token).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        async Task<string> ReadAsync(StreamReader reader, int maximum)
        {
            try { return await ReadBoundedAsync(reader, maximum, deadline.Token).ConfigureAwait(false); }
            catch { deadline.Cancel(); throw; }
        }
        var stdout = ReadAsync(process.StandardOutput, limit);
        var stderr = ReadAsync(process.StandardError, 65_536);
        try
        {
            // Observe stream failures concurrently so an oversized output is killed
            // immediately rather than deadlocking on a full pipe.
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
            if (process.ExitCode != 0) throw new IOException("Subtitle extraction failed: " + (await stderr).Trim());
            return await stdout;
        }
        catch (Exception error)
        {
            bool timedOut = !token.IsCancellationRequested && deadline.IsCancellationRequested && error is OperationCanceledException;
            if (!process.HasExited)
            {
                try { process.Kill(true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }
            deadline.Cancel();
            try { await Task.WhenAll(stdout, stderr); } catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException) { }
            token.ThrowIfCancellationRequested();
            if (timedOut) throw new TimeoutException("Subtitle extraction timed out; using visible text instead.");
            throw;
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) return result.ToString();
            if (result.Length + count > limit) throw new InvalidDataException("Subtitle output exceeded the safe size limit.");
            result.Append(buffer, 0, count);
        }
    }
}
