using System.Globalization;
using System.Text.Json;
using Dilim.Core.Formats;
using Dilim.Core.Profiles;
using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Dilim.Engines.Tools;

/// <summary>Native image/PDF and FFmpeg edits. Each run owns a new output folder; originals are never rewritten.</summary>
public static class ToolEngine
{
    public static bool HasFfmpeg => FfmpegConverter.Locate() is not null;

    /// <summary>The ffmpeg the tools use, or null when it isn't installed.</summary>
    public static string? FfmpegPath => FfmpegConverter.Locate()?.Ffmpeg;

    public static async Task<IReadOnlyList<string>> RunAsync(string id, IReadOnlyList<string> files, ToolRequest request,
        string outputDirectory, IProgress<double>? progress, CancellationToken ct)
    {
        request.Validate(id);
        if (files.Count == 0 || files.Any(f => !File.Exists(f))) throw new FileNotFoundException("tool.error.missing");
        Directory.CreateDirectory(outputDirectory);
        if (Directory.EnumerateFileSystemEntries(outputDirectory).Any()) throw new IOException("tool.error.conflict");
        var outputs = new List<string>();
        // Work only in a directory reserved by the caller. On failure/cancellation the caller retains completed files.
        if (id == "organizePDF" || id == "removeMetadata" && Path.GetExtension(files[0]).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfOutputs = new List<string>();
            await Task.Run(() =>
            {
                foreach (var file in id == "organizePDF" ? files.Take(1) : files)
                {
                    var output = Path.Combine(outputDirectory, $"{pdfOutputs.Count + 1}-{Path.GetFileNameWithoutExtension(file)}.pdf");
                    using var source = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                    using var result = new PdfDocument();
                    foreach (var page in id == "organizePDF" ? ParsePages(request.Pages, source.PageCount) : Enumerable.Range(1, source.PageCount).ToArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        result.AddPage(source.Pages[page - 1]);
                    }
                    result.Save(output);
                    pdfOutputs.Add(output);
                }
            }, ct);
            return pdfOutputs;
        }
        if (id == "createCollage")
        {
            var output = Path.Combine(outputDirectory, "collage.png");
            await Task.Run(() =>
            {
                var columns = Math.Min(request.Columns, files.Count);
                var rows = (files.Count + columns - 1) / columns;
                using var canvas = new MagickImage(MagickColors.White, (uint)(columns * 640), (uint)(rows * 480));
                for (var i = 0; i < files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    using var image = new MagickImage(files[i]);
                    image.AutoOrient();
                    image.Resize(new MagickGeometry(620, 460));
                    canvas.Composite(image, i % columns * 640 + (640 - (int)image.Width) / 2,
                        i / columns * 480 + (480 - (int)image.Height) / 2, CompositeOperator.Over);
                }
                canvas.Write(output);
            }, ct);
            return [output];
        }
        if (id == "joinVideos")
        {
            var output = Path.Combine(outputDirectory, "joined.mp4");
            await MediaAsync(id, files, request, output, progress, ct);
            return [output];
        }
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var format = ProfileSelector.DetectFormats([file]).FirstOrDefault();
            if (format?.Category is FormatCategory.Video or FormatCategory.Audio)
            {
                var extension = format.Category == FormatCategory.Audio && id != "audioToVideo" ? ".m4a" : ".mp4";
                var stem = $"{i + 1}-{Path.GetFileNameWithoutExtension(file)}";
                if (id == "videoSnapshots" && request.Times.Length > 0)
                {
                    // Each chosen frame, full size, named by its time.
                    foreach (var time in request.Times.Distinct().Order())
                    {
                        ct.ThrowIfCancellationRequested();
                        var frame = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(file)} {Timecode(time)}.png");
                        await MediaAsync(id, [file], request with { Start = time }, frame, null, ct);
                        outputs.Add(frame);
                    }
                    progress?.Report((i + 1d) / files.Count);
                    continue;
                }
                var output = Path.Combine(outputDirectory, stem + (id == "videoSnapshots" ? ".png" : extension));
                if (id == "splitVideo") output = Path.Combine(outputDirectory, stem + "-%03d.mp4");
                await MediaAsync(id, [file], request, output, progress, ct);
                if (id == "splitVideo") outputs.AddRange(Directory.GetFiles(outputDirectory, stem + "-*.mp4"));
                else outputs.Add(output);
            }
            else
            {
                var output = Path.Combine(outputDirectory, $"{i + 1}-{Path.GetFileNameWithoutExtension(file)}.png");
                await Task.Run(() => EditImage(id, file, output, request, ct), ct);
                outputs.Add(output);
            }
            progress?.Report((i + 1d) / files.Count);
        }
        return outputs;
    }

    /// <summary>Page order is preserved, including repetitions; ranges may go backwards.</summary>
    public static IReadOnlyList<int> ParsePages(string text, int count)
    {
        var pages = new List<int>();
        foreach (var token in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = token.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0], out var start) || start < 1 || start > count)
                throw new ArgumentException("tool.error.pages");
            var end = start;
            if (ends.Length == 2 && (!int.TryParse(ends[1], out end) || end < 1 || end > count))
                throw new ArgumentException("tool.error.pages");
            for (var page = start; ; page += start <= end ? 1 : -1)
            {
                pages.Add(page);
                if (pages.Count > 10000) throw new ArgumentException("tool.error.pages");
                if (page == end) break;
            }
        }
        if (pages.Count == 0) throw new ArgumentException("tool.error.pages");
        return pages;
    }

    private static void EditImage(string id, string file, string output, ToolRequest r, CancellationToken ct)
    {
        using var image = new MagickImage(file);
        image.AutoOrient();
        // A monochrome/transparent PNG may load as grayscale. Coloured backgrounds/text must promote its pixels.
        image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = ColorType.TrueColorAlpha;
        ct.ThrowIfCancellationRequested();
        switch (id)
        {
            case "editImage":
                image.Rotate(r.Rotation);
                if (r.Mirror) image.Flop();
                if (r.ResizePercent != 100) image.Resize(new Percentage(r.ResizePercent));
                image.BrightnessContrast(new Percentage(r.Brightness), new Percentage(r.Contrast));
                image.Modulate(new Percentage(100), new Percentage(r.Saturation), new Percentage(100));
                break;
            case "frameImage":
                image.BackgroundColor = new MagickColor(r.Color);
                image.Alpha(AlphaOption.Remove);
                break;
            case "redactImage":
                CheckRectangle(r, image.Width, image.Height);
                // An opaque rectangle is baked into the pixels, never a removable overlay.
                image.Draw(new Drawables().FillColor(MagickColors.Black).StrokeColor(MagickColors.Black)
                    .Rectangle(r.X, r.Y, r.X + r.Width - 1, r.Y + r.Height - 1));
                image.Strip();
                break;
            case "annotateImage":
                image.Draw(new Drawables().Font("Arial").FontPointSize(r.FontSize)
                    .FillColor(new MagickColor(r.Color)).StrokeColor(MagickColors.Transparent).Text(r.X, r.Y + r.FontSize, r.Text));
                break;
            default: throw new ArgumentException("tool.error.unsupported");
        }
        ct.ThrowIfCancellationRequested();
        image.Write(output);
    }

    private static void CheckRectangle(ToolRequest r, uint width, uint height)
    {
        if ((long)r.X + r.Width > width || (long)r.Y + r.Height > height) throw new ArgumentException("tool.error.rectangle");
    }

    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static async Task MediaAsync(string id, IReadOnlyList<string> files, ToolRequest r, string output,
        IProgress<double>? progress, CancellationToken ct)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-progress", "pipe:1" };
        foreach (var file in files) args.AddRange(["-i", file]);
        // Probe before editing: reject silent no-op audio tools and invalid rectangles/time ranges.
        var info = await ProbeAsync(tools.Ffprobe, files[0], ct);
        var audio = info.Audio;
        var video = info.Video;
        if (id == "videoSnapshots" && info.Duration > 0 && r.Start >= info.Duration) throw new ArgumentException("tool.error.time");
        if (id is "normalizeAudio" or "audioChannels" or "redactAudio" or "audioToVideo" && !audio)
            throw new ArgumentException("tool.error.audio");
        if (id is "trimVideo" or "trimAudio" or "redactVideo" or "redactAudio")
            if (info.Duration > 0 && r.End > info.Duration + .05) throw new ArgumentException("tool.error.time");
        if (id is "redactVideo" or "cropVideo") CheckRectangle(r, (uint)info.Width, (uint)info.Height);
        string? vf = null, af = null;
        switch (id)
        {
            case "compress": args.AddRange(video ? ["-crf", "30"] : ["-b:a", "96k"]); break;
            case "removeMetadata": args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]); break;
            case "normalizeAudio": af = "loudnorm=I=-16:TP=-1.5:LRA=11"; break;
            case "muteVideo": args.Add("-an"); break;
            case "trimVideo": case "trimAudio": args.AddRange(["-ss", N(r.Start), "-t", N(r.End - r.Start)]); break;
            case "cropVideo": vf = $"crop={r.Width / 2 * 2}:{r.Height / 2 * 2}:{r.X}:{r.Y}"; break;
            case "changeVideoSpeed":
                vf = $"setpts=(PTS-STARTPTS)/{N(r.Speed)}";
                if (audio)
                {
                    var speed = r.Speed;
                    var filters = new List<string>();
                    while (speed > 2) { filters.Add("atempo=2"); speed /= 2; }
                    while (speed < .5) { filters.Add("atempo=0.5"); speed /= .5; }
                    filters.Add("atempo=" + N(speed));
                    af = string.Join(',', filters);
                }
                break;
            case "videoSnapshots": args.AddRange(["-ss", N(r.Start), "-frames:v", "1"]); break;
            case "splitVideo" when r.Times.Length > 0:
                // Cut exactly at the chosen points: a key frame is forced at each.
                var points = string.Join(',', r.Times.Where(t => t > 0.05 && (info.Duration <= 0 || t < info.Duration - 0.05)).Distinct().Order().Select(N));
                if (points.Length == 0) throw new ArgumentException("tool.error.time");
                args.AddRange(["-force_key_frames", points, "-f", "segment", "-segment_times", points, "-reset_timestamps", "1"]);
                break;
            case "splitVideo": args.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{N(r.End)})", "-f", "segment", "-segment_time", N(r.End), "-reset_timestamps", "1"]); break;
            case "audioChannels": args.AddRange(["-ac", r.Channels.ToString(CultureInfo.InvariantCulture)]); break;
            case "redactVideo": vf = $"drawbox=x={r.X}:y={r.Y}:w={r.Width}:h={r.Height}:color=black:t=fill:enable='between(t,{N(r.Start)},{N(r.End)})'"; break;
            case "redactAudio":
                // Gate both sources so speech is absent inside the censored interval.
                args.AddRange(["-filter_complex", $"[0:a]volume=0:enable='between(t,{N(r.Start)},{N(r.End)})'[clean];sine=frequency=1000:sample_rate=48000:duration={N(info.Duration)}[tone];[tone]volume=0.15:enable='between(t,{N(r.Start)},{N(r.End)})',volume=0:enable='not(between(t,{N(r.Start)},{N(r.End)}))'[bleep];[clean][bleep]amix=inputs=2:duration=first:normalize=0[out]", "-map", "[out]"]);
                break;
            case "audioToVideo": args.AddRange(["-filter_complex", "[0:a]showwaves=s=1280x720:mode=cline:colors=0xF35B28:rate=30,format=yuv420p[v]", "-map", "[v]", "-map", "0:a:0", "-shortest"]); break;
            case "joinVideos":
                var probes = new List<MediaProbe> { info };
                foreach (var file in files.Skip(1)) probes.Add(await ProbeAsync(tools.Ffprobe, file, ct));
                if (probes.Any(p => !p.Video)) throw new ArgumentException("tool.error.unsupported");
                // The first clip is the canvas: its size (even, as H.264 needs) and frame rate. The others are fitted
                // inside it without distortion. A clip without sound gets silence when any clip has sound.
                var w = Math.Max(2, info.Width / 2 * 2);
                var h = Math.Max(2, info.Height / 2 * 2);
                var fps = info.FrameRate is > 1 and <= 120 ? N(info.FrameRate) : "30";
                var withAudio = probes.Any(p => p.Audio);
                var chains = new List<string>();
                for (var i = 0; i < files.Count; i++)
                {
                    chains.Add($"[{i}:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},format=yuv420p,setpts=PTS-STARTPTS[v{i}]");
                    if (!withAudio) continue;
                    chains.Add(probes[i].Audio
                        ? $"[{i}:a]aformat=sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS[a{i}]"
                        : $"anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={N(Math.Max(0.04, probes[i].Duration))}[a{i}]");
                }
                chains.Add(string.Concat(Enumerable.Range(0, files.Count).Select(i => withAudio ? $"[v{i}][a{i}]" : $"[v{i}]")) + $"concat=n={files.Count}:v=1:a={(withAudio ? 1 : 0)}[v]" + (withAudio ? "[a]" : ""));
                args.AddRange(["-filter_complex", string.Join(';', chains), "-map", "[v]"]);
                if (withAudio) args.AddRange(["-map", "[a]"]);
                break;
            default: throw new ArgumentException("tool.error.unsupported");
        }
        var total = info.Duration;
        if (id == "joinVideos")
            foreach (var file in files.Skip(1)) total += (await ProbeAsync(tools.Ffprobe, file, ct)).Duration;
        if (vf is not null) args.AddRange(["-vf", vf]);
        if (af is not null) args.AddRange(["-af", af]);
        // Cropping the picture keeps the sound exactly as it was (copied, not encoded again) when MP4 can hold it.
        var copyAudio = id == "cropVideo" && audio && await AudioFitsMp4Async(tools.Ffprobe, files[0], ct);
        if (Path.GetExtension(output) == ".mp4") args.AddRange(["-c:v", "libx264", "-preset", "fast", "-pix_fmt", "yuv420p", "-c:a", copyAudio ? "copy" : "aac"]);
        else if (Path.GetExtension(output) == ".m4a") args.AddRange(["-vn", "-c:a", "aac"]);
        args.Add(output);
        try
        {
            var result = await FfmpegRunner.RunAsync(tools.Ffmpeg, args,
                seconds => { if (total > 0) progress?.Report(Math.Clamp(seconds / total, 0, .99)); }, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(FfmpegRunner.Summarize(result.StandardError));
        }
        catch
        {
            // Only this invocation's incomplete output. A segment job may have finished earlier segments.
            if (File.Exists(output)) File.Delete(output);
            throw;
        }
    }

    private static async Task<bool> AudioFitsMp4Async(string probe, string file, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(probe, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name", "-of", "csv=p=0", file], TimeSpan.FromSeconds(20), ct);
        return result.ExitCode == 0 && result.StandardOutput.Trim() is "aac" or "mp3" or "ac3" or "eac3" or "alac" or "opus" or "flac";
    }

    /// <summary>"0-01.60" style time for file names (no colons, which Windows refuses).</summary>
    public static string Timecode(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes}-{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    /// <summary>Duration, streams, size and frame rate of a media file, from ffprobe.</summary>
    public static async Task<MediaProbe> ProbeAsync(string file, CancellationToken ct)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        return await ProbeAsync(tools.Ffprobe, file, ct);
    }

    private static async Task<MediaProbe> ProbeAsync(string probe, string file, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", file], TimeSpan.FromSeconds(30), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError);
        using var doc = JsonDocument.Parse(result.StandardOutput);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video"
            && !(s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var pic) && pic.GetInt32() == 1));
        var hasVideo = video.ValueKind != JsonValueKind.Undefined;
        var duration = doc.RootElement.GetProperty("format").TryGetProperty("duration", out var length)
            && double.TryParse(length.GetString(), CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        var fps = hasVideo && video.TryGetProperty("r_frame_rate", out var rate) ? ParseRate(rate.GetString()) : 0;
        var rotation = hasVideo ? Rotation(video) : 0;
        int width = hasVideo ? video.GetProperty("width").GetInt32() : 0, height = hasVideo ? video.GetProperty("height").GetInt32() : 0;
        // A phone video stored sideways plays upright: its displayed size is the turned one.
        if (rotation is 90 or 270) (width, height) = (height, width);
        return new MediaProbe(duration, streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), hasVideo, width, height, fps);
    }

    private static int Rotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var side))
            foreach (var item in side.EnumerateArray())
                if (item.TryGetProperty("rotation", out var r) && r.TryGetInt32(out var degrees))
                    return ((degrees % 360) + 360) % 360;
        if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rotate)
            && int.TryParse(rotate.GetString(), out var tagged))
            return ((tagged % 360) + 360) % 360;
        return 0;
    }

    private static double ParseRate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var parts = text.Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var n)
            && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var d) && d > 0 ? n / d
            : double.TryParse(text, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}

/// <summary>What ffprobe says about a media file. Width and height are as displayed (rotation applied).</summary>
public sealed record MediaProbe(double Duration, bool Audio, bool Video, int Width, int Height, double FrameRate);
