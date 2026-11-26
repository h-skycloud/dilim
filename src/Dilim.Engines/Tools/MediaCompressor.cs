// "Compress" for pictures, video and sound: Balanced or Strong, an optional smaller size, and an optional target (a file
// size, or a share of the original). The original is never touched; the result is kept only when it is smaller.
//
// Pictures: JPEG and pictures without transparency become JPEG; WebP stays WebP; pictures with transparency stay PNG
//   with fewer colours. With a target, the quality is searched (and the picture made smaller if quality alone can't).
// Video: H.264 at a quality (CRF), or at the bitrate the target size allows. Sound: AAC at a bitrate.

using System.Globalization;
using Dilim.Core.Formats;
using Dilim.Core.Profiles;
using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using ImageMagick;

namespace Dilim.Engines.Tools;

public sealed record CompressChoice
{
    public bool Strong { get; init; }

    /// <summary>Pictures: percent of the original size (100 keeps it). Video: largest height (0 keeps it).</summary>
    public int ResizePercent { get; init; } = 100;
    public int MaxHeight { get; init; }

    /// <summary>A target: bytes, or a share of the original (0.5 = half). Neither: the preset alone.</summary>
    public long? TargetBytes { get; init; }
    public double? TargetShare { get; init; }
}

public static class MediaCompressor
{
    public static async Task<string> CompressAsync(string file, CompressChoice choice, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        var format = ProfileSelector.DetectFormats([file]).FirstOrDefault();
        var original = new FileInfo(file).Length;
        long? target = choice.TargetBytes ?? (choice.TargetShare is { } share ? (long)(original * share) : null);
        Directory.CreateDirectory(folder);
        string output = format?.Category switch
        {
            FormatCategory.Image => await Task.Run(() => Picture(file, choice, target, folder, ct), ct),
            FormatCategory.Video => await VideoAsync(file, choice, target, folder, progress, ct),
            FormatCategory.Audio => await AudioAsync(file, choice, target, folder, progress, ct),
            _ => throw new ArgumentException("tool.error.unsupported"),
        };
        // Never bigger than the original: then the original is kept as the result.
        if (new FileInfo(output).Length >= original && Path.GetExtension(output).Equals(Path.GetExtension(file), StringComparison.OrdinalIgnoreCase))
            File.Copy(file, output, overwrite: true);
        return output;
    }

    private static string Name(string folder, string file, string extension)
    {
        var path = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + extension);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(file)} ({n}){extension}");
        return path;
    }

    // ───────── Pictures ─────────

    private static string Picture(string file, CompressChoice choice, long? target, string folder, CancellationToken ct)
    {
        using var source = new MagickImage(file);
        source.AutoOrient();
        var source_ext = Path.GetExtension(file).ToLowerInvariant();
        var transparent = source.HasAlpha && !source.IsOpaque;
        var (format, extension) = transparent ? (MagickFormat.Png, ".png") : source_ext == ".webp" ? (MagickFormat.WebP, ".webp") : (MagickFormat.Jpeg, ".jpg");
        var output = Name(folder, file, extension);
        var scale = choice.ResizePercent / 100.0;
        var quality = choice.Strong ? 62 : 80;

        byte[] Encode(double size, int q)
        {
            ct.ThrowIfCancellationRequested();
            using var image = (MagickImage)source.Clone();
            if (size < 0.999) image.Resize(new Percentage(size * 100));
            image.Strip(); // camera data and thumbnails are often a good part of a small picture
            if (format == MagickFormat.Png)
            {
                // Fewer colours, as tools like pngquant do: the PNG stays lossless in shape and transparency.
                image.Quantize(new QuantizeSettings { Colors = (uint)Math.Clamp(q * 3, 32, 256), DitherMethod = DitherMethod.FloydSteinberg });
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
            }
            else image.Quality = (uint)q;
            return image.ToByteArray(format);
        }

        var bytes = Encode(scale, quality);
        if (target is { } limit && bytes.LongLength > limit)
        {
            // Quality first (to 30), then size, until it fits.
            var (low, high) = (25, quality);
            var best = bytes;
            for (var i = 0; i < 6 && high - low > 2; i++)
            {
                var middle = (low + high) / 2;
                var attempt = Encode(scale, middle);
                if (attempt.LongLength <= limit) { best = attempt; low = middle; } else high = middle;
            }
            while (best.LongLength > limit && scale > 0.1)
            {
                scale *= 0.85;
                best = Encode(scale, Math.Max(low, 30));
            }
            bytes = best;
        }
        File.WriteAllBytes(output, bytes);
        return output;
    }

    // ───────── Video and sound ─────────

    private static async Task<string> VideoAsync(string file, CompressChoice choice, long? target, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var info = await ToolEngine.ProbeAsync(file, ct);
        var output = Name(folder, file, ".mp4");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-progress", "pipe:1", "-i", file };
        if (choice.MaxHeight > 0 && info.Height > choice.MaxHeight) args.AddRange(["-vf", $"scale=-2:{choice.MaxHeight}"]);
        var audioKbps = choice.Strong ? 80 : 128;
        args.AddRange(["-c:v", "libx264", "-preset", "medium", "-pix_fmt", "yuv420p", "-movflags", "+faststart"]);
        if (target is { } bytes && info.Duration > 0)
        {
            // The bitrate the target allows, a little under for the container.
            var total = bytes * 8 / info.Duration / 1000 * 0.95;
            var videoKbps = Math.Max(80, total - (info.Audio ? audioKbps : 0));
            args.AddRange(["-b:v", K(videoKbps), "-maxrate", K(videoKbps * 1.3), "-bufsize", K(videoKbps * 2)]);
        }
        else args.AddRange(["-crf", choice.Strong ? "31" : "26"]);
        args.AddRange(info.Audio ? ["-c:a", "aac", "-b:a", audioKbps + "k"] : ["-an"]);
        args.Add(output);
        await RunAsync(tools.Ffmpeg, args, info.Duration, progress, ct);
        return output;
    }

    private static async Task<string> AudioAsync(string file, CompressChoice choice, long? target, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var info = await ToolEngine.ProbeAsync(file, ct);
        var output = Name(folder, file, ".m4a");
        var kbps = choice.Strong ? 80.0 : 128.0;
        if (target is { } bytes && info.Duration > 0) kbps = Math.Clamp(bytes * 8 / info.Duration / 1000 * 0.95, 24, 256);
        await RunAsync(tools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-progress", "pipe:1", "-i", file, "-vn", "-c:a", "aac", "-b:a", K(kbps), output],
            info.Duration, progress, ct);
        return output;
    }

    private static async Task RunAsync(string ffmpeg, List<string> args, double duration, IProgress<double>? progress, CancellationToken ct)
    {
        var result = await FfmpegRunner.RunAsync(ffmpeg, args, seconds => { if (duration > 0) progress?.Report(Math.Clamp(seconds / duration, 0, .99)); }, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(FfmpegRunner.Summarize(result.StandardError));
    }

    private static string K(double kbps) => ((int)kbps).ToString(CultureInfo.InvariantCulture) + "k";
}
