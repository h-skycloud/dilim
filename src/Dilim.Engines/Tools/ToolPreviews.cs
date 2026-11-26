// Small pictures for the tool windows: a PDF page, a video frame. They are PNG bytes the window turns into a bitmap,
// made off the UI thread and cached on disk by source, size and time so reopening a tool is instant.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using PDFtoImage;
using SkiaSharp;

#pragma warning disable CA1416 // PDFtoImage is marked for the platforms PDFium ships for; Dilim only runs on those.

namespace Dilim.Engines.Tools;

public static class ToolPreviews
{
    private static readonly string CacheFolder = Path.Combine(Path.GetTempPath(), "dilim-previews");

    /// <summary>Page <paramref name="page"/> (0-based) of a PDF as PNG, <paramref name="width"/> pixels wide.</summary>
    public static Task<byte[]?> PdfPageAsync(string file, int page, int width, CancellationToken ct = default) =>
        CachedAsync($"pdf|{file}|{page}|{width}", async path =>
        {
            await Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(file);
                ct.ThrowIfCancellationRequested();
                using var bitmap = Conversion.ToImage(bytes, page: page, password: null,
                    options: new RenderOptions(Width: width, WithAnnotations: true, BackgroundColor: SKColors.White, WithAspectRatio: true));
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(path, data.ToArray());
            }, ct);
        }, file, ct);

    /// <summary>A picture, turned upright, <paramref name="width"/> pixels wide at most, as PNG.</summary>
    public static Task<byte[]?> ImageAsync(string file, int width, CancellationToken ct = default) =>
        CachedAsync($"image|{file}|{width}", path => Task.Run(() =>
        {
            using var image = new ImageMagick.MagickImage(file);
            image.AutoOrient();
            if (image.Width > width) image.Resize((uint)width, 0);
            image.Write(path, ImageMagick.MagickFormat.Png);
        }, ct), file, ct);

    /// <summary>The same picture pixelated and blurred, for previews of blurred redactions.</summary>
    public static Task<byte[]?> BlurredImageAsync(string file, int width, CancellationToken ct = default) =>
        CachedAsync($"blur|{file}|{width}", path => Task.Run(() =>
        {
            using var image = new ImageMagick.MagickImage(file);
            image.AutoOrient();
            if (image.Width > width) image.Resize((uint)width, 0);
            var block = Math.Max(4, (int)image.Width / 60);
            var (w, h) = (image.Width, image.Height);
            image.Scale(new ImageMagick.MagickGeometry((uint)Math.Max(1, w / block), (uint)Math.Max(1, h / block)) { IgnoreAspectRatio = true });
            image.Scale(new ImageMagick.MagickGeometry(w, h) { IgnoreAspectRatio = true });
            image.GaussianBlur(0, block / 2.0);
            image.Write(path, ImageMagick.MagickFormat.Png);
        }, ct), file, ct);

    /// <summary>The number of pages of a PDF.</summary>
    public static int PdfPageCount(string file) => Conversion.GetPageCount(File.ReadAllBytes(file), password: null);

    /// <summary>The frame of a video at <paramref name="seconds"/> as PNG, <paramref name="width"/> pixels wide (0: full size).</summary>
    public static Task<byte[]?> VideoFrameAsync(string file, double seconds, int width, CancellationToken ct = default) =>
        CachedAsync($"video|{file}|{seconds:0.000}|{width}", async path =>
        {
            var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
            var scale = width > 0 ? $"scale={width}:-2" : "null";
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", file,
                "-frames:v", "1", "-vf", scale, "-f", "image2", "-c:v", "png", path };
            var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, TimeSpan.FromSeconds(30), ct);
            if (result.ExitCode != 0 || !File.Exists(path))
            {
                // Past the end of a short clip ffmpeg writes nothing: take the very first frame instead.
                if (seconds > 0) { File.Delete(path); args[6] = "0"; await ProcessRunner.RunAsync(tools.Ffmpeg, args, TimeSpan.FromSeconds(30), ct); }
            }
        }, file, ct);

    /// <summary>
    /// <paramref name="count"/> frames spread evenly over the video, <paramref name="width"/> pixels wide, in one pass:
    /// the film strip of the trim and split tools, and the frames a silent preview plays.
    /// </summary>
    public static async Task<IReadOnlyList<byte[]>> FramesAsync(string file, double duration, int count, int width, CancellationToken ct = default)
    {
        if (duration <= 0 || count <= 0) return [];
        try
        {
            var stamp = File.GetLastWriteTimeUtc(file).Ticks + "|" + new FileInfo(file).Length;
            var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"frames|{file}|{count}|{width}|{stamp}")))[..24];
            var folder = Path.Combine(CacheFolder, key);
            if (!Directory.Exists(folder) || Directory.GetFiles(folder, "*.jpg").Length == 0)
            {
                var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
                var work = folder + ".part";
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                var rate = (count / duration).ToString("0.######", CultureInfo.InvariantCulture);
                var result = await ProcessRunner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file,
                    "-vf", $"fps={rate},scale={width}:-2", "-frames:v", count.ToString(CultureInfo.InvariantCulture), "-q:v", "4",
                    Path.Combine(work, "%04d.jpg")], TimeSpan.FromMinutes(5), ct);
                if (result.ExitCode != 0) return [];
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.Move(work, folder);
            }
            var frames = new List<byte[]>();
            foreach (var path in Directory.GetFiles(folder, "*.jpg").Order(StringComparer.Ordinal))
                frames.Add(await File.ReadAllBytesAsync(path, ct));
            return frames;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return []; }
    }

    private static async Task<byte[]?> CachedAsync(string key, Func<string, Task> render, string source, CancellationToken ct)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(source).Ticks + "|" + new FileInfo(source).Length;
            var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + stamp)))[..24] + ".png";
            Directory.CreateDirectory(CacheFolder);
            var path = Path.Combine(CacheFolder, name);
            if (!File.Exists(path)) await render(path);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; } // a preview is never worth an error message
    }
}
