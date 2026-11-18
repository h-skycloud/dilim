// Renders the demo animations of the "What's new" slides (src/Dilim.App/Assets/WhatsNew/<version>/*.webp) with the
// headless app: real windows, real donut animations, frame by frame, encoded as compact animated WebP by Magick.NET.
// The files it writes are product assets; run it again when the UI they show changes. It is explicit, so a normal test
// run skips it:
//
//   dotnet build tests/Dilim.App.Tests
//   tests/Dilim.App.Tests/bin/Debug/net10.0/Dilim.App.Tests.exe -method "Dilim.App.Tests.WhatsNewMedia.*" -explicit only
//
// Each clip is drawn on a 768 × 432 canvas (16:9, shown at 496 × 279 in the window, sharp up to 150 % scaling) in the
// light theme. The first frame of each clip is what reduced motion shows, so every clip starts on a meaningful picture.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dilim.App.Controls;
using Dilim.App.Services;
using Dilim.App.ViewModels;
using Dilim.App.ViewModels.Pages;
using Dilim.App.Views;
using Dilim.Core.Localization;
using Dilim.Core.Presets;
using Dilim.Core.Settings;
using ImageMagick;
using ImageMagick.Drawing;

namespace Dilim.App.Tests;

[Collection("Motion")]
public class WhatsNewMedia
{
    internal const int CanvasWidth = 768;
    internal const int CanvasHeight = 432;
    private const double Frame = 1 / 25.0;

    /// <summary>The light theme's PageBrush: the canvas behind every clip.</summary>
    private const string Page = "#F6F5F1";

    /// <summary>Where a version's slides live in the repository.</summary>
    internal static string Folder(string version) =>
        Path.Combine(RepositoryRoot, "src", "Dilim.App", "Assets", "WhatsNew", version);

