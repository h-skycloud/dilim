// Arranges pictures into one: a grid, a row, a column, or a featured layout (the first picture large, the others
// beside it). The canvas has a ratio and a width; spacing, corner radius and background are the same for preview and
// result, which is drawn with the same code at its full width.

using ImageMagick;
using ImageMagick.Drawing;

namespace Dilim.Engines.Tools;

public enum CollageLayout { Grid, Row, Column, Featured }

public sealed record CollageOptions
{
    public CollageLayout Layout { get; init; } = CollageLayout.Grid;
    public int Columns { get; init; } = 2;

    /// <summary>Width / height of the canvas; 0 lets the layout decide from the pictures (a row is as wide as needed).</summary>
    public double Ratio { get; init; } = 1;
    public int Width { get; init; } = 2048;

    /// <summary>True: each picture fills its cell (edges cropped); false: the whole picture fits inside it.</summary>
    public bool Fill { get; init; } = true;

    /// <summary>Spacing and corner radius as a share of the width (so the preview looks like the result).</summary>
    public double Spacing { get; init; } = 0.01;
    public double Corners { get; init; }

    /// <summary>"#RRGGBB", or "transparent".</summary>
    public string Background { get; init; } = "#FFFFFF";
}

public static class CollageMaker
{
    /// <summary>The cells of <paramref name="count"/> pictures on a canvas of the given size, in reading order.</summary>
    public static IReadOnlyList<(double X, double Y, double W, double H)> Cells(CollageOptions o, int count, double width, double height)
    {
        var gap = o.Spacing * width;
        var cells = new List<(double, double, double, double)>();
        if (count == 0) return cells;
        void GridIn(double x, double y, double w, double h, int n, int columns)
        {
            columns = Math.Clamp(columns, 1, n);
            var rows = (n + columns - 1) / columns;
            var cw = (w - gap * (columns + 1)) / columns;
            var ch = (h - gap * (rows + 1)) / rows;
            for (var i = 0; i < n; i++)
            {
                var row = i / columns;
                // A last row with fewer pictures is centred.
                var inRow = row == rows - 1 ? n - row * columns : columns;
                var offset = (columns - inRow) * (cw + gap) / 2;
                cells.Add((x + gap + offset + i % columns * (cw + gap), y + gap + row * (ch + gap), cw, ch));
            }
        }
        switch (o.Layout)
        {
            case CollageLayout.Row: GridIn(0, 0, width, height, count, count); break;
            case CollageLayout.Column: GridIn(0, 0, width, height, count, 1); break;
            case CollageLayout.Featured when count > 1:
                // The first picture takes two thirds; the others share the rest in a column (or two when many).
                var landscape = width >= height;
                var main = landscape ? (width - gap) * 2 / 3 : (height - gap) * 2 / 3;
                cells.Add(landscape ? (gap, gap, main - gap, height - 2 * gap) : (gap, gap, width - 2 * gap, main - gap));
                if (landscape) GridIn(main, 0, width - main, height, count - 1, count > 5 ? 2 : 1);
                else GridIn(0, main, width, height - main, count - 1, count > 5 ? count / 2 : count - 1);
                break;
            default: GridIn(0, 0, width, height, count, o.Layout == CollageLayout.Featured ? 1 : o.Columns); break;
        }
        return cells;
    }

    /// <summary>The collage of <paramref name="pictures"/> (already oriented), <paramref name="width"/> pixels wide.</summary>
    public static MagickImage Render(IReadOnlyList<IMagickImage<byte>> pictures, CollageOptions o, int width, CancellationToken ct = default)
    {
        var height = Height(o, pictures, width);
        var transparent = o.Background.Equals("transparent", StringComparison.OrdinalIgnoreCase);
        var canvas = new MagickImage(transparent ? MagickColors.Transparent : new MagickColor(o.Background), (uint)width, (uint)height);
        var cells = Cells(o with { Spacing = o.Spacing }, pictures.Count, width, height);
        var radius = o.Corners * width;
        for (var i = 0; i < pictures.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (x, y, w, h) = cells[i];
            if (w < 1 || h < 1) continue;
            using var picture = (MagickImage)pictures[i].Clone();
            if (o.Fill)
            {
                picture.Resize(new MagickGeometry((uint)Math.Ceiling(w), (uint)Math.Ceiling(h)) { FillArea = true });
                picture.Crop(new MagickGeometry((uint)Math.Ceiling(w), (uint)Math.Ceiling(h)), Gravity.Center);
                picture.ResetPage();
            }
            else picture.Resize(new MagickGeometry((uint)w, (uint)h));
            if (radius >= 1) Round(picture, radius);
            var px = (int)Math.Round(x + (w - picture.Width) / 2);
            var py = (int)Math.Round(y + (h - picture.Height) / 2);
            canvas.Composite(picture, px, py, CompositeOperator.Over);
        }
        return canvas;
    }

    /// <summary>Canvas height: from the ratio, or for ratio 0 from the pictures' own shapes.</summary>
    public static int Height(CollageOptions o, IReadOnlyList<IMagickImage<byte>> pictures, int width)
    {
        if (o.Ratio > 0) return Math.Max(1, (int)Math.Round(width / o.Ratio));
        var aspect = pictures.Count == 0 ? 1 : pictures.Average(p => (double)p.Width / p.Height);
        return o.Layout switch
        {
            CollageLayout.Row => Math.Max(1, (int)(width / (aspect * pictures.Count))),
            CollageLayout.Column => Math.Max(1, (int)(width / aspect * pictures.Count)),
            _ => Math.Max(1, (int)(width / aspect / Math.Max(1, o.Columns) * Math.Ceiling(pictures.Count / (double)Math.Max(1, o.Columns)))),
        };
    }

    private static void Round(MagickImage picture, double radius)
    {
        using var mask = new MagickImage(MagickColors.Transparent, picture.Width, picture.Height);
        new Drawables().FillColor(MagickColors.White).RoundRectangle(0, 0, picture.Width - 1, picture.Height - 1, radius, radius).Draw(mask);
        picture.Alpha(AlphaOption.Set);
        picture.Composite(mask, CompositeOperator.DstIn);
    }

    /// <summary>Reads and orients the pictures, at most <paramref name="maxSide"/> pixels (0: full size).</summary>
    public static List<IMagickImage<byte>> Load(IReadOnlyList<string> files, int maxSide)
    {
        var list = new List<IMagickImage<byte>>();
        foreach (var file in files)
        {
            var image = new MagickImage(file);
            image.AutoOrient();
            if (maxSide > 0 && Math.Max(image.Width, image.Height) > maxSide) image.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
            list.Add(image);
        }
        return list;
    }

    /// <summary>Writes the collage of <paramref name="files"/> to <paramref name="output"/> (PNG keeps transparency).</summary>
    public static Task SaveAsync(IReadOnlyList<string> files, CollageOptions o, string output, CancellationToken ct) => Task.Run(() =>
    {
        var pictures = Load(files, 0);
        try
        {
            using var collage = Render(pictures, o, o.Width, ct);
            if (output.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) { collage.Quality = 92; collage.Alpha(AlphaOption.Remove); }
            collage.Write(output);
        }
        finally { foreach (var p in pictures) p.Dispose(); }
    }, ct);
}
