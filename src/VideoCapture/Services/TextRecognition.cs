using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TesseractOCR;
using TesseractOCR.Enums;

namespace VideoCapture.Services;

internal static class TextRecognition
{
    public static Task<string> ReadAsync(BitmapSource source)
    {
        // Freeze an independent copy before crossing to a worker thread.
        BitmapSource image = source.Clone();
        if (image.PixelWidth > 4096 || image.PixelHeight > 4096)
        {
            double scale = 4096d / Math.Max(image.PixelWidth, image.PixelHeight);
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }
        image.Freeze();
        return Task.Run(() =>
        {
            string data = Path.Combine(AppContext.BaseDirectory, "tessdata");
            if (!File.Exists(Path.Combine(data, "eng.traineddata")))
                throw new FileNotFoundException("The English OCR model is missing. Restore the complete application folder.");
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
            using var engine = new Engine(data, Language.English, EngineMode.LstmOnly);
            using var pixels = TesseractOCR.Pix.Image.LoadFromMemory(stream.ToArray());
            using var page = engine.Process(pixels, PageSegMode.SparseText);
            return page.Text.Trim();
        });
    }
}
