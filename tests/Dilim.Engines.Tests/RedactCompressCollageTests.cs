using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using Dilim.Engines.Tools;
using ImageMagick;
using ImageMagick.Drawing;

namespace Dilim.Engines.Tests;

public sealed class RedactCompressCollageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dilim-rcc", Guid.NewGuid().ToString("N")[..8]);
    private int _n;

    public RedactCompressCollageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Output() => Path.Combine(_root, "out" + ++_n);

    private string Photo(string name, uint w = 1200, uint h = 800, bool alpha = false)
    {
        var path = Path.Combine(_root, name);
        using var image = new MagickImage("plasma:orange-teal", w, h);
        image.AddNoise(NoiseType.Gaussian, 0.2);
        new Drawables().FillColor(MagickColors.White).Rectangle(100, 100, 300, 200).Draw(image);
        if (alpha)
        {
            // A logo: shapes on a transparent background.
            using var logo = new MagickImage(MagickColors.Transparent, w, h);
            new Drawables().FillColor(new MagickColor("#F35B28")).Circle(w / 2.0, h / 2.0, w / 2.0, h / 4.0).Draw(logo);
            logo.Write(path);
            return path;
        }
        image.Quality = 95;
        image.Write(path);
        return path;
    }

    private static IMagickColor<byte> Pixel(string file, int x, int y)
    {
        using var image = new MagickImage(file);
        return image.GetPixels().GetPixel(x, y).ToColor()!;
    }

    [Fact]
    public async Task Several_boxes_are_redacted_solid_or_pixelated()
    {
        var photo = Photo("aile.jpg");
        var ct = TestContext.Current.CancellationToken;
        var solid = Assert.Single(await ToolEngine.RunAsync("redactImage", [photo], new ToolRequest { Boxes = [100, 100, 200, 100, 600, 400, 100, 100], RedactColor = "#FF0000" }, Output(), null, ct));
        Assert.EndsWith(".jpg", solid);
        var red = Pixel(solid, 150, 150);
        Assert.True(red.R > 200 && red.G < 60, "first box red");
        Assert.True(Pixel(solid, 650, 450).R > 200, "second box red");

        var blurred = Assert.Single(await ToolEngine.RunAsync("redactImage", [photo], new ToolRequest { Boxes = [60, 60, 300, 200], Blur = true }, Output(), null, ct));
        // The white rectangle's sharp corner is gone: blocks there mix it with the picture around.
        var c = Pixel(blurred, 101, 101);
        Assert.False(c.R > 250 && c.G > 250 && c.B > 250, "blurred box is not plain white");
    }

    [Fact]
    public async Task A_video_box_covers_only_its_time_range()
    {
        var ffmpeg = FfmpegConverter.Locate()?.Ffmpeg ?? throw new InvalidOperationException("FFmpeg");
        var ct = TestContext.Current.CancellationToken;
        var clip = Path.Combine(_root, "clip.mp4");
        Assert.Equal(0, (await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=white:s=320x240:r=25:d=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", clip], TimeSpan.FromSeconds(30), ct)).ExitCode);
        foreach (var blur in new[] { false, true })
        {
            var output = Assert.Single(await ToolEngine.RunAsync("redactVideo", [clip], new ToolRequest { Boxes = [0, 0, 100, 100, 200, 100, 100, 100], Ranges = [0, 1, 1, 2], Blur = blur }, Output(), null, ct));
            Assert.Equal((320, 240), ((await ToolEngine.ProbeAsync(output, ct)).Width, (await ToolEngine.ProbeAsync(output, ct)).Height));
            if (!blur)
            {
                // At 0.5 s the first box is black; at 1.5 s it isn't.
                var early = await ToolPreviews.VideoFrameAsync(output, 0.5, 0, ct);
                var late = await ToolPreviews.VideoFrameAsync(output, 1.5, 0, ct);
                using var a = new MagickImage(early!); using var b = new MagickImage(late!);
                Assert.True(a.GetPixels().GetPixel(50, 50).ToColor()!.R < 40);
                Assert.True(b.GetPixels().GetPixel(50, 50).ToColor()!.R > 200);
            }
        }
    }

    [Fact]
    public async Task Pictures_get_smaller_and_reach_a_target()
    {
        var photo = Photo("portakal.png", 1600, 1200);
        var original = new FileInfo(photo).Length;
        var ct = TestContext.Current.CancellationToken;
        var balanced = await MediaCompressor.CompressAsync(photo, new CompressChoice(), Output(), null, ct);
        Assert.EndsWith(".jpg", balanced); // no transparency: JPEG
        Assert.True(new FileInfo(balanced).Length < original / 3);
        var target = 120_000L;
        var fitted = await MediaCompressor.CompressAsync(photo, new CompressChoice { TargetBytes = target }, Output(), null, ct);
        Assert.True(new FileInfo(fitted).Length <= target, $"{new FileInfo(fitted).Length} > {target}");
        var half = await MediaCompressor.CompressAsync(photo, new CompressChoice { ResizePercent = 50 }, Output(), null, ct);
        using (var image = new MagickImage(half)) Assert.Equal(800u, image.Width);
        var transparent = await MediaCompressor.CompressAsync(Photo("logo.png", 600, 400, alpha: true), new CompressChoice { Strong = true }, Output(), null, ct);
        Assert.EndsWith(".png", transparent);
    }

    [Fact]
    public async Task Video_and_sound_reach_a_target_size()
    {
        var ffmpeg = FfmpegConverter.Locate()?.Ffmpeg ?? throw new InvalidOperationException("FFmpeg");
        var ct = TestContext.Current.CancellationToken;
        var clip = Path.Combine(_root, "video.mp4");
        Assert.Equal(0, (await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=4", "-f", "lavfi", "-i", "sine=frequency=500:duration=4",
            "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k", "-shortest", clip], TimeSpan.FromSeconds(60), ct)).ExitCode);
        var target = new FileInfo(clip).Length / 4;
        var small = await MediaCompressor.CompressAsync(clip, new CompressChoice { TargetBytes = target, MaxHeight = 480 }, Output(), null, ct);
        Assert.True(new FileInfo(small).Length <= target * 1.15, $"{new FileInfo(small).Length} vs {target}");
        Assert.Equal(480, (await ToolEngine.ProbeAsync(small, ct)).Height);
    }

    [Theory]
    [InlineData(CollageLayout.Grid, 2)]
    [InlineData(CollageLayout.Row, 1)]
    [InlineData(CollageLayout.Column, 1)]
    [InlineData(CollageLayout.Featured, 1)]
    public async Task Collages_have_the_chosen_canvas_and_a_cell_per_picture(CollageLayout layout, int columns)
    {
        var files = new[] { Photo("a.jpg"), Photo("b.jpg", 600, 900), Photo("c.jpg") };
        var o = new CollageOptions { Layout = layout, Columns = columns, Ratio = 4.0 / 3, Width = 1200, Spacing = 0.02, Corners = 0.02 };
        Assert.Equal(3, CollageMaker.Cells(o, 3, 1200, 900).Count);
        Assert.All(CollageMaker.Cells(o, 3, 1200, 900), c => Assert.True(c.X >= 0 && c.Y >= 0 && c.X + c.W <= 1200.5 && c.Y + c.H <= 900.5));
        var output = Path.Combine(_root, $"collage-{layout}.png");
        await CollageMaker.SaveAsync(files, o, output, TestContext.Current.CancellationToken);
        using var collage = new MagickImage(output);
        Assert.Equal((1200u, 900u), (collage.Width, collage.Height));
    }
}
