using Avalonia.Controls;
using Avalonia.Headless;
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

    /// <summary>A photo-like picture (as from a phone camera), w × h.</summary>
    private static string Photo(string name, int w = 1600, int h = 1066)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        using var image = new ImageMagick.MagickImage("plasma:skyblue-seagreen", (uint)w, (uint)h);
        new ImageMagick.Drawing.Drawables().FillColor(new ImageMagick.MagickColor("#E8873A")).Circle(w * 0.62, h * 0.45, w * 0.62, h * 0.45 + h * 0.18)
            .FillColor(new ImageMagick.MagickColor("#2F4F2F")).Rectangle(0, h * 0.8, w, h).Draw(image);
        image.Quality = 90;
        image.Write(path);
        return path;
    }

    [AvaloniaTheory]
    [InlineData("tr", false)]
    [InlineData("tr", true)]
    [InlineData("en", false)]
    public void Crop_image_keeps_a_ratio_and_saves_the_box(string language, bool dark)
    {
        TestServices.EnsureInitialized(language);
        var theme = AppHost.Get<ThemeService>();
        try
        {
            if (dark) theme.Apply(new ThemeSettings { Mode = ThemeMode.Dark, CornerRadius = 16 }, language);
            var file = Photo($"Deniz feneri {language}{dark}.jpg");
            var preset = AppHost.Get<UserDataStore>().FindPreset("tool-crop")!.Clone();
            var window = new CropImageTool([file], preset, AppHost.Get<ILocalizer>());
            window.Show();
            var canvas = window.GetVisualDescendants().OfType<BoxCanvas>().Single();
            for (var i = 0; i < 200 && canvas.Boxes.Count == 0; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
            Assert.Equal(new Avalonia.PixelSize(1600, 1066), canvas.SourceSize);
            // 1:1 — the largest centred square.
            window.GetVisualDescendants().OfType<Segmented>().Single().Selected = 1;
            Settle();
            Assert.Equal(new Avalonia.Rect(267, 0, 1066, 1066), canvas.Boxes[0].Area);
            RenderTests.Save(window, $"tool-crop-image-{language}{(dark ? "-dark" : "")}.png");

            if (language == "tr" && !dark)
            {
                var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == AppHost.Get<ILocalizer>()["tool.crop.apply"]);
                primary.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                string? saved = null;
                for (var i = 0; i < 300 && saved is null; i++)
                {
                    Dispatcher.UIThread.RunJobs(); Thread.Sleep(20);
                    saved = Directory.EnumerateFiles(Root, "Deniz feneri trFalse*", SearchOption.AllDirectories).FirstOrDefault(f => f != file);
                }
                Assert.NotNull(saved);
                for (var i = 0; i < 50; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
                using var result = new ImageMagick.MagickImage(saved);
                Assert.Equal((1066u, 1066u), (result.Width, result.Height));
            }
            window.Close();
        }
        finally
        {
            if (dark) theme.Apply(new ThemeSettings(), language);
        }
    }

    private static string TestClip(string name, double seconds = 4)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        var ffmpeg = ToolEngine.FfmpegPath ?? throw new InvalidOperationException("FFmpeg is needed for this test");
        var made = Task.Run(() => ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=s=640x360:r=25:d={seconds}",
            "-f", "lavfi", "-i", $"sine=frequency=330:duration={seconds}", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", path],
            TimeSpan.FromSeconds(60), CancellationToken.None)).GetAwaiter().GetResult();
        Assert.Equal(0, made.ExitCode);
        return path;
    }

    private static void Wait(Func<bool> until, int tries = 400)
    {
        for (var i = 0; i < tries && !until(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); }
        RenderTests.Pump();
    }

    private static void Press(Window window, string text) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text || (b.Content as StackPanel)?.Children.OfType<TextBlock>().Any(t => t.Text == text) == true)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static Button Primary(Window window, string text) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text && b.Classes.Contains("accent"));

    [AvaloniaFact]
    public void Video_tools_render_and_ask_the_engine_for_what_was_chosen()
    {
        TestServices.EnsureInitialized("tr");
        var loc = AppHost.Get<ILocalizer>();
        var clip = TestClip("Fener.mp4");
        ToolRequest? asked = null;
        string? askedId = null;
        Task<IReadOnlyList<string>> Run(string id, IReadOnlyList<string> files, ToolRequest request, Window owner, IProgress<double> progress, CancellationToken ct)
        {
            (askedId, asked) = (id, request);
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        // Speed: 2× from its stops.
        var speed = new VideoSpeedTool([clip], loc, Run);
        speed.Show();
        var preview = speed.GetVisualDescendants().OfType<VideoPreview>().Single();
        Wait(() => preview.Info is not null);
        Wait(() => false, 40);
        speed.GetVisualDescendants().OfType<Slider>().First(s => s.Classes.Contains("level")).Value = 4;
        RenderTests.Save(speed, "tool-video-speed-tr.png");
        Primary(speed, loc["tool.crop.apply"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => asked is not null);
        Assert.Equal(("changeVideoSpeed", 2.0), (askedId, asked!.Speed));
        speed.Close();

        // Frames: two added.
        asked = null;
        var frames = new VideoFramesTool([clip], loc, Run);
        frames.Show();
        preview = frames.GetVisualDescendants().OfType<VideoPreview>().Single();
        Wait(() => preview.Info is not null);
        Press(frames, loc["tool.frames.add"]); Wait(() => false, 20);
        preview.Seek(1.6); Wait(() => false, 20);
        Press(frames, loc["tool.frames.add"]); Wait(() => false, 40);
        RenderTests.Save(frames, "tool-video-frames-tr.png");
        Primary(frames, loc.Format("tool.frames.save", 2)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => asked is not null);
        Assert.Equal(2, asked!.Times.Length);
        frames.Close();

        // Crop: 1:1 centred.
        asked = null;
        var crop = new CropVideoTool([clip], loc, Run);
        crop.Show();
        preview = crop.GetVisualDescendants().OfType<VideoPreview>().Single();
        Wait(() => preview.Canvas.Boxes.Count > 0);
        crop.GetVisualDescendants().OfType<Segmented>().Single().Selected = 1;
        Wait(() => false, 40);
        RenderTests.Save(crop, "tool-video-crop-tr.png");
        Primary(crop, loc["tool.crop.apply"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => asked is not null);
        Assert.Equal((140, 0, 360, 360), (asked!.X, asked.Y, asked.Width, asked.Height));
        crop.Close();

        // Trim: typed times.
        asked = null;
        var trim = new TrimVideoTool([clip], loc, Run);
        trim.Show();
        preview = trim.GetVisualDescendants().OfType<VideoPreview>().Single();
        Wait(() => preview.Info is not null);
        var boxes = trim.GetVisualDescendants().OfType<TextBox>().ToList();
        void Type(TextBox box, string text)
        {
            box.Focus(); box.Text = text;
            trim.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Wait(() => false, 5);
        }
        Type(boxes[0], "0:00.50");
        Type(boxes[1], "3");
        Wait(() => trim.GetVisualDescendants().OfType<Filmstrip>().Single().GetType() is not null, 60);
        RenderTests.Save(trim, "tool-video-trim-tr.png");
        Primary(trim, loc["tool.crop.apply"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => asked is not null);
        Assert.Equal((0.5, 3.0), (asked!.Start, asked.End));
        trim.Close();

        // Split: three equal parts.
        asked = null;
        var split = new SplitVideoTool([clip], loc, Run);
        split.Show();
        preview = split.GetVisualDescendants().OfType<VideoPreview>().Single();
        Wait(() => preview.Info is not null);
        split.GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 3;
        Press(split, loc["tool.split.set"]);
        Wait(() => false, 60);
        RenderTests.Save(split, "tool-video-split-tr.png");
        Primary(split, loc["tool.split.run"]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Wait(() => asked is not null);
        Assert.Equal(2, asked!.Times.Length);
        split.Close();
    }

    private static string Voice(string name)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        var ffmpeg = ToolEngine.FfmpegPath ?? throw new InvalidOperationException("FFmpeg is needed for this test");
        var made = Task.Run(() => ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i",
            "aevalsrc='0.6*sin(2*PI*300*t)*(between(t,0.5,1.1)+between(t,1.5,2.1))|0.3*sin(2*PI*300*t)*(between(t,0.5,1.1)+between(t,1.5,2.1))':s=44100:d=2.5",
            "-c:a", "pcm_s16le", path], TimeSpan.FromSeconds(30), CancellationToken.None)).GetAwaiter().GetResult();
        Assert.Equal(0, made.ExitCode);
        return path;
    }

    [AvaloniaFact]
    public void Audio_tools_render_and_ask_the_engine_for_what_was_chosen()
    {
        TestServices.EnsureInitialized("tr");
        var loc = AppHost.Get<ILocalizer>();
        var voice = Voice("Duraklamalı ses.wav");
        ToolRequest? asked = null;
        Task<IReadOnlyList<string>> Run(string id, IReadOnlyList<string> files, ToolRequest request, Window owner, IProgress<double> progress, CancellationToken ct)
        {
            asked = request;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
        void Go(Window w, string text) { asked = null; Primary(w, text).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Wait(() => asked is not null); }
        bool Drawn(Window w) => w.GetVisualDescendants().OfType<WaveformView>().All(v => v.Peaks.Length > 0);

        var trim = new TrimAudioTool([voice], loc, Run);
        trim.Show();
        Wait(() => Drawn(trim));
        trim.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Wait(() => false, 20);
        RenderTests.Save(trim, "tool-audio-trim-tr.png");
        Go(trim, loc["tool.crop.apply"]);
        Assert.True(asked!.TrimSilence);
        Assert.Equal(2.5, asked.End, 2);
        trim.Close();

        var channels = new AudioChannelsTool([voice], loc, Run);
        channels.Show();
        Wait(() => Drawn(channels));
        channels.GetVisualDescendants().OfType<Segmented>().Single().Selected = 0;
        channels.GetVisualDescendants().OfType<Slider>().First().Value = 50;
        Wait(() => false, 20);
        RenderTests.Save(channels, "tool-audio-channels-tr.png");
        Go(channels, loc["tool.crop.apply"]);
        Assert.Equal((1, 0.5, 1.0), (asked!.Channels, asked.LeftVolume, asked.RightVolume));
        channels.Close();

        var normalize = new NormalizeTool([voice], loc, Run);
        normalize.Show();
        Wait(() => Drawn(normalize) && normalize.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Giriş:", StringComparison.Ordinal) == true));
        normalize.GetVisualDescendants().OfType<Slider>().First().Value = -20;
        Wait(() => false, 20);
        RenderTests.Save(normalize, "tool-audio-normalize-tr.png");
        Go(normalize, loc["tool.normalize.run"]);
        Assert.Equal(-20, asked!.TargetLufs);
        normalize.Close();

        var visual = new VisualizerTool([voice], loc, Run);
        visual.Show();
        visual.GetVisualDescendants().OfType<Segmented>().Last().Selected = 1;
        Wait(() => visual.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null));
        Wait(() => false, 40);
        RenderTests.Save(visual, "tool-audio-visualizer-tr.png");
        Go(visual, loc["tool.visual.run"]);
        Assert.Equal(("waveform", "9:16"), (asked!.VisualStyle, asked.Shape));
        visual.Close();

        var bleep = new BleepTool([voice], loc, Run);
        bleep.Show();
        Wait(() => Drawn(bleep));
        var lane = bleep.GetVisualDescendants().OfType<BleepLane>().Single();
        lane.Ranges.Add((0.6, 0.9)); lane.Ranges.Add((1.6, 1.9));
        lane.InvalidateVisual();
        Wait(() => false, 20);
        var update = typeof(BleepTool).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        update.Invoke(bleep, null);
        Wait(() => false, 20);
        RenderTests.Save(bleep, "tool-audio-bleep-tr.png");
        Go(bleep, loc["tool.bleep.run"]);
        Assert.Equal([0.6, 0.9, 1.6, 1.9], asked!.Ranges);
        bleep.Close();
    }
}
