// Reduces the size of a PDF the way the "Reduce size" tool shows it: two methods, five levels, a measured size and
// quality for each, and a target size the tool gets as close to as it can from below.
//
// Smart: PdfCompressor scales pictures down to the level's resolution and re-encodes them at its JPEG quality; text,
//        fonts and vector drawings stay as they are (selectable, sharp at any zoom).
// Flatten: every page becomes one JPEG picture at the level's resolution. Text is no longer selectable, but a PDF of
//        heavy vector drawings (plans, maps) gets much smaller.
//
// Sizes are not guessed: each setting is actually applied in memory, so the number shown is the number saved.
// Quality is measured too: a few pages of the original and of the result are rendered at 150 dpi and compared
// (structural similarity), so "Print-sharp" means the pages really look the same at that resolution.

using Dilim.Core.Presets;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PDFtoImage;
using SkiaSharp;

#pragma warning disable CA1416 // PDFtoImage is marked for the platforms PDFium ships for; Dilim only runs on those.

namespace Dilim.Engines.Pdf;

public enum PdfShrinkMethod
{
    Smart,
    Flatten,
}

/// <summary>A resolution and JPEG quality: one of the five levels, or a setting between two of them.</summary>
public sealed record PdfShrinkSetting(int Dpi, int Quality);

/// <summary>What a setting gives: the bytes of the result, and how much it still looks like the original (0–1).</summary>
public sealed record PdfShrinkResult(PdfShrinkMethod Method, PdfShrinkSetting Setting, long Size, double Similarity);

public static class PdfShrinker
{
    /// <summary>Light, Print, Balanced, Screen, Smallest.</summary>
    public static readonly IReadOnlyList<PdfShrinkSetting> Levels =
    [
        new(300, 85),
        new(220, 80),
        new(150, 72),
        new(110, 60),
        new(72, 45),
    ];

    /// <summary>A flattened page never gets more pixels than this (a huge sheet at 300 dpi would need gigabytes).</summary>
    private const double MaxPagePixels = 40_000_000;

    /// <summary>Pages compared for the quality estimate: first, middle, last.</summary>
    private const int ComparedPages = 3;

    private const int CompareDpi = 150;

    /// <summary>Applies <paramref name="setting"/> and returns the new PDF.</summary>
    public static Task<byte[]> ShrinkAsync(string file, PdfShrinkMethod method, PdfShrinkSetting setting, CancellationToken ct,
        IProgress<double>? progress = null) =>
        Task.Run(() => method == PdfShrinkMethod.Smart ? Smart(file, setting, ct) : Flatten(file, setting, progress, ct), ct);

    /// <summary>
    /// Applies <paramref name="setting"/> and measures the result: its size and, with <paramref name="quality"/>, its
    /// similarity to the original (NaN without).
    /// </summary>
    public static async Task<PdfShrinkResult> MeasureAsync(string file, PdfShrinkMethod method, PdfShrinkSetting setting, CancellationToken ct,
        bool quality = true)
    {
        var bytes = await ShrinkAsync(file, method, setting, ct);
        // Never bigger than the original: when shrinking doesn't help, the original is kept as it is.
        var original = new FileInfo(file).Length;
        if (bytes.Length >= original)
            return new PdfShrinkResult(method, setting, original, 1);
        var similarity = quality ? await Task.Run(() => Similarity(file, bytes, ct), ct) : double.NaN;
        return new PdfShrinkResult(method, setting, bytes.Length, similarity);
    }

    /// <summary>
    /// The best setting whose result stays under <paramref name="targetBytes"/>: the levels from best to smallest, then
    /// a few steps between the last level that was too big and the first that fits. Null when even the smallest level
    /// is too big (the caller can say so and keep the smallest).
    /// </summary>
    public static async Task<PdfShrinkResult?> FitAsync(string file, PdfShrinkMethod method, long targetBytes, CancellationToken ct,
        Func<PdfShrinkSetting, Task<PdfShrinkResult>>? measure = null, IProgress<string>? step = null)
    {
        // Sizes only while searching; the quality of the setting it settles on is measured once at the end.
        var sizeOnly = measure is null;
        measure ??= s => MeasureAsync(file, method, s, ct, quality: false);
        PdfShrinkResult? fits = null;
        PdfShrinkSetting? tooBig = null;
        foreach (var level in Levels)
        {
            ct.ThrowIfCancellationRequested();
            var result = await measure(level);
            if (result.Size <= targetBytes) { fits = result; break; }
            tooBig = level;
        }
        if (fits is null || tooBig is null)
            return fits is not null && sizeOnly ? await MeasureAsync(file, method, fits.Setting, ct) : fits;
        // Bisect between the two settings: the closer to the target from below, the better the quality.
        var (low, high) = (fits.Setting, tooBig);
        for (var i = 0; i < 3; i++)
        {
            var middle = new PdfShrinkSetting((low.Dpi + high.Dpi) / 2, (low.Quality + high.Quality) / 2);
            if (middle == low || middle == high) break;
            step?.Report($"{middle.Dpi} dpi");
            var result = await measure(middle);
            if (result.Size <= targetBytes) { fits = result; low = middle; }
            else high = middle;
        }
        return sizeOnly && fits is not null ? await MeasureAsync(file, method, fits.Setting, ct) : fits;
    }

