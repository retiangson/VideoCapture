using System.IO;

namespace VideoCapture.Services;

public sealed class RecordingStore
{
    public string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VideoCapture");

    public string CreatePath()
    {
        Directory.CreateDirectory(DirectoryPath);
        return Path.Combine(DirectoryPath, $"Roku-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mkv");
    }

    public static bool HasOutput(string path)
    {
        try { return new FileInfo(path) is { Exists: true, Length: > 1024 }; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string CompletionMessage(string path) => HasOutput(path)
        ? "Saved → " + Path.GetFileName(path)
        : "Recording failed or is empty → " + Path.GetFileName(path);

    // VLC's sout option has its own quoting rules, independent of shell quoting.
    public static string QuoteOutputPath(string path)
    {
        if (path.Contains('"')) throw new ArgumentException("A recording path cannot contain double quotes.", nameof(path));
        return "\"" + Path.GetFullPath(path).Replace('\\', '/') + "\"";
    }
}
