using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Speech.Recognition;
using System.Text;
using NAudio.Wave;

namespace VideoCapture.Services;

internal static class WhisperTranscription
{
    internal const string ModelName = "ggml-small-q5_1.bin";
    private const string ModelHash = "AE85E4A935D7A567BD102FE55AFC16BB595BDB618E11B2FC7591BC08120411BB";
    private static readonly Lazy<Task> VerifiedModel = new(() => Task.Run(() =>
    {
        using var model = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Models", ModelName));
        if (Convert.ToHexString(SHA256.HashData(model)) != ModelHash) throw new InvalidDataException("The offline speech model is damaged. Restore it with tools/Setup-SpeechModel.ps1.");
    }));
    internal static string Language(string id) => id.StartsWith("whisper:", StringComparison.Ordinal) ? id[8..]
        : SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Id == id)?.Culture.TwoLetterISOLanguageName ?? "auto";

    public static async Task<string> RecognizeAsync(string wave, string languageId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await VerifiedModel.Value.WaitAsync(token);
        // Avoid sending digital silence into a decoder that could hallucinate words.
        bool audible = await Task.Run(() =>
        {
            using var reader = new WaveFileReader(wave);
            if (reader.WaveFormat.BitsPerSample != 16 || reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
                throw new InvalidDataException("Speech input must be 16-bit PCM audio.");
            var buffer = new byte[8192];
            int count, active = 0;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                for (int i = 0; i + 1 < count; i += 2) if (Math.Abs((int)BitConverter.ToInt16(buffer, i)) > 100 && ++active >= 160) return true;
            }
            return false;
        }, token);
        if (!audible) return "";
        string language = Language(languageId);
        if (language != "auto" && (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetter)))
            throw new ArgumentException("Unsupported speech language.", nameof(languageId));
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Tools", "Media", "ffmpeg.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Models")
        };
        // Fixed relative model name and stdout destination avoid embedding user paths in a filter expression.
        string filter = $"whisper=model={ModelName}:language={language}:queue=20:use_gpu=false:destination='pipe\\:1':format=text";
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe", "-i", Path.GetFullPath(wave),
            "-ar", "16000", "-ac", "1", "-af", filter, "-f", "null", "-" }) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = Process.Start(start) ?? throw new IOException("Could not start offline Whisper transcription.");
        var output = ReadBoundedAsync(process.StandardOutput);
        var errors = ReadBoundedAsync(process.StandardError);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) when (process.HasExited) { } }
            await process.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("Offline Whisper transcription timed out.");
        }
        string text = await output, diagnostics = await errors;
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException("Offline Whisper failed. " + diagnostics);
        return text.Trim();
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
            if (result.Length < 65536) result.Append(buffer, 0, Math.Min(count, 65536 - result.Length));
        return result.ToString();
    }
}
