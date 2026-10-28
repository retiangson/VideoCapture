using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TesseractOCR;
using TesseractOCR.Enums;

namespace VideoCapture.Services;

internal sealed class SubtitleOcr : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Engine? _engine;
    private bool _disposed;
    public Task<(string Text, float Confidence)> ReadAsync(BitmapSource image, double bottomFraction, CancellationToken token)
    {
        int height = Math.Max(1, (int)(image.PixelHeight * Math.Clamp(bottomFraction, 0.2, 1)));
        BitmapSource crop = new CroppedBitmap(image, new Int32Rect(0, image.PixelHeight - height, image.PixelWidth, height));
        if (crop.PixelWidth > 1920) crop = new TransformedBitmap(crop, new ScaleTransform(1920d / crop.PixelWidth, 1920d / crop.PixelWidth));
        crop.Freeze();
        return Task.Run(() =>
        {
            _gate.Wait(token);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                token.ThrowIfCancellationRequested();
                _engine ??= new Engine(Path.Combine(AppContext.BaseDirectory, "tessdata"), Language.English, EngineMode.LstmOnly);
                using var memory = new MemoryStream();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop)); encoder.Save(memory);
                using var pixels = TesseractOCR.Pix.Image.LoadFromMemory(memory.ToArray());
                using var page = _engine.Process(pixels, PageSegMode.SparseText);
                token.ThrowIfCancellationRequested();
                return (page.Text.Trim(), (float)page.MeanConfidence);
            }
            finally { _gate.Release(); }
        }, token);
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _disposed = true; _engine?.Dispose(); _engine = null; }
        finally { _gate.Release(); }
    }
}
