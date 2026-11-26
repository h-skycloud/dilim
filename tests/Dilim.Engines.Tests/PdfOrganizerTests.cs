using Dilim.Engines.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Dilim.Engines.Tests;

public sealed class PdfOrganizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dilim-organize", Guid.NewGuid().ToString("N")[..8]);

    public PdfOrganizerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Pages of the given sizes (mm); each page's width in points tells it apart.</summary>
    private string Pdf(string name, params (double W, double H)[] sizes)
    {
        var path = Path.Combine(_root, name);
        using var document = new PdfDocument();
        foreach (var (w, h) in sizes)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(w); page.Height = XUnit.FromMillimeter(h);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.DarkOrange, 10, 10, 50, 50);
        }
        document.Save(path);
        return path;
    }

    private static PdfDocument Read(string path) => PdfReader.Open(path, PdfDocumentOpenMode.Import);

    [Fact]
    public void Pages_from_several_pdfs_are_ordered_turned_and_repeated()
    {
        var a = Pdf("a.pdf", (210, 297), (148, 210));
        var b = Pdf("b.pdf", (420, 297));
        var output = Path.Combine(_root, "out.pdf");
        PdfOrganizer.Save([new(b, 0), new(a, 1, 90), new(a, 0), new(a, 0, 180)], sameWidth: false, output, CancellationToken.None);
        using var result = Read(output);
        Assert.Equal(4, result.PageCount);
        Assert.Equal(XUnit.FromMillimeter(420).Point, result.Pages[0].Width.Point, 0.5);
        Assert.Equal(XUnit.FromMillimeter(148).Point, result.Pages[1].Width.Point, 0.5);
        Assert.Equal(90, result.Pages[1].Rotate);
        Assert.Equal(0, result.Pages[2].Rotate);
        Assert.Equal(180, result.Pages[3].Rotate);
    }

    [Fact]
    public void Same_width_scales_every_page_to_the_narrowest_as_shown()
    {
        var a = Pdf("a.pdf", (210, 297), (420, 297));
        var output = Path.Combine(_root, "same.pdf");
        // The A3 sheet turned a quarter shows 297 mm wide; the A4 page 210 mm: everything becomes 210 mm wide.
        PdfOrganizer.Save([new(a, 0), new(a, 1, 90)], sameWidth: true, output, CancellationToken.None);
        using var result = Read(output);
        var width = XUnit.FromMillimeter(210).Point;
        Assert.All(result.Pages.Cast<PdfPage>(), p => Assert.Equal(width, p.Width.Point, 0.5));
        // The turned A3 sheet is 420 mm tall as shown, scaled by 210/297.
        Assert.Equal(XUnit.FromMillimeter(420 * 210.0 / 297).Point, result.Pages[1].Height.Point, 0.5);
        Assert.Equal(0, result.Pages[1].Rotate); // the turn is drawn into the page
    }

    /// <summary>Turned clockwise: the orange square from the top-left corner ends up top-right, as on screen.</summary>
    [Fact]
    public async Task A_quarter_turn_drawn_into_a_page_turns_clockwise()
    {
        var a = Pdf("a.pdf", (210, 297));
        var output = Path.Combine(_root, "turn.pdf");
        PdfOrganizer.Save([new(a, 0, 90)], sameWidth: true, output, CancellationToken.None);
        var png = await Dilim.Engines.Tools.ToolPreviews.PdfPageAsync(output, 0, 300, TestContext.Current.CancellationToken);
        Assert.NotNull(png);
        using var image = new ImageMagick.MagickImage(png);
        using var pixels = image.GetPixels();
        bool Orange(int x, int y) { var c = pixels.GetPixel(x, y).ToColor()!; return c.R > 180 && c.G is > 80 and < 180 && c.B < 80; }
        Assert.True(Orange((int)image.Width - 10, 10), "top-right should be orange");
        Assert.False(Orange(10, 10), "top-left should be white");
    }

    [Fact]
    public void Nothing_to_save_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PdfOrganizer.Save([], false, Path.Combine(_root, "x.pdf"), CancellationToken.None));
    }
}
