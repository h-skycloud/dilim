using Dilim.Engines.Pdf;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Dilim.Engines.Tests;

public sealed class PdfShrinkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dilim-shrink", Guid.NewGuid().ToString("N")[..8]);

    public PdfShrinkerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A sheet with a large detailed photo (as from a camera, JPEG 95) and a caption.</summary>
    private string PhotoPdf()
    {
        var photo = Path.Combine(_root, "photo.jpg");
        using (var image = new MagickImage("plasma:orange-teal", 3000, 2000))
        {
            image.AddNoise(NoiseType.Gaussian, 0.15); // a little sensor noise, as in a real photo
            image.Quality = 95;
            image.Write(photo);
        }
        var path = Path.Combine(_root, "pafta.pdf");
        using var document = new PdfDocument();
        for (var p = 0; p < 2; p++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420); page.Height = XUnit.FromMillimeter(297);
            using var gfx = XGraphics.FromPdfPage(page);
            using var picture = XImage.FromFile(photo);
            gfx.DrawImage(picture, 40, 40, page.Width.Point - 80, page.Height.Point - 140);
            gfx.DrawRectangle(new XPen(XColors.Black, 0.5), 40, page.Height.Point - 80, 300, 30); // a title block
        }
        document.Save(path);
        return path;
    }

    /// <summary>A plan of many thin lines: vector-heavy, little to gain for Smart.</summary>
    private string VectorPdf()
    {
        var path = Path.Combine(_root, "plan.pdf");
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(594); page.Height = XUnit.FromMillimeter(420);
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var random = new Random(7);
            var pen = new XPen(XColors.Black, 0.25);
            for (var i = 0; i < 400000; i++)
            {
                var x = random.NextDouble() * page.Width.Point; var y = random.NextDouble() * page.Height.Point;
                gfx.DrawLine(pen, x, y, x + random.NextDouble() * 30 - 15, y + random.NextDouble() * 30 - 15);
            }
        }
        document.Options.CompressContentStreams = false;
        document.Save(path);
        return path;
    }

    [Fact]
    public async Task Smart_levels_get_smaller_and_lose_quality_in_order()
    {
        var file = PhotoPdf();
        var ct = TestContext.Current.CancellationToken;
        var original = new FileInfo(file).Length;
        var results = new List<PdfShrinkResult>();
        foreach (var level in PdfShrinker.Levels) results.Add(await PdfShrinker.MeasureAsync(file, PdfShrinkMethod.Smart, level, ct));
        Assert.All(results, r => Assert.True(r.Size < original, $"{r.Setting} gave {r.Size} of {original}"));
        for (var i = 1; i < results.Count; i++)
            Assert.True(results[i].Size <= results[i - 1].Size, $"level {i} {results[i].Size} > level {i - 1} {results[i - 1].Size}");
        Assert.True(results[0].Similarity >= results[^1].Similarity);
        Assert.True(results[2].Size < original * 0.5, $"balanced only reached {results[2].Size} of {original}");
        Assert.True(results[0].Similarity > 0.9, $"light is only {results[0].Similarity}");
    }

    [Fact]
    public async Task Flatten_shrinks_a_vector_heavy_plan_that_smart_cannot()
    {
        var file = VectorPdf();
        var ct = TestContext.Current.CancellationToken;
        var smart = await PdfShrinker.MeasureAsync(file, PdfShrinkMethod.Smart, PdfShrinker.Levels[3], ct);
        var flat = await PdfShrinker.MeasureAsync(file, PdfShrinkMethod.Flatten, PdfShrinker.Levels[3], ct);
        Assert.True(flat.Size < smart.Size, $"flatten {flat.Size} vs smart {smart.Size}");
        var bytes = await PdfShrinker.ShrinkAsync(file, PdfShrinkMethod.Flatten, PdfShrinker.Levels[2], ct);
        using var stream = new MemoryStream(bytes);
        using var flattened = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        // Same page size as the original sheet.
        Assert.Equal(XUnit.FromMillimeter(594).Point, flattened.Pages[0].Width.Point, 0.5);
    }

    [Fact]
    public async Task A_target_size_gets_the_best_quality_under_it()
    {
        var file = PhotoPdf();
        var ct = TestContext.Current.CancellationToken;
        var balanced = await PdfShrinker.MeasureAsync(file, PdfShrinkMethod.Smart, PdfShrinker.Levels[2], ct);
        var screen = await PdfShrinker.MeasureAsync(file, PdfShrinkMethod.Smart, PdfShrinker.Levels[3], ct);
        var target = (balanced.Size + screen.Size) / 2;
        var fit = await PdfShrinker.FitAsync(file, PdfShrinkMethod.Smart, target, ct);
        Assert.NotNull(fit);
        Assert.True(fit.Size <= target);
        // Between the levels: better than plain "Screen".
        Assert.True(fit.Size >= screen.Size, $"fit {fit.Size} screen {screen.Size} target {target}");
        // An impossible target: nothing fits.
        Assert.Null(await PdfShrinker.FitAsync(file, PdfShrinkMethod.Smart, 1000, ct));
    }

    [Fact]
    public async Task Compare_gives_the_same_crop_before_and_after()
    {
        var file = PhotoPdf();
        var ct = TestContext.Current.CancellationToken;
        var shrunk = await PdfShrinker.ShrinkAsync(file, PdfShrinkMethod.Smart, PdfShrinker.Levels[4], ct);
        var (before, after) = await PdfShrinker.CompareAsync(file, shrunk, 0, 300, ct);
        using var a = new MagickImage(before);
        using var b = new MagickImage(after);
        Assert.Equal((300u, 300u), (a.Width, a.Height));
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
    }
}
