// Writes a PDF from pages picked, ordered, turned and repeated by the user, from one PDF or several.
//
// Pages are copied as they are (text, links and vector drawings kept); a turn is added to the page's own rotation.
// "Same width" scales every page to the width of the narrowest one (as shown, turns included): those pages are drawn
// into new pages of the new size, which keeps their look but not their links or form fields.

using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Dilim.Engines.Pdf;

/// <summary>One page of the result: page <paramref name="Page"/> (0-based) of <paramref name="File"/>, turned clockwise.</summary>
public sealed record PdfPageChoice(string File, int Page, int Rotation = 0);

public static class PdfOrganizer
{
    public static Task SaveAsync(IReadOnlyList<PdfPageChoice> pages, bool sameWidth, string output, CancellationToken ct) =>
        Task.Run(() => Save(pages, sameWidth, output, ct), ct);

    public static void Save(IReadOnlyList<PdfPageChoice> pages, bool sameWidth, string output, CancellationToken ct)
    {
        if (pages.Count == 0) throw new ArgumentException("tool.error.pages");
        if (pages.Any(p => p.Rotation % 90 != 0)) throw new ArgumentException("tool.error.rotation");
        var sources = new Dictionary<string, PdfDocument>(StringComparer.Ordinal);
        try
        {
            PdfDocument Source(string file)
            {
                if (!sources.TryGetValue(file, out var document))
                    sources[file] = document = PdfSharpConverter.Open(file, PdfDocumentOpenMode.Import);
                return document;
            }

            using var result = new PdfDocument();
            if (!sameWidth)
            {
                foreach (var choice in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    var source = Source(choice.File);
                    if (choice.Page < 0 || choice.Page >= source.PageCount) throw new ArgumentException("tool.error.pages");
                    var page = result.AddPage(source.Pages[choice.Page]);
                    page.Rotate = Normalize(source.Pages[choice.Page].Rotate + choice.Rotation);
                }
            }
            else
            {
                // Sizes as shown: a page turned a quarter shows its height as width.
                var shown = pages.Select(choice =>
                {
                    var page = Source(choice.File).Pages[choice.Page];
                    var turn = Normalize(page.Rotate + choice.Rotation);
                    var (w, h) = (page.Width.Point, page.Height.Point);
                    return (choice, turn, w, h, shownWidth: turn is 90 or 270 ? h : w, shownHeight: turn is 90 or 270 ? w : h);
                }).ToList();
                var width = shown.Min(s => s.shownWidth);
                var forms = new Dictionary<string, XPdfForm>(StringComparer.Ordinal);
                try
                {
                    foreach (var (choice, turn, w, h, shownWidth, shownHeight) in shown)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!forms.TryGetValue(choice.File, out var form))
                            forms[choice.File] = form = XPdfForm.FromFile(choice.File);
                        form.PageIndex = choice.Page;
                        var scale = width / shownWidth;
                        var page = result.AddPage();
                        page.Width = XUnit.FromPoint(width);
                        page.Height = XUnit.FromPoint(shownHeight * scale);
                        using var gfx = XGraphics.FromPdfPage(page);
                        // Turn clockwise around the new page: the source's top-left corner goes to the matching corner.
                        switch (turn)
                        {
                            case 90: gfx.TranslateTransform(page.Width.Point, 0); break;
                            case 180: gfx.TranslateTransform(page.Width.Point, page.Height.Point); break;
                            case 270: gfx.TranslateTransform(0, page.Height.Point); break;
                        }
                        if (turn != 0) gfx.RotateTransform(turn);
                        gfx.DrawImage(form, 0, 0, w * scale, h * scale);
                    }
                }
                finally
                {
                    foreach (var form in forms.Values) form.Dispose();
                }
            }
            result.Options.CompressContentStreams = true;
            result.Save(output);
        }
        finally
        {
            foreach (var document in sources.Values) document.Dispose();
        }
    }

    private static int Normalize(int degrees) => ((degrees % 360) + 360) % 360;
}
