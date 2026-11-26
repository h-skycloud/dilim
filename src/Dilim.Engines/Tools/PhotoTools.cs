// Annotations drawn onto a picture, and the adjustments of "Edit photo". Both work on the upright picture in its own
// pixels: the windows show the same thing on a smaller copy and save it at full size.

using ImageMagick;
using ImageMagick.Drawing;

namespace Dilim.Engines.Tools;

public enum AnnotationKind { Arrow, Pen, Text, Rectangle, Ellipse, Highlight, Number, Magnifier, Picture }

/// <summary>
/// One mark on a picture. Points are x, y pairs in the picture's pixels: arrows, shapes, magnifiers and pictures use the
/// first two (from, to / corner, corner / centre, edge), pens and highlighters all of them, text and numbers the first.
/// </summary>
public sealed record Annotation
{
    public AnnotationKind Kind { get; init; }
    public double[] Points { get; init; } = [];
    public string Color { get; init; } = "#E53935";

    /// <summary>Line width in the picture's pixels (text and numbers: their size).</summary>
    public double Width { get; init; } = 8;

    public string Text { get; init; } = "";
    public int Number { get; init; } = 1;
    public string PicturePath { get; init; } = "";
}

public static class AnnotationRenderer
{
    public static void Draw(MagickImage image, IReadOnlyList<Annotation> annotations)
    {
        foreach (var a in annotations) Draw(image, a);
    }

