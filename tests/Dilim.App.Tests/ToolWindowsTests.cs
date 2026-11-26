using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dilim.App.Services;
using Dilim.App.Views.Tools;
using Dilim.Core.Localization;
using Dilim.Core.Settings;
using Dilim.Engines.Infrastructure;
using Dilim.Engines.Tools;

namespace Dilim.App.Tests;

/// <summary>The redesigned tool windows: they render in both themes and run their tool with what the user arranged.</summary>
public class ToolWindowsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "dilim-tool-windows", Guid.NewGuid().ToString("N")[..8]);

    private static string Pdf(string name, int pages)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        using var document = new PdfSharp.Pdf.PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(PdfSharp.Drawing.XBrushes.DarkOrange, 60, 80, 300, 200);
        }
        document.Save(path);
        return path;
    }

    private static string Video(string name, string color, string size)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        var ffmpeg = ToolEngine.FfmpegPath ?? throw new InvalidOperationException("FFmpeg is needed for this test");
        // Off the UI thread: blocking it on an await would never let the continuation run.
        var made = Task.Run(() => ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={color}:s={size}:r=10:d=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", path], TimeSpan.FromSeconds(30), CancellationToken.None)).GetAwaiter().GetResult();
        Assert.Equal(0, made.ExitCode);
        return path;
    }

    /// <summary>Lets the background thumbnails arrive and draws.</summary>
    private static void Settle(Func<bool>? until = null)
    {
        for (var i = 0; i < 200 && until?.Invoke() != true; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
        RenderTests.Pump();
    }

    private static int Pictures(Window window) => window.GetVisualDescendants().OfType<Image>().Count();

    [AvaloniaTheory]
    [InlineData("tr", false)]
    [InlineData("tr", true)]
    [InlineData("en", false)]
    public void Merge_pdfs_renders(string language, bool dark)
    {
        TestServices.EnsureInitialized(language);
        var theme = AppHost.Get<ThemeService>();
        try
        {
            if (dark) theme.Apply(new ThemeSettings { Mode = ThemeMode.Dark, CornerRadius = 16 }, language);
            var files = new[] { Pdf("Vaziyet planı.pdf", 2), Pdf("Kesitler.pdf", 1), Pdf("Detaylar.pdf", 3) };
            var window = new MergeTool("mergePDF", files, AppHost.Get<ILocalizer>(), (_, _, _, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));
            window.Show();
            Settle(() => Pictures(window) >= 3);
            RenderTests.Save(window, $"tool-merge-pdf-{language}{(dark ? "-dark" : "")}.png");
            Assert.Equal(3, Pictures(window));
            window.Close();
        }
        finally
        {
            if (dark) theme.Apply(new ThemeSettings(), language);
        }
    }

    [AvaloniaFact]
    public void Join_videos_renders_with_frames_and_runs_in_the_chosen_order()
    {
        TestServices.EnsureInitialized("tr");
        var files = new[] { Video("Çiçekler.mp4", "orange", "320x180"), Video("Ağaç.mp4", "teal", "180x320") };
        IReadOnlyList<string>? ran = null;
        var window = new MergeTool("joinVideos", files, AppHost.Get<ILocalizer>(), (id, paths, _, _, _, _) =>
        {
            Assert.Equal("joinVideos", id);
            ran = paths.ToArray();
            return Task.FromResult<IReadOnlyList<string>>([Path.Combine(Root, "joined.mp4")]);
        });
        window.Show();
        Settle(() => Pictures(window) >= 2);
        RenderTests.Save(window, "tool-join-videos-tr.png");
        Assert.Equal(2, Pictures(window));

        // Second clip first, then the main button.
        window.GetVisualDescendants().OfType<OrderList>().Single().Move(1, 0);
        var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == AppHost.Get<ILocalizer>()["tool.joinVideos.run"]);
        primary.Command?.Execute(null);
        primary.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(() => ran is not null);
        Assert.Equal([files[1], files[0]], ran);
        Settle();
        RenderTests.Save(window, "tool-join-videos-done-tr.png");
        window.Close();
    }

    [AvaloniaFact]
    public void One_file_is_not_enough_to_merge()
    {
        TestServices.EnsureInitialized("en");
        var window = new MergeTool("mergePDF", [Pdf("only.pdf", 1)], AppHost.Get<ILocalizer>(), (_, _, _, _, _, _) => Task.FromResult<IReadOnlyList<string>>([]));
        window.Show();
        Settle();
        var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == AppHost.Get<ILocalizer>()["tool.mergePDF.run"]);
        Assert.False(primary.IsEnabled);
        window.Close();
    }

    /// <summary>A two-page A3 sheet with a large detailed photo.</summary>
    private static string PhotoPdf(string name)
    {
        Directory.CreateDirectory(Root);
        var photo = Path.Combine(Root, name + ".jpg");
        using (var image = new ImageMagick.MagickImage("plasma:orange-teal", 3000, 2000))
        {
            image.AddNoise(ImageMagick.NoiseType.Gaussian, 0.15);
            image.Quality = 95;
            image.Write(photo);
        }
        var path = Path.Combine(Root, name);
        using var document = new PdfSharp.Pdf.PdfDocument();
        for (var p = 0; p < 2; p++)
        {
            var page = document.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromMillimeter(420); page.Height = PdfSharp.Drawing.XUnit.FromMillimeter(297);
            using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            using var picture = PdfSharp.Drawing.XImage.FromFile(photo);
            gfx.DrawImage(picture, 40, 40, page.Width.Point - 80, page.Height.Point - 140);
            gfx.DrawRectangle(new PdfSharp.Drawing.XPen(PdfSharp.Drawing.XColors.Black, 0.5), 40, page.Height.Point - 80, 300, 30);
        }
        document.Save(path);
        return path;
    }

    private static string Text(Window window, Func<TextBlock, bool> which) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(which).Text ?? "";

    [AvaloniaTheory]
    [InlineData("tr", false)]
    [InlineData("tr", true)]
    [InlineData("en", false)]
    public void Pdf_shrink_measures_compares_and_aims_at_a_target(string language, bool dark)
    {
        TestServices.EnsureInitialized(language);
        var theme = AppHost.Get<ThemeService>();
        var loc = AppHost.Get<ILocalizer>();
        try
        {
            if (dark) theme.Apply(new ThemeSettings { Mode = ThemeMode.Dark, CornerRadius = 16 }, language);
            var file = PhotoPdf($"Cephe paftası {language}{dark}.pdf");
            var window = new PdfShrinkTool([file], loc);
            window.Show();
            var calculating = loc["tool.pdfshrink.calculating"];
            bool Measured() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == loc["tool.pdfshrink.q3"] || t.Text == loc["tool.pdfshrink.q4"] || t.Text == loc["tool.pdfshrink.q2"]);
            Settle(Measured);
            for (var i = 0; i < 400 && !Measured(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
            Assert.True(Measured(), "no estimate");
            RenderTests.Save(window, $"tool-pdf-shrink-{language}{(dark ? "-dark" : "")}.png");

            if (language == "tr" && !dark)
            {
                // Compare quality: before and after pictures.
                var compare = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == loc["tool.pdfshrink.compare"]);
                compare.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 400 && Pictures(window) < 2; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
                Assert.Equal(2, Pictures(window));

                // A target: 0.5 MB.
                window.GetVisualDescendants().OfType<TextBox>().Single().Text = "0,5";
                bool Picked() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Seçilen ayar", StringComparison.Ordinal) == true);
                for (var i = 0; i < 600 && !Picked(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
                Assert.True(Picked(), "no setting picked for the target");
                window.Height = 1180;
                RenderTests.Pump();
                RenderTests.Save(window, "tool-pdf-shrink-target-tr.png");
            }
            window.Close();
        }
        finally
        {
            if (dark) theme.Apply(new ThemeSettings(), language);
        }
    }

    [AvaloniaTheory]
    [InlineData("tr", false)]
    [InlineData("tr", true)]
    [InlineData("en", false)]
    public void Organize_pdf_turns_repeats_removes_and_saves_the_pages(string language, bool dark)
    {
        TestServices.EnsureInitialized(language);
        var theme = AppHost.Get<ThemeService>();
        try
        {
            if (dark) theme.Apply(new ThemeSettings { Mode = ThemeMode.Dark, CornerRadius = 16 }, language);
            var file = Pdf($"Proje {language}{dark}.pdf", 4);
            var window = new PdfOrganizeTool([file], AppHost.Get<ILocalizer>());
            window.Show();
            int Cards() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("page-card"));
            Settle(() => Cards() == 4 && Pictures(window) >= 4);
            for (var i = 0; i < 200 && Pictures(window) < 4; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); RenderTests.Pump(); }
            Assert.Equal(4, Cards());

            Button Action(int card, string key) => window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("page-card")).ElementAt(card)
                .GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == AppHost.Get<ILocalizer>()[key]);
            void Click(Button b) { b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(); }
            Click(Action(1, "tool.organize.rotate"));
            Click(Action(2, "tool.organize.duplicate"));
            Click(Action(4, "tool.organize.remove"));
            Settle();
            Assert.Equal(4, Cards());
            RenderTests.Save(window, $"tool-organize-pdf-{language}{(dark ? "-dark" : "")}.png");

            if (language == "tr" && !dark)
            {
                var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == AppHost.Get<ILocalizer>()["tool.organize.run"]);
                primary.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var output = Path.Combine(Root, "Dilim", Path.GetFileName(file));
                for (var i = 0; i < 200 && !File.Exists(output); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
                Settle();
                using var saved = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                // Pages 1, 2 (turned), 3, 3 — page 4 removed.
                Assert.Equal(4, saved.PageCount);
                Assert.Equal(90, saved.Pages[1].Rotate);
            }
            window.Close();
        }
        finally
        {
            if (dark) theme.Apply(new ThemeSettings(), language);
        }
    }
}