    /// <summary>
    /// A label for a similarity: 4 print-sharp, 3 screen-sharp, 2 readable, 1 draft. Calibrated on photo sheets: Light
    /// and Print come out print-sharp, Balanced screen-sharp, Screen readable, Smallest a draft.
    /// </summary>
    public static int Grade(double similarity) => similarity switch
    {
        >= 0.96 => 4,
        >= 0.93 => 3,
        >= 0.87 => 2,
        _ => 1,
    };

    // ───────── Methods ─────────

    private static byte[] Smart(string file, PdfShrinkSetting setting, CancellationToken ct)
    {
        using var document = PdfSharpConverter.OpenForChanges(file);
        PdfCompressor.Compress(document, PdfCompression.Ebook, keepMetadata: true, ct, setting.Dpi, setting.Quality);
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static byte[] Flatten(string file, PdfShrinkSetting setting, IProgress<double>? progress, CancellationToken ct)
    {
        var source = File.ReadAllBytes(file);
        var sizes = Conversion.GetPageSizes(source, null);
        using var result = new PdfDocument();
        for (var i = 0; i < sizes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (width, height) = (sizes[i].Width, sizes[i].Height); // points
            var dpi = (double)setting.Dpi;
            var pixels = width / 72 * dpi * (height / 72 * dpi);
            if (pixels > MaxPagePixels) dpi *= Math.Sqrt(MaxPagePixels / pixels);
            using var bitmap = Conversion.ToImage(source, page: i, password: null,
                options: new RenderOptions(Dpi: (int)Math.Max(36, dpi), WithAnnotations: true, WithFormFill: true, BackgroundColor: SKColors.White));
            using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, setting.Quality);
            using var stream = new MemoryStream(jpeg.ToArray());
            var page = result.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            using var picture = XImage.FromStream(stream);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(picture, 0, 0, width, height);
            progress?.Report((i + 1.0) / sizes.Count);
        }
        result.Options.CompressContentStreams = true;
        using var output = new MemoryStream();
        result.Save(output, false);
        return output.ToArray();
    }

    // ───────── Quality ─────────

    /// <summary>Renders of the original's compared pages, kept while the tool is open (the original doesn't change).</summary>
    private static readonly Dictionary<string, MagickImage> OriginalPages = new(StringComparer.Ordinal);