    internal static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dilim.slnx")))
                dir = dir.Parent;
            return dir!.FullName;
        }
    }

    [AvaloniaFact(Explicit = true)]
    public void Generate_0_3_0()
    {
        TestServices.EnsureInitialized("en");
        var folder = Folder("0.3.0");
        Directory.CreateDirectory(folder);
        var theme = AppHost.Get<ThemeService>();
        // Animations on whatever this machine prefers: every theme applied here says ReduceMotion = false, since
        // ThemeService.Apply reads the setting again.
        Motion.Set(true);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            theme.Apply(new ThemeSettings { ReduceMotion = false, Mode = ThemeMode.Light }, "en");
            Write(folder, "shelf.webp", Shelf);
            Write(folder, "pie.webp", PieDonuts);
            Write(folder, "catalog.webp", () => Catalog("0.3.0"));
            Write(folder, "theme.webp", Softness);
            Write(folder, "languages.webp", () => Languages("0.3.0"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            theme.Apply(new ThemeSettings(), "en");
            TestServices.EnsureInitialized("en");
        }
    }

    // ───────────────────────── Scenes ─────────────────────────

    /// <summary>
    /// The shelf: files arrive one after another, then two of them are selected for a drag. The first frame (all files,
    /// two selected) is what reduced motion shows; the poster frame of the manifest points at the same picture.
    /// </summary>
    private static Clip Shelf()
    {
        var clip = new Clip();
        var files = Path.Combine(Path.GetTempPath(), "dilim-whatsnew-shelf");
        if (Directory.Exists(files)) Directory.Delete(files, recursive: true);
        Directory.CreateDirectory(Path.Combine(files, "Site photos"));
        string Touch(string name) { var path = Path.Combine(files, name); File.WriteAllText(path, "x"); return path; }
        var paths = new[] { Touch("Site plan.pdf"), Touch("Facade render.jpg"), Path.Combine(files, "Site photos"), Touch("Walkthrough.mp4"), Touch("Budget.xlsx") };
        var window = new ShelfWindow(AppHost.Get<ILocalizer>(), new Dilim.Core.Shelf.ShelfList(null)) { Width = 360, Height = 540 };
        try
        {
            window.Show();
            Pump();
            clip.Add(Capture(window), 900);
            foreach (var path in paths)
            {
                window.Add([path]);
                Pump();
                clip.Add(Capture(window), 450);
            }
            clip.Hold(500);
            var rows = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("shelf-item")).ToList();
            foreach (var row in rows.Take(2))
            {
                row.Classes.Add("selected");
                Pump();
                clip.Add(Capture(window), 600);
            }
            clip.Hold(1600);
        }
        finally
        {
            window.Hide();
            Directory.Delete(files, recursive: true);
        }
        return clip;
    }

    /// <summary>The pie opening for a drawing, a photo and a video: each gets its own slices.</summary>
    private static Clip PieDonuts()
    {
        var clip = new Clip();
        string[][] sets =
        [
            [@"C:\Project\Site plan.pdf"],
            [@"C:\Project\Facade render.jpg"],
            [@"C:\Project\Walkthrough.mp4"],
        ];
        foreach (var files in sets)
        {
            var (window, vm) = OpenDonut(files, clickMode: true);
            vm.Items = vm.Items.Select(Enabled).ToList();
            try
            {
                var donut = window.DonutControl;
                if (clip.Count == 0)
                {
                    donut.FinishAnimations();
                    Highlight(window, vm, 0);
                    clip.Add(Capture(window), 900);
                    donut.SetExternalHighlight(-1);
                }
                donut.PlayOpenAnimation();
                Run(clip, window, 0.4);
                for (var slice = 0; slice < Math.Min(3, vm.Items.Count); slice++)
                {
                    Highlight(window, vm, slice);
                    clip.Add(Capture(window), 700);
                }
            }
            finally
            {
                window.Close();
            }
        }
        return clip;
    }

    /// <summary>The tool catalogue, its categories one after another.</summary>
    private static Clip Catalog(string version)
    {
        var clip = new Clip();
        var vm = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        vm.Navigate("catalog");
        var window = new MainWindow { DataContext = vm, Width = 960, Height = 540 };
        try
        {
            window.Show();
            Pump();
            clip.Add(Capture(window, version), 1600);
            var categories = window.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(l => l.Classes.Contains("categories"));
            if (categories is not null)
                for (var i = 1; i < Math.Min(5, categories.ItemCount); i++)
                {
                    categories.SelectedIndex = i;
                    Pump();
                    clip.Add(Capture(window, version), 1100);
                }
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
        return clip;
    }

    /// <summary>The donut rounding off as Softness goes up, then a few accent colours.</summary>
    private static Clip Softness()
    {
        var clip = new Clip();
        var theme = AppHost.Get<ThemeService>();
        var (window, vm) = OpenDonut([@"C:\photos\a.jpg", @"C:\photos\b.jpg"], clickMode: true);
        try
        {
            window.DonutControl.FinishAnimations();
            Highlight(window, vm, 2);
            // Frame 0: soft, default colours.
            theme.Apply(new ThemeSettings { ReduceMotion = false, Mode = ThemeMode.Light, CornerRadius = 28 }, "en");
            Pump();
            clip.Add(Capture(window), 1000);
            const int steps = 14;
            for (var i = 0; i <= steps; i++)
            {
                theme.Apply(new ThemeSettings { ReduceMotion = false, Mode = ThemeMode.Light, CornerRadius = 28.0 * i / steps }, "en");
                Pump();
                clip.Add(Capture(window), i == 0 ? 500 : 60);
            }
            clip.Add(Capture(window), 500);
            foreach (var accent in new[] { "#1D9E75", "#D4537E", "#E8873A", "#7F77DD" })
            {
                theme.Apply(new ThemeSettings { ReduceMotion = false, Mode = ThemeMode.Light, CornerRadius = 28, Accent = accent }, "en");
                Pump();
                clip.Add(Capture(window), 800);
            }
        }
        finally
        {
            window.Close();
            theme.Apply(new ThemeSettings { ReduceMotion = false, Mode = ThemeMode.Light }, "en");
        }
        return clip;
    }

    /// <summary>The Home page in one language after another.</summary>
    private static Clip Languages(string version)
    {
        var clip = new Clip();
        foreach (var language in new[] { "tr", "en", "es", "hi", "ja", "ru", "ko", "zh-CN", "pl" })
        {
            TestServices.EnsureInitialized(language);
            AppHost.Get<ThemeService>().Apply(new ThemeSettings { ReduceMotion = false, Mode = ThemeMode.Light }, language);
            var vm = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
            vm.Navigate("home");
            var window = new MainWindow { DataContext = vm, Width = 960, Height = 540 };
            window.Show();
            Pump();
            clip.Add(Capture(window, version), 1100);
            window.Close();
            vm.Dispose();
        }
        TestServices.EnsureInitialized("en");
        return clip;
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static (RadialWindow Window, RadialViewModel Vm) OpenDonut(IReadOnlyList<string> files, bool clickMode)
    {
        var vm = AppHost.Get<RadialViewModel>();
        if (!clickMode)
            vm.ResetForDrag();
        vm.Load(files, clickMode);
        vm.FitToThisDonut();
        // The headless platform has no transparent windows: the page colour instead of black around the donut.
        var window = new RadialWindow { DataContext = vm, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Page)) };
        window.Show();
        Pump();
        return (window, vm);
    }

    private static DonutItem Enabled(DonutItem item) => new()
    {
        Id = item.Id,
        Label = item.Label,
        Caption = item.Caption,
        Children = item.Children?.Select(Enabled).ToList(),
    };

    private static void Highlight(RadialWindow window, RadialViewModel vm, int index)
    {
        window.DonutControl.SetExternalHighlight(index);
        vm.ShowHint(index, -1, centerHot: false);
        Settle(window.DonutControl);
    }

    /// <summary>Lets the donut's springs (highlight, hub) come to rest, and draws it.</summary>
    private static void Settle(DonutMenu donut)
    {
        for (var i = 0; i < 90; i++)
            donut.Advance(1 / 60.0);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>One frame: the donut's clock moves on by <see cref="Frame"/>.</summary>
    private static void Step(DonutMenu donut)
    {
        KeepFramesComing(donut);
        donut.Advance(Frame);
        Dispatcher.UIThread.RunJobs();
        KeepFramesComing(donut);
    }

    /// <summary>Runs the donut's animations <paramref name="seconds"/> (or until <paramref name="until"/>), a frame every <see cref="Frame"/>.</summary>
    private static void Run(Clip clip, RadialWindow window, double seconds, Func<bool>? until = null)
    {
        for (var t = 0.0; t < seconds && until?.Invoke() != true; t += Frame)
        {
            Step(window.DonutControl);
            clip.Add(Capture(window), (int)(Frame * 1000));
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Assert.True(condition());
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    private static Avalonia.Rect Lerp(Avalonia.Rect a, Avalonia.Rect b, double t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    /// <summary>
    /// A rendered frame of <paramref name="window"/> centred on the page-coloured canvas, scaled down to fit. With
    /// <paramref name="version"/>, the sidebar shows that version instead of the one being built (the clips are made
    /// before the version is bumped).
    /// </summary>
    private static MagickImage Capture(TopLevel window, string? version = null, Avalonia.Rect? region = null)
    {
        if (window is RadialWindow radial)
            KeepFramesComing(radial.DonutControl);
        try
        {
            return CaptureCore(window, version, region);
        }
        finally
        {
            if (window is RadialWindow after)
                KeepFramesComing(after.DonutControl);
        }
    }

    private static readonly System.Reflection.FieldInfo LastFrameTick =
        typeof(DonutMenu).GetField("_lastTick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DonutMenu._lastTick is gone: update WhatsNewMedia.KeepFramesComing");

    /// <summary>
    /// Taking a frame (PNG, compare) takes longer than the donut's stalled-frames watchdog waits (StalledLoopMs), which
    /// would then jump every animation to its end. The clips step the clock themselves, so the watchdog is told that
    /// frames are coming.
    /// </summary>
    private static void KeepFramesComing(DonutMenu donut) => LastFrameTick.SetValue(donut, Environment.TickCount64);

    private static MagickImage CaptureCore(TopLevel window, string? version, Avalonia.Rect? region)
    {
        if (version is not null)
        {
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
                if (text.Text?.Contains(UpdateService.CurrentVersion, StringComparison.Ordinal) == true)
                    text.Text = text.Text.Replace(UpdateService.CurrentVersion, version, StringComparison.Ordinal);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        using var png = new MemoryStream();
        if (window is RadialWindow)
        {
            // The donut is drawn straight from its state: the clips step its clock themselves, and the render timer
            // would run its own frame loop as well.
            window.UpdateLayout();
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new Avalonia.PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height)));
            bitmap.Render(window);
            bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        else
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        png.Position = 0;
        using var shot = new MagickImage(png);
        if (region is { } r)
        {
            var x = (int)Math.Clamp(r.X, 0, Math.Max(0, shot.Width - r.Width));
            var y = (int)Math.Clamp(r.Y, 0, Math.Max(0, shot.Height - r.Height));
            shot.Crop(new MagickGeometry(x, y, (uint)r.Width, (uint)r.Height));
            shot.ResetPage();
        }
        var canvas = new MagickImage(new MagickColor(Page), CanvasWidth, CanvasHeight);
        var scale = Math.Min(1, Math.Min((double)CanvasWidth / shot.Width, (double)CanvasHeight / shot.Height));
        if (scale < 1)
        {
            shot.FilterType = FilterType.Lanczos;
            shot.Resize((uint)Math.Round(shot.Width * scale), (uint)Math.Round(shot.Height * scale));
        }
        canvas.Composite(shot, Gravity.Center, CompositeOperator.Over);
        return canvas;
    }

    /// <summary>
    /// Renders and writes one clip. DILIM_WHATSNEW_ONLY (names without extension, comma-separated) limits a run to some
    /// clips, e.g. <c>DILIM_WHATSNEW_ONLY=crop,theme</c>; <paramref name="onRequestOnly"/> clips (made elsewhere) are
    /// written only when named there.
    /// </summary>
    private static void Write(string folder, string name, Func<Clip> render, bool onRequestOnly = false)
    {
        var only = Environment.GetEnvironmentVariable("DILIM_WHATSNEW_ONLY");
        var named = !string.IsNullOrWhiteSpace(only)
                    && only.Split(',', StringSplitOptions.TrimEntries).Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase);
        if (onRequestOnly ? !named : !string.IsNullOrWhiteSpace(only) && !named)
            return;
        using (var clip = render())
        {
            var path = Path.Combine(folder, name);
            clip.WriteWebP(path);
            // The first frame next to the screenshots, for review.
            clip.SaveFirstFrame(Path.Combine(TestServices.ScreenshotDirectory, "whatsnew-" + Path.ChangeExtension(name, ".png")));
            Assert.InRange(new FileInfo(path).Length, 1, 400 * 1024);
        }
    }

    /// <summary>Frames with how long each shows; a frame identical to the one before only lengthens it.</summary>
    private sealed class Clip : IDisposable
    {
        private readonly List<(MagickImage Image, int Milliseconds)> _frames = [];

        public int Count => _frames.Count;

        /// <summary>Shows the last frame <paramref name="milliseconds"/> longer.</summary>
        public void Hold(int milliseconds) => _frames[^1] = (_frames[^1].Image, _frames[^1].Milliseconds + milliseconds);

        public void Add(MagickImage image, int milliseconds)
        {
            if (_frames.Count > 0 && _frames[^1].Image.Compare(image, ErrorMetric.Absolute) == 0)
            {
                _frames[^1] = (_frames[^1].Image, _frames[^1].Milliseconds + milliseconds);
                image.Dispose();
                return;
            }
            _frames.Add((image, milliseconds));
        }

        public void WriteWebP(string path)
        {
            using var collection = new MagickImageCollection();
            foreach (var (image, milliseconds) in _frames)
            {
                var copy = image.Clone();
                // Delays are in 1/100 s; at least 2 so viewers don't speed the frame up.
                copy.AnimationDelay = (uint)Math.Max(2, Math.Round(milliseconds / 10.0));
                copy.AnimationTicksPerSecond = 100;
                copy.Quality = 82;
                collection.Add(copy);
            }
            var defines = new ImageMagick.Formats.WebPWriteDefines { Method = 6, Lossless = false, ThreadLevel = true };
            if (_frames.Count > 1)
                collection[0].Settings.SetDefine(MagickFormat.WebP, "mixed", "true");
            collection.Write(path, defines);
        }

        public void SaveFirstFrame(string path) => _frames[0].Image.Write(path, MagickFormat.Png);

        public void Dispose()
        {
            foreach (var (image, _) in _frames)
                image.Dispose();
            _frames.Clear();
        }
    }
}
