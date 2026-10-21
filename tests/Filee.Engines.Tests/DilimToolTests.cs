using System.Security.Cryptography;
using System.Text.Json;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;
using Filee.Engines.Dilim;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Filee.Engines.Tests;

public sealed class DilimToolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("filee-dilim-tools-").FullName;
    public void Dispose() => Directory.Delete(_root, true);
    private string Output() => Path.Combine(_root, Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Images_are_edited_as_new_pixel_data_and_originals_are_unchanged()
    {
        var file = Path.Combine(_root, "ışık örneği.png");
        using (var image = new MagickImage(MagickColors.Transparent, 80, 40)) image.Write(file);
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        foreach (var id in new[] { "editImage", "frameImage", "redactImage", "annotateImage", "createCollage" })
        {
            var result = await ToolEngine.RunAsync(id, id == "createCollage" ? [file, file] : [file],
                new ToolRequest { Width = 20, Height = 20, Color = "#F35B28", Text = "Hi", FontSize = 12 }, Output(), null, TestContext.Current.CancellationToken);
            using var output = new MagickImage(Assert.Single(result));
            Assert.Equal(MagickFormat.Png, output.Format);
            if (id == "editImage") { Assert.Equal(40u, output.Width); Assert.Equal(80u, output.Height); }
            if (id == "createCollage") { Assert.Equal(1280u, output.Width); Assert.Equal(480u, output.Height); }
            if (id == "redactImage") Assert.Equal(MagickColors.Black, output.GetPixels().GetPixel(10, 10).ToColor());
            if (id == "frameImage") Assert.Equal(new MagickColor("#F35B28"), output.GetPixels().GetPixel(10, 10).ToColor());
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
        }
    }

    [Fact]
    public async Task Pdf_reorder_supports_reverse_ranges_and_repeated_pages_and_removes_document_metadata()
    {
        var file = Path.Combine(_root, "test.pdf");
        using (var pdf = new PdfDocument())
        {
            pdf.Info.Author = "Private Author";
            for (var i = 0; i < 3; i++) { var page = pdf.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(100 + i * 10); }
            pdf.Save(file);
        }
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        var result = await ToolEngine.RunAsync("organizePDF", [file], new ToolRequest { Pages = "3-2,1,1" }, Output(), null, TestContext.Current.CancellationToken);
        using (var pdf = PdfReader.Open(Assert.Single(result), PdfDocumentOpenMode.Import))
        {
            Assert.Equal(4, pdf.PageCount);
            Assert.Equal(120, pdf.Pages[0].Width.Point);
            Assert.Equal(110, pdf.Pages[1].Width.Point);
        }
        var stripped = await ToolEngine.RunAsync("removeMetadata", [file, file], new ToolRequest(), Output(), null, TestContext.Current.CancellationToken);
        Assert.Equal(2, stripped.Count);
        foreach (var output in stripped)
        {
            using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
            Assert.NotEqual("Private Author", pdf.Info.Author);
            Assert.Equal(3, pdf.PageCount);
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
    }

    [Theory]
    [InlineData("3-1,2", 3, new[] { 3, 2, 1, 2 })]
    [InlineData("1,1", 1, new[] { 1, 1 })]
    public void Page_order_is_explicit(string text, int count, int[] expected) => Assert.Equal(expected, ToolEngine.ParsePages(text, count));

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("1-a")]
    [InlineData("")]
    public void Invalid_pages_are_rejected(string text) => Assert.Throws<ArgumentException>(() => ToolEngine.ParsePages(text, 3));

    [Theory]
    [InlineData("compress")]
    [InlineData("removeMetadata")]
    [InlineData("normalizeAudio")]
    [InlineData("muteVideo")]
    [InlineData("trimVideo")]
    [InlineData("cropVideo")]
    [InlineData("changeVideoSpeed")]
    [InlineData("joinVideos")]
    [InlineData("videoSnapshots")]
    [InlineData("splitVideo")]
    [InlineData("audioToVideo")]
    [InlineData("trimAudio")]
    [InlineData("audioChannels")]
    [InlineData("redactVideo")]
    [InlineData("redactAudio")]
    public async Task Media_tools_write_readable_outputs_with_requested_properties(string id)
    {
        // The delivery fetches the repository's checksum-pinned FFmpeg; this test never downloads executables.
        var tools = FfmpegConverter.Locate();
        Assert.True(tools is not null, "Fetch FFmpeg with build/fetch-engines.ps1 -Only ffmpeg before running Dilim media tests.");
        var file = Path.Combine(_root, "örnek clip.mp4");
        var made = await ProcessRunner.RunAsync(tools.Value.Ffmpeg,
            ["-v", "error", "-f", "lavfi", "-i", "color=c=orange:s=320x240:r=12:d=2", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", file], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, made.ExitCode);
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        var result = await ToolEngine.RunAsync(id, id == "joinVideos" ? [file, file] : [file],
            new ToolRequest { Start = .25, End = id == "splitVideo" ? 1 : 1.25, Width = 100, Height = 80 }, Output(), null, TestContext.Current.CancellationToken);
        Assert.NotEmpty(result);
        foreach (var output in result)
        {
            Assert.True(new FileInfo(output).Length > 0);
            var probe = await ProcessRunner.RunAsync(tools.Value.Ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", output], TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Equal(0, probe.ExitCode);
            using var doc = JsonDocument.Parse(probe.StandardOutput);
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            if (id == "muteVideo") Assert.DoesNotContain(streams, s => s.GetProperty("codec_type").GetString() == "audio");
            if (id == "audioChannels") Assert.Equal(1, streams.Single(s => s.GetProperty("codec_type").GetString() == "audio").GetProperty("channels").GetInt32());
            if (id == "cropVideo") Assert.Equal(100, streams.Single(s => s.GetProperty("codec_type").GetString() == "video").GetProperty("width").GetInt32());
            if (id == "joinVideos") Assert.Equal(1280, streams.Single(s => s.GetProperty("codec_type").GetString() == "video").GetProperty("width").GetInt32());
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
    }

    [Fact]
    public async Task Photo_editor_resizes_and_adjusts_colour_without_rewriting_the_source()
    {
        var source = Path.Combine(_root, "photo.png");
        using (var image = new MagickImage(MagickColors.Orange, 80, 40)) image.Write(source);
        var output = await ToolEngine.RunAsync("editImage", [source], new ToolRequest { Rotation = 0, ResizePercent = 50, Saturation = 0 }, Output(), null, TestContext.Current.CancellationToken);
        using var result = new MagickImage(Assert.Single(output));
        Assert.Equal(40u, result.Width); Assert.Equal(20u, result.Height);
        var pixel = result.GetPixels().GetPixel(5, 5).ToColor()!;
        Assert.Equal(pixel.R, pixel.G); Assert.Equal(pixel.G, pixel.B);
        using var original = new MagickImage(source); Assert.Equal(80u, original.Width);
    }

    [Fact]
    public async Task Invalid_time_rectangle_and_cancellation_do_not_modify_the_source()
    {
        Assert.Throws<ArgumentException>(() => new ToolRequest { Start = 3, End = 1 }.Validate("trimVideo"));
        Assert.Throws<ArgumentException>(() => new ToolRequest { Speed = double.NaN }.Validate("changeVideoSpeed"));
        var file = Path.Combine(_root, "source.png");
        using (var image = new MagickImage(MagickColors.White, 10, 10)) image.Write(file);
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        await Assert.ThrowsAsync<ArgumentException>(() => ToolEngine.RunAsync("redactImage", [file], new ToolRequest(), Output(), null, TestContext.Current.CancellationToken));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ToolEngine.RunAsync("editImage", [file], new ToolRequest(), Output(), null, cts.Token));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
    }
}
