using System.IO;
using LibVLCSharp.Shared;

namespace VideoCapture.Services;

/// <summary>Owns the native player and media; callers serialize lifecycle changes.</summary>
public sealed class PlaybackSession : IDisposable
{
    private readonly LibVLC _engine;
    private Media? _media;
    public MediaPlayer Player { get; }
    public VideoFrameBuffer Frames { get; }
    public bool IsLive { get; private set; }
    public string? RecordingPath { get; private set; }
    public bool IsRecording => RecordingPath is not null;
    public bool HasMedia => _media is not null;
    public bool UsesSoftwarePreview { get; }
    public long DisplayedFrames => UsesSoftwarePreview ? Frames.FrameCount : _media?.Statistics.DisplayedPictures ?? 0;

    public PlaybackSession(params string[] engineOptions) : this(true, engineOptions) { }

    public PlaybackSession(bool softwarePreview, params string[] engineOptions)
    {
        UsesSoftwarePreview = softwarePreview;
        Core.Initialize();
        var options = new List<string> { "--no-video-title-show", "--no-snapshot-preview" };
        // Portable/dev installs may omit VLC's plugin index. Generate it once;
        // subsequent instances validate/reuse it instead of loading every DLL.
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins", "plugins.dat")))
            options.Add("--reset-plugins-cache");
        _engine = new LibVLC(options.Concat(engineOptions).ToArray());
        Player = new MediaPlayer(_engine) { Volume = 75 };
        Player.EnableKeyInput = false;
        Player.EnableMouseInput = false;
        Frames = new VideoFrameBuffer(GetVideoInfo);
        if (softwarePreview) Frames.Attach(Player);
    }

    public void OpenFile(string path, string? recordingPath = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The movie file no longer exists.", path);
        Stop();
        var media = new Media(_engine, new Uri(Path.GetFullPath(path)));
        ConfigureRecording(media, recordingPath);
        Play(media);
    }

    public void OpenCapture(string video, string? audio, string? recordingPath = null, CaptureMode? mode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(video);
        string capturePlugin = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins", "access", "libdshow_plugin.dll");
        if (!File.Exists(capturePlugin))
            throw new FileNotFoundException("The DirectShow capture plug-in is missing from this app. Restore the complete application folder; reinstalling the camera will not fix this packaging error.", capturePlugin);
        Stop();
        var media = new Media(_engine, "dshow://", FromType.FromLocation);
        media.AddOption($":dshow-vdev={video}");
        media.AddOption($":dshow-adev={audio ?? "none"}");
        if (mode is not null)
        {
            media.AddOption($":dshow-size={mode.Width}x{mode.Height}");
            media.AddOption($":dshow-fps={mode.FpsOption}");
            if (mode.Chroma != "Auto") media.AddOption($":dshow-chroma={mode.Chroma}");
            // UVC cards can advertise stale 4:3 aspect metadata even for a square-pixel 1080p mode.
            media.AddOption($":dshow-aspect-ratio={mode.Width}:{mode.Height}");
        }
        media.AddOption(":live-caching=80");
        ConfigureRecording(media, recordingPath);
        IsLive = true;
        Play(media);
    }

    private void ConfigureRecording(Media media, string? recordingPath)
    {
        if (recordingPath is not null)
        {
            // Display the original input; encode only the file branch to avoid an encode/decode round trip in preview.
            string output = RecordingStore.QuoteOutputPath(recordingPath);
            string fileBranch = "transcode{vcodec=h264,venc=x264{profile=high,preset=ultrafast,tune=zerolatency},vb=8000,acodec=mp4a,ab=192}:std{access=file,mux=mkv,dst=" + output + "}";
            // The duplicate option parses the chain once; std parses the embedded path a second time.
            string quotedBranch = "\"" + fileBranch.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            media.AddOption(":sout=#duplicate{dst=display,dst=" + quotedBranch + "}");
            media.AddOption(":no-sout-all");
        }
        RecordingPath = recordingPath;
    }

    public VideoSourceInfo? GetVideoInfo()
    {
        if (_media is null) return null;
        foreach (var track in _media.Tracks)
        {
            if (track.TrackType != TrackType.Video) continue;
            var video = track.Data.Video;
            if (video.Width == 0 || video.Height == 0) continue;
            double width = video.Width, height = video.Height;
            if (!IsLive && video.SarNum > 0 && video.SarDen > 0) width *= (double)video.SarNum / video.SarDen;
            if ((int)video.Orientation >= 4) (width, height) = (height, width);
            return new(video.Width, video.Height,
                video.FrameRateDen > 0 ? (double)video.FrameRateNum / video.FrameRateDen : 0, width / height, (int)video.Orientation >= 4);
        }
        return null;
    }

    private void Play(Media media)
    {
        _media = media;
        Player.EnableHardwareDecoding = !UsesSoftwarePreview;
        if (!Player.Play(media))
        {
            Stop();
            throw new InvalidOperationException("The media engine could not start playback.");
        }
    }

    public void Stop()
    {
        Player.Stop();
        Player.AspectRatio = null;
        Player.CropGeometry = "";
        Player.Media = null;
        _media?.Dispose();
        _media = null;
        RecordingPath = null;
        IsLive = false;
    }

    public void Dispose()
    {
        Stop();
        Player.Dispose();
        Frames.Dispose();
        _engine.Dispose();
    }
}

public sealed record VideoSourceInfo(uint Width, uint Height, double FramesPerSecond, double DisplayAspectRatio, bool IsTransposed);