    private static void Draw(MagickImage image, Annotation a)
    {
        var p = a.Points;
        var color = new MagickColor(a.Color);
        switch (a.Kind)
        {
            case AnnotationKind.Arrow when p.Length >= 4:
            {
                var (x1, y1, x2, y2) = (p[0], p[1], p[2], p[3]);
                var angle = Math.Atan2(y2 - y1, x2 - x1);
                var head = Math.Max(a.Width * 3.2, 14);
                // The shaft stops at the head's base so its square end doesn't show through the tip.
                var bx = x2 - Math.Cos(angle) * head * 0.8; var by = y2 - Math.Sin(angle) * head * 0.8;
                new Drawables().StrokeColor(color).StrokeWidth(a.Width).StrokeLineCap(LineCap.Round).FillColor(MagickColors.Transparent).Line(x1, y1, bx, by)
                    .StrokeWidth(1).FillColor(color).Polygon(
                        new PointD(x2, y2),
                        new PointD(x2 - Math.Cos(angle - 0.45) * head, y2 - Math.Sin(angle - 0.45) * head),
                        new PointD(x2 - Math.Cos(angle + 0.45) * head, y2 - Math.Sin(angle + 0.45) * head)).Draw(image);
                break;
            }
            case AnnotationKind.Pen or AnnotationKind.Highlight when p.Length >= 4:
            {
                var highlight = a.Kind == AnnotationKind.Highlight;
                var stroke = highlight ? new MagickColor(color.R, color.G, color.B, (byte)(255 * 0.38)) : color;
                var points = Enumerable.Range(0, p.Length / 2).Select(i => new PointD(p[2 * i], p[2 * i + 1])).ToList();
                // A straight stroke of two points: ImageMagick's polyline wants three.
                if (points.Count == 2) points.Insert(1, new PointD((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2));
                new Drawables().StrokeColor(stroke).StrokeWidth(highlight ? a.Width * 3 : a.Width).StrokeLineCap(highlight ? LineCap.Square : LineCap.Round)
                    .StrokeLineJoin(LineJoin.Round).FillColor(MagickColors.Transparent).Polyline(points).Draw(image);
                break;
            }
            case AnnotationKind.Rectangle when p.Length >= 4:
                new Drawables().StrokeColor(color).StrokeWidth(a.Width).FillColor(MagickColors.Transparent)
                    .RoundRectangle(Math.Min(p[0], p[2]), Math.Min(p[1], p[3]), Math.Max(p[0], p[2]), Math.Max(p[1], p[3]), a.Width, a.Width).Draw(image);
                break;
            case AnnotationKind.Ellipse when p.Length >= 4:
                new Drawables().StrokeColor(color).StrokeWidth(a.Width).FillColor(MagickColors.Transparent)
                    .Ellipse((p[0] + p[2]) / 2, (p[1] + p[3]) / 2, Math.Abs(p[2] - p[0]) / 2, Math.Abs(p[3] - p[1]) / 2, 0, 360).Draw(image);
                break;
            case AnnotationKind.Text when p.Length >= 2 && a.Text.Length > 0:
            {
                var size = a.Width;
                var drawables = new Drawables().FontPointSize(size).FillColor(color).StrokeColor(MagickColors.Transparent);
                if (Font() is { } font) drawables.Font(font);
                // Lines one under the other, the first one's top at the point.
                var lines = a.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++) drawables.Text(p[0], p[1] + size * (i + 0.85) * 1.15, lines[i]);
                drawables.Draw(image);
                break;
            }
            case AnnotationKind.Number when p.Length >= 2:
            {
                var r = a.Width;
                var drawables = new Drawables().FillColor(color).StrokeColor(MagickColors.White).StrokeWidth(Math.Max(2, r / 9)).Circle(p[0], p[1], p[0] + r, p[1])
                    .FillColor(MagickColors.White).StrokeColor(MagickColors.Transparent).FontPointSize(r * 1.1).TextAlignment(TextAlignment.Center);
                if (Font() is { } font) drawables.Font(font);
                drawables.Text(p[0], p[1] + r * 0.38, a.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).Draw(image);
                break;
            }
            case AnnotationKind.Magnifier when p.Length >= 4:
            {
                // A round lens showing the area under it twice as large.
                var (cx, cy) = (p[0], p[1]);
                var r = Math.Max(10, Math.Sqrt(Math.Pow(p[2] - cx, 2) + Math.Pow(p[3] - cy, 2)));
                var area = new MagickGeometry((int)(cx - r / 2), (int)(cy - r / 2), (uint)Math.Max(1, r), (uint)Math.Max(1, r));
                using var zoomed = (MagickImage)image.CloneArea(area);
                zoomed.Resize(new MagickGeometry((uint)(r * 2), (uint)(r * 2)) { IgnoreAspectRatio = true });
                using var mask = new MagickImage(MagickColors.Transparent, zoomed.Width, zoomed.Height);
                new Drawables().FillColor(MagickColors.White).Circle(r, r, r * 2 - 1, r).Draw(mask);
                zoomed.Alpha(AlphaOption.Set);
                zoomed.Composite(mask, CompositeOperator.DstIn);
                image.Composite(zoomed, (int)(cx - r), (int)(cy - r), CompositeOperator.Over);
                new Drawables().StrokeColor(color).StrokeWidth(Math.Max(3, a.Width / 2)).FillColor(MagickColors.Transparent).Circle(cx, cy, cx + r, cy).Draw(image);
                break;
            }
            case AnnotationKind.Picture when p.Length >= 4 && File.Exists(a.PicturePath):
            {
                using var picture = new MagickImage(a.PicturePath);
                picture.AutoOrient();
                var (x, y, w, h) = (Math.Min(p[0], p[2]), Math.Min(p[1], p[3]), Math.Abs(p[2] - p[0]), Math.Abs(p[3] - p[1]));
                picture.Resize(new MagickGeometry((uint)Math.Max(1, w), (uint)Math.Max(1, h)));
                image.Composite(picture, (int)(x + (w - picture.Width) / 2), (int)(y + (h - picture.Height) / 2), CompositeOperator.Over);
                break;
            }
        }
    }

    private static string? _font;

    /// <summary>A readable sans-serif font this computer has (Segoe UI on Windows), or null for ImageMagick's own.</summary>
    private static string? Font()
    {
        if (_font is not null) return _font.Length > 0 ? _font : null;
        var families = MagickNET.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _font = new[] { "Segoe UI", "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans", "Noto Sans" }.FirstOrDefault(families.Contains) ?? "";
        return _font.Length > 0 ? _font : null;
    }

    public static Task SaveAsync(string file, IReadOnlyList<Annotation> annotations, string output, CancellationToken ct) => Task.Run(() =>
    {
        using var image = new MagickImage(file);
        image.AutoOrient();
        image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        ct.ThrowIfCancellationRequested();
        Draw(image, annotations);
        if (image.Format == MagickFormat.Jpeg) image.Quality = Math.Max(image.Quality, 90);
        image.Write(output);
    }, ct);
}

