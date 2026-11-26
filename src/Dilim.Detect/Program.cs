using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Dilim.Detect;

/// <summary>Prints the faces or lines of text in a picture as [{"x":..,"y":..,"w":..,"h":..}], in its upright pixels.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("faces" or "text") || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("usage: Dilim.Detect faces|text <picture>");
            return 2;
        }
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(args[1]));
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var boxes = args[0] == "faces" ? await FacesAsync(decoder) : await TextAsync(decoder);
            Console.Out.Write(JsonSerializer.Serialize(boxes.Select(b => new { x = b.X, y = b.Y, w = b.W, h = b.H })));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>The picture upright (as its EXIF orientation says), at most <paramref name="maxSide"/> pixels, and the scale back.</summary>
    private static async Task<(SoftwareBitmap Bitmap, double Scale)> UprightAsync(BitmapDecoder decoder, BitmapPixelFormat format, uint maxSide)
    {
        double width = decoder.OrientedPixelWidth, height = decoder.OrientedPixelHeight;
        var scale = Math.Min(1, maxSide / Math.Max(width, height));
        var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
        // ScaledWidth/Height apply before the orientation: give them in the stored (not turned) direction.
        var turned = decoder.OrientedPixelWidth != decoder.PixelWidth;
        transform.ScaledWidth = (uint)Math.Max(1, Math.Round((turned ? height : width) * scale));
        transform.ScaledHeight = (uint)Math.Max(1, Math.Round((turned ? width : height) * scale));
        var bitmap = await decoder.GetSoftwareBitmapAsync(format, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        return (bitmap, scale);
    }

    private static async Task<List<(int X, int Y, int W, int H)>> FacesAsync(BitmapDecoder decoder)
    {
        if (!FaceDetector.IsSupported) throw new NotSupportedException("Face detection is not available on this Windows.");
        var (bitmap, scale) = await UprightAsync(decoder, BitmapPixelFormat.Gray8, 2400);
        using (bitmap)
        {
            var detector = await FaceDetector.CreateAsync();
            // Small faces too: the smallest size Windows allows for this picture.
            detector.MinDetectableFaceSize = new Windows.Graphics.Imaging.BitmapSize { Width = Math.Max(detector.MinDetectableFaceSize.Width, 24), Height = Math.Max(detector.MinDetectableFaceSize.Height, 24) };
            var faces = await detector.DetectFacesAsync(bitmap);
            // A little margin around each face (hair, chin), so the box covers it.
            return faces.Select(f =>
            {
                var b = f.FaceBox;
                var pad = b.Width * 0.18;
                return Box(b.X - pad, b.Y - pad * 1.4, b.Width + 2 * pad, b.Height + pad * 2.4, scale, decoder);
            }).ToList();
        }
    }

    private static async Task<List<(int X, int Y, int W, int H)>> TextAsync(BitmapDecoder decoder)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new NotSupportedException("No text recognition language is installed in Windows.");
        var (bitmap, scale) = await UprightAsync(decoder, BitmapPixelFormat.Bgra8, Math.Min(OcrEngine.MaxImageDimension, 4000));
        using (bitmap)
        {
            var result = await engine.RecognizeAsync(bitmap);
            // A box per line of text: the words of the line together.
            return result.Lines.Where(l => l.Words.Count > 0).Select(line =>
            {
                var left = line.Words.Min(w => w.BoundingRect.X);
                var top = line.Words.Min(w => w.BoundingRect.Y);
                var right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                var bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                var pad = (bottom - top) * 0.2;
                return Box(left - pad, top - pad, right - left + 2 * pad, bottom - top + 2 * pad, scale, decoder);
            }).ToList();
        }
    }

    private static (int X, int Y, int W, int H) Box(double x, double y, double w, double h, double scale, BitmapDecoder decoder)
    {
        double maxW = decoder.OrientedPixelWidth, maxH = decoder.OrientedPixelHeight;
        var left = Math.Clamp(x / scale, 0, maxW - 1);
        var top = Math.Clamp(y / scale, 0, maxH - 1);
        var right = Math.Clamp((x + w) / scale, left + 1, maxW);
        var bottom = Math.Clamp((y + h) / scale, top + 1, maxH);
        return ((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }
}
