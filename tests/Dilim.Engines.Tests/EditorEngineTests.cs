using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using Dilim.Engines.Tools;
using ImageMagick;

namespace Dilim.Engines.Tests;

public sealed class EditorEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dilim-editors", Guid.NewGuid().ToString("N")[..8]);

    public EditorEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Photo(string name)
    {
        var path = Path.Combine(_root, name);
        using var image = new MagickImage("plasma:skyblue-seagreen", 800, 600);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Dilim Camera");
        exif.SetValue(ExifTag.Artist, "HGK");
        exif.SetValue(ExifTag.ImageDescription, "Cephe");
        image.SetProfile(exif);
        image.Quality = 88;
        image.Write(path);
        return path;
    }

    [Fact]
    public async Task Picture_metadata_is_read_changed_and_removed()
    {
        var photo = Photo("cephe.jpg");
        var ct = TestContext.Current.CancellationToken;
        var fields = await MetadataEditor.ReadAsync(photo, ct);
        Assert.Contains(fields, f => f.Group == "EXIF" && f.Key == "Make" && f.Value == "Dilim Camera" && f.Editable);
        Assert.Contains(fields, f => f.Group == MetadataEditor.FileGroup && f.Name == "Dimensions" && f.Value == "800 × 600");

        var changed = Path.Combine(_root, "changed.jpg");
        await MetadataEditor.SaveAsync(photo, [new("EXIF", "Artist", "Dilim"), new("EXIF", "Make", null)], false, changed, ct);
        var after = await MetadataEditor.ReadAsync(changed, ct);
        Assert.Contains(after, f => f.Key == "Artist" && f.Value == "Dilim");
        Assert.DoesNotContain(after, f => f.Key == "Make");

        var stripped = Path.Combine(_root, "stripped.jpg");
        await MetadataEditor.SaveAsync(photo, [], true, stripped, ct);
        Assert.DoesNotContain(await MetadataEditor.ReadAsync(stripped, ct), f => f.Group == "EXIF");
    }

    [Fact]
    public async Task Media_tags_are_changed_without_encoding_again()
    {
        var ffmpeg = FfmpegConverter.Locate()?.Ffmpeg ?? throw new InvalidOperationException("FFmpeg");
        var ct = TestContext.Current.CancellationToken;
        var song = Path.Combine(_root, "song.m4a");
        Assert.Equal(0, (await ProcessRunner.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=1", "-c:a", "aac", "-metadata", "title=Eski", "-metadata", "artist=Biri", song], TimeSpan.FromSeconds(30), ct)).ExitCode);
        var fields = await MetadataEditor.ReadAsync(song, ct);
        var title = Assert.Single(fields, f => f.Key == "g|title");
        Assert.Equal("Eski", title.Value);
        var output = Path.Combine(_root, "song2.m4a");
        await MetadataEditor.SaveAsync(song, [new("Tags", "g|title", "Yeni"), new("Tags", "g|artist", null)], false, output, ct);
        var after = await MetadataEditor.ReadAsync(output, ct);
        Assert.Contains(after, f => f.Key == "g|title" && f.Value == "Yeni");
        Assert.DoesNotContain(after, f => f.Key == "g|artist");
    }

    [Fact]
    public async Task Pdf_information_is_changed()
    {
        var pdf = Path.Combine(_root, "doc.pdf");
        using (var document = new PdfSharp.Pdf.PdfDocument()) { document.AddPage(); document.Info.Title = "Taslak"; document.Info.Author = "Biri"; document.Save(pdf); }
        var ct = TestContext.Current.CancellationToken;
        Assert.Contains(await MetadataEditor.ReadAsync(pdf, ct), f => f.Key == "/Title" && f.Value == "Taslak");
        var output = Path.Combine(_root, "doc2.pdf");
        await MetadataEditor.SaveAsync(pdf, [new("Document", "/Title", "Son"), new("Document", "/Author", null)], false, output, ct);
        var after = await MetadataEditor.ReadAsync(output, ct);
        Assert.Contains(after, f => f.Key == "/Title" && f.Value == "Son");
        Assert.DoesNotContain(after, f => f.Key == "/Author");
    }

    [Fact]
    public async Task Every_annotation_kind_draws_on_the_picture()
    {
        var photo = Photo("not.jpg");
        var stamp = Photo("stamp.jpg");
        var marks = new Annotation[]
        {
            new() { Kind = AnnotationKind.Arrow, Points = [50, 50, 300, 200] },
            new() { Kind = AnnotationKind.Pen, Points = [400, 100, 420, 130, 450, 120, 480, 170] },
            new() { Kind = AnnotationKind.Highlight, Points = [100, 400, 300, 400], Color = "#FFEB3B" },
            new() { Kind = AnnotationKind.Rectangle, Points = [500, 300, 700, 450] },
            new() { Kind = AnnotationKind.Ellipse, Points = [520, 320, 680, 430] },
            new() { Kind = AnnotationKind.Text, Points = [60, 500], Text = "Giriş", Width = 40 },
            new() { Kind = AnnotationKind.Number, Points = [700, 80], Number = 3, Width = 26 },
            new() { Kind = AnnotationKind.Magnifier, Points = [200, 300, 260, 300] },
            new() { Kind = AnnotationKind.Picture, Points = [600, 480, 760, 580], PicturePath = stamp },
        };
        var output = Path.Combine(_root, "annotated.jpg");
        await AnnotationRenderer.SaveAsync(photo, marks, output, TestContext.Current.CancellationToken);
        using var before = new MagickImage(photo);
        using var after = new MagickImage(output);
        Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
        var red = after.GetPixels().GetPixel(175, 125).ToColor()!; // on the arrow's shaft
        Assert.True(red.R > 180 && red.G < 120, $"arrow colour {red}");
    }

    [Fact]
    public void Adjustments_change_the_photo_and_neutral_ones_do_not()
    {
        using var source = new MagickImage("gradient:#202020-#E0E0E0", 64, 64);
        using (var same = (MagickImage)source.Clone()) { PhotoAdjust.Apply(same, new PhotoSettings()); Assert.Equal(0, same.Compare(source, ErrorMetric.Absolute)); }
        using var brighter = (MagickImage)source.Clone();
        PhotoAdjust.Apply(brighter, new PhotoSettings { Exposure = 1 });
        Assert.True(brighter.Statistics().Composite().Mean > source.Statistics().Composite().Mean);
        using var warmer = (MagickImage)source.Clone();
        PhotoAdjust.Apply(warmer, new PhotoSettings { Temperature = 80 });
        var stats = warmer.Statistics();
        Assert.True(stats.GetChannel(PixelChannel.Red)!.Mean > stats.GetChannel(PixelChannel.Blue)!.Mean);
        foreach (var s in new[] { new PhotoSettings { Dehaze = 60, Clarity = 50, Grain = 30, NoiseReduction = 40, Saturation = 30, Vibrance = 20, Hue = 30 }, new PhotoSettings { Dehaze = -40, Clarity = -40 } })
        {
            using var copy = (MagickImage)source.Clone();
            PhotoAdjust.Apply(copy, s);
            Assert.Equal((64u, 64u), (copy.Width, copy.Height));
        }
    }
}