/// <summary>"Edit photo" settings: every value 0 leaves the photo as it is. Light and colour −100…100, exposure in EV.</summary>
public sealed record PhotoSettings
{
    public double Exposure { get; init; }
    public double Brightness { get; init; }
    public double Contrast { get; init; }
    public double Highlights { get; init; }
    public double Shadows { get; init; }
    public double Whites { get; init; }
    public double Blacks { get; init; }
    public double Gamma { get; init; }
    public double Temperature { get; init; }
    public double Tint { get; init; }
    public double Saturation { get; init; }
    public double Vibrance { get; init; }
    public double Hue { get; init; }
    public double Clarity { get; init; }
    public double Dehaze { get; init; }
    public double Grain { get; init; }
    public double NoiseReduction { get; init; }

    public bool IsNeutral => this == new PhotoSettings();
}

public static class PhotoAdjust
{
    /// <summary>
    /// Applies <paramref name="s"/>. Light and white balance go through one curve per channel (fast, no banding);
    /// saturation, hue and vibrance through HSL; clarity and dehaze as local contrast; grain and noise last.
    /// </summary>
    public static void Apply(MagickImage image, PhotoSettings s)
    {
        if (s.IsNeutral) return;
        image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        if (s.NoiseReduction > 0) image.WaveletDenoise(new Percentage(s.NoiseReduction * 0.12), 0.5);
        if (s.Dehaze != 0)
        {
            // Haze lifts the darks and flattens colours: pull the black point down and add local contrast (or the reverse).
            if (s.Dehaze > 0) { image.Level(new Percentage(s.Dehaze * 0.12), new Percentage(100)); image.LocalContrast(60, new Percentage(s.Dehaze * 0.25)); }
            else image.Colorize(MagickColors.White, new Percentage(-s.Dehaze * 0.25));
        }
        using (var curve = Curve(s)) image.Clut(curve, PixelInterpolateMethod.Bilinear);
        if (s.Saturation != 0 || s.Hue != 0 || s.Vibrance != 0)
        {
            // Vibrance is gentler than saturation: about half as strong on the whole picture.
            var saturation = 100 + s.Saturation + s.Vibrance * 0.5;
            image.Modulate(new Percentage(100), new Percentage(Math.Max(0, saturation)), new Percentage(100 + s.Hue / 180 * 100));
        }
        if (s.Clarity > 0) image.LocalContrast(24, new Percentage(s.Clarity * 0.4));
        else if (s.Clarity < 0) image.GaussianBlur(0, -s.Clarity / 50.0);
        if (s.Grain > 0) image.AddNoise(NoiseType.Gaussian, s.Grain / 100 * 0.8, Channels.RGB);
    }

    /// <summary>A 256-step curve per channel holding exposure, brightness, contrast, tones, gamma and white balance.</summary>
    private static MagickImage Curve(PhotoSettings s)
    {
        var lut = new MagickImage(MagickColors.Black, 256, 1);
        using var pixels = lut.GetPixels();
        var exposure = Math.Pow(2, s.Exposure);
        var gamma = Math.Pow(2, -s.Gamma / 100);
        var (rGain, gGain, bGain) = (1 + s.Temperature * 0.0018, 1 - s.Tint * 0.0015, 1 - s.Temperature * 0.0018);
        for (var i = 0; i < 256; i++)
        {
            var x = i / 255.0 * exposure;
            x += s.Brightness / 100 * 0.25;
            x = (x - 0.5) * (1 + s.Contrast / 100) + 0.5;
            var c = Math.Clamp(x, 0, 1);
            x += s.Shadows / 100 * 0.35 * 4 * c * (1 - c) * (1 - c);
            x += s.Highlights / 100 * 0.35 * 4 * c * c * (1 - c);
            x += s.Whites / 100 * 0.25 * Math.Pow(c, 4);
            x += s.Blacks / 100 * 0.25 * Math.Pow(1 - c, 4);
            x = Math.Pow(Math.Clamp(x, 0, 1), gamma);
            byte Channel(double gain) => (byte)Math.Round(Math.Clamp(x * gain, 0, 1) * 255);
            pixels.SetPixel(i, 0, [Channel(rGain), Channel(gGain), Channel(bGain)]);
        }
        return lut;
    }

    public static Task SaveAsync(string file, PhotoSettings settings, string output, CancellationToken ct) => Task.Run(() =>
    {
        using var image = new MagickImage(file);
        image.AutoOrient();
        ct.ThrowIfCancellationRequested();
        Apply(image, settings);
        if (image.Format == MagickFormat.Jpeg) image.Quality = Math.Max(image.Quality, 92);
        image.Write(output);
    }, ct);
}