    /// <summary>
    /// Mean structural similarity of a few pages. Both sides are rendered at <see cref="CompareDpi"/> and brought to
    /// at most <see cref="ComparePixels"/>: enough to see softened pictures and blurred lines, quick enough to show live.
    /// </summary>
    private static double Similarity(string file, byte[] result, CancellationToken ct)
    {
        var original = File.ReadAllBytes(file);
        var count = Math.Min(Conversion.GetPageCount(original, null), Conversion.GetPageCount(result, null));
        if (count == 0) return 1;
        var pages = new[] { 0, count / 2, count - 1 }.Distinct().Take(ComparedPages).ToArray();
        var stamp = file + "|" + File.GetLastWriteTimeUtc(file).Ticks + "|";
        var total = 0.0;
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            MagickImage a;
            lock (OriginalPages)
            {
                if (!OriginalPages.TryGetValue(stamp + page, out a!))
                {
                    if (OriginalPages.Count > 24) { foreach (var old in OriginalPages.Values) old.Dispose(); OriginalPages.Clear(); }
                    a = Render(original, page);
                    OriginalPages[stamp + page] = a;
                }
            }
            using var b = Render(result, page);
            if (a.Width != b.Width || a.Height != b.Height) b.Resize(new MagickGeometry(a.Width, a.Height) { IgnoreAspectRatio = true });
            total += Ssim(Gray(a), Gray(b), (int)a.Width, (int)a.Height);
        }
        return total / pages.Length;
    }

    private const int ComparePixels = 700_000;

    private static byte[] Gray(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        copy.ColorType = ColorType.Grayscale;
        copy.Depth = 8;
        return copy.ToByteArray(MagickFormat.Gray);
    }

    /// <summary>
    /// Mean structural similarity of two gray pictures of the same size, over 8 × 8 windows every 4 pixels (the
    /// standard constants for 8-bit values). 1 for identical pictures.
    /// </summary>
    internal static double Ssim(byte[] a, byte[] b, int width, int height)
    {
        const int window = 8, step = 4;
        const double c1 = 6.5025, c2 = 58.5225; // (0.01·255)², (0.03·255)²
        if (a.Length < width * height || b.Length < width * height || width < window || height < window) return 1;
        double sum = 0;
        var count = 0;
        for (var y = 0; y + window <= height; y += step)
        for (var x = 0; x + window <= width; x += step)
        {
            double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
            for (var j = 0; j < window; j++)
            {
                var row = (y + j) * width + x;
                for (var i = 0; i < window; i++)
                {
                    double va = a[row + i], vb = b[row + i];
                    sa += va; sb += vb; saa += va * va; sbb += vb * vb; sab += va * vb;
                }
            }
            const double n = window * window;
            double ma = sa / n, mb = sb / n;
            double va2 = saa / n - ma * ma, vb2 = sbb / n - mb * mb, cov = sab / n - ma * mb;
            sum += (2 * ma * mb + c1) * (2 * cov + c2) / ((ma * ma + mb * mb + c1) * (va2 + vb2 + c2));
            count++;
        }
        return count == 0 ? 1 : Math.Clamp(sum / count, 0, 1);
    }

    /// <summary>
    /// The middle of a page rendered for comparison: the central part of the sheet (where drawings and pictures are,
    /// rather than margins), at most about <see cref="ComparePixels"/> pixels at <paramref name="dpi"/>.
    /// </summary>
    internal static MagickImage Render(byte[] pdf, int page, int dpi = CompareDpi)
    {
        var size = Conversion.GetPageSize(pdf, page, null);
        // The share of each side kept, so the part has about ComparePixels at this resolution.
        var full = size.Width / 72 * dpi * (size.Height / 72 * dpi);
        var share = (float)Math.Min(1, Math.Sqrt(ComparePixels / full));
        var bounds = new System.Drawing.RectangleF(size.Width * (1 - share) / 2, size.Height * (1 - share) / 2, size.Width * share, size.Height * share);
        using var bitmap = Conversion.ToImage(pdf, page: page, password: null,
            options: new RenderOptions(Dpi: dpi, WithAnnotations: true, BackgroundColor: SKColors.White, Bounds: bounds, DpiRelativeToBounds: true));
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var image = new MagickImage(png.ToArray());
        image.ColorType = ColorType.TrueColor;
        return image;
    }

    /// <summary>A whole page, for the before/after view (at most 4 megapixels).</summary>
    private static MagickImage RenderWhole(byte[] pdf, int page, int dpi)
    {
        var size = Conversion.GetPageSize(pdf, page, null);
        var pixels = size.Width / 72 * dpi * (size.Height / 72 * dpi);
        if (pixels > 4_000_000) dpi = (int)Math.Max(36, dpi * Math.Sqrt(4_000_000 / pixels));
        using var bitmap = Conversion.ToImage(pdf, page: page, password: null,
            options: new RenderOptions(Dpi: dpi, WithAnnotations: true, BackgroundColor: SKColors.White));
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new MagickImage(png.ToArray());
    }

    /// <summary>
    /// The same part of a page before and after, enlarged: a crop from the middle of <paramref name="page"/> at
    /// 200 dpi, as PNG bytes, for the "Compare quality" view.
    /// </summary>
    public static async Task<(byte[] Before, byte[] After)> CompareAsync(string file, byte[] shrunk, int page, int side, CancellationToken ct) =>
        await Task.Run(() =>
        {
            var original = File.ReadAllBytes(file);
            using var a = RenderWhole(original, page, 200);
            using var b = RenderWhole(shrunk, page, 200);
            if (a.Width != b.Width || a.Height != b.Height) b.Resize(new MagickGeometry(a.Width, a.Height) { IgnoreAspectRatio = true });
            var crop = new MagickGeometry((int)Math.Max(0, (a.Width - side) / 2), (int)Math.Max(0, (a.Height - side) / 2), (uint)Math.Min(side, a.Width), (uint)Math.Min(side, a.Height));
            a.Crop(crop); b.Crop(crop);
            return (a.ToByteArray(MagickFormat.Png), b.ToByteArray(MagickFormat.Png));
        }, ct);
}
