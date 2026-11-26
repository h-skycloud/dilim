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
                // Sound stays in a lossless file as it was (WAV, FLAC); everything else becomes M4A (AAC).
                var source = Path.GetExtension(file).ToLowerInvariant();
                var extension = format.Category == FormatCategory.Audio && id != "audioToVideo"
                    ? source is ".wav" or ".flac" ? source : ".m4a"
                    : ".mp4";
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
                var keep = Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" ? Path.GetExtension(file).ToLowerInvariant() : ".png";
                var output = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(file)}{keep}");
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
                foreach (var box in r.BoxList())
                {
                    if ((long)box.X + box.Width > image.Width || (long)box.Y + box.Height > image.Height) throw new ArgumentException("tool.error.rectangle");
                    RedactRegion(image, box, r.Blur, r.RedactColor);
                }
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

    /// <summary>
    /// Covers a region for good: a solid colour, or pixelated into blocks a sixteenth of its size and then softened,
    /// which no "unblur" can bring back (a plain blur of text can sometimes be reversed).
    /// </summary>
    private static void RedactRegion(MagickImage image, (int X, int Y, int Width, int Height) box, bool blur, string color)
    {
        if (!blur)
        {
            image.Draw(new Drawables().FillColor(new MagickColor(color)).StrokeColor(new MagickColor(color))
                .Rectangle(box.X, box.Y, box.X + box.Width - 1, box.Y + box.Height - 1));
            return;
        }
        using var part = (MagickImage)image.CloneArea(new MagickGeometry(box.X, box.Y, (uint)box.Width, (uint)box.Height));
        var block = Math.Max(6, Math.Max(box.Width, box.Height) / 16);
        part.Scale(new MagickGeometry((uint)Math.Max(1, box.Width / block), (uint)Math.Max(1, box.Height / block)) { IgnoreAspectRatio = true });
        part.Scale(new MagickGeometry((uint)box.Width, (uint)box.Height) { IgnoreAspectRatio = true });
        part.GaussianBlur(0, block / 2.0);
        image.Composite(part, box.X, box.Y, CompositeOperator.Over);
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
        if (id is "trimVideo" or "trimAudio" or "redactVideo" or "redactAudio" && info.Duration > 0)
            if (r.Ranges.Length > 0 ? r.Ranges.Any(t => t > info.Duration + .05) : r.End > info.Duration + .05)
                throw new ArgumentException("tool.error.time");
        if (id is "redactVideo" or "cropVideo") CheckRectangle(r, (uint)info.Width, (uint)info.Height);
        string? vf = null, af = null;
        switch (id)
        {
            case "compress": args.AddRange(video ? ["-crf", "30"] : ["-b:a", "96k"]); break;
            case "removeMetadata": args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]); break;
            case "normalizeAudio": af = $"loudnorm=I={N(r.TargetLufs)}:TP={N(r.TruePeak)}:LRA={N(r.LoudnessRange)}"; break;
            case "muteVideo": args.Add("-an"); break;
            case "trimVideo": case "trimAudio":
                args.AddRange(["-ss", N(r.Start), "-t", N(r.End - r.Start)]);
                if (id == "trimAudio" && r.TrimSilence) af = AudioPreview.TrimSilenceFilter;
                break;
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
            case "audioChannels": af = AudioPreview.ChannelsFilter(r.Channels, r.LeftVolume, r.RightVolume); break;
            case "redactVideo":
            {
                // Each box covers its own time range (the whole video without one): a solid colour, or pixelated.
                var boxes = r.BoxList();
                var ranges = Enumerable.Range(0, boxes.Count).Select(k => r.Ranges.Length >= 2 * (k + 1) ? (r.Ranges[2 * k], r.Ranges[2 * k + 1]) : (r.Start, r.Boxes.Length > 0 ? info.Duration + 1 : r.End)).ToList();
                foreach (var b in boxes)
                    if (b.X + b.Width > info.Width || b.Y + b.Height > info.Height) throw new ArgumentException("tool.error.rectangle");
                string When(int k) => $"enable='between(t,{N(ranges[k].Item1)},{N(ranges[k].Item2)})'";
                if (!r.Blur)
                {
                    var color = r.RedactColor.TrimStart('#');
                    vf = string.Join(',', boxes.Select((b, k) => $"drawbox=x={b.X}:y={b.Y}:w={b.Width}:h={b.Height}:color=0x{color}:t=fill:{When(k)}"));
                }
                else
                {
                    var graph = new List<string> { $"[0:v]split={boxes.Count + 1}[base]" + string.Concat(Enumerable.Range(0, boxes.Count).Select(k => $"[c{k}]")) };
                    for (var k = 0; k < boxes.Count; k++)
                    {
                        var b = boxes[k];
                        var block = Math.Max(6, Math.Max(b.Width, b.Height) / 16);
                        graph.Add($"[c{k}]crop={b.Width}:{b.Height}:{b.X}:{b.Y},scale=max(1\\,iw/{block}):max(1\\,ih/{block}),scale={b.Width}:{b.Height}:flags=neighbor,gblur=sigma={block / 2}[p{k}]");
                    }
                    var last = "base";
                    for (var k = 0; k < boxes.Count; k++)
                    {
                        graph.Add($"[{last}][p{k}]overlay={boxes[k].X}:{boxes[k].Y}:{When(k)}[o{k}]");
                        last = $"o{k}";
                    }
                    args.AddRange(["-filter_complex", string.Join(';', graph), "-map", $"[{last}]", "-map", "0:a?"]);
                }
                break;
            }
            case "redactAudio":
                // Speech silenced and a tone in its place inside every range.
                var bleeps = r.Ranges.Length > 0
                    ? Enumerable.Range(0, r.Ranges.Length / 2).Select(k => (r.Ranges[2 * k], r.Ranges[2 * k + 1])).ToList()
                    : [(r.Start, r.End)];
                args.AddRange(["-filter_complex", "[0:a]" + AudioPreview.BleepFilter(bleeps) + "[out]", "-map", "[out]"]);
                break;
            case "audioToVideo":
            {
                var (vw, vh) = r.Shape switch { "9:16" => (720, 1280), "1:1" => (1080, 1080), _ => (1280, 720) };
                if (r.VisualStyle == "image")
                {
                    // A still picture (or the whole recording's waveform as one) for as long as the sound lasts.
                    var picture = r.ImagePath.Length > 0 && File.Exists(r.ImagePath) ? r.ImagePath : await WaveformPictureAsync(tools.Ffmpeg, files[0], vw, vh, ct);
                    args.AddRange(["-loop", "1", "-framerate", "2", "-i", picture]);
                    args.AddRange(["-filter_complex", $"[1:v]scale={vw}:{vh}:force_original_aspect_ratio=decrease,pad={vw}:{vh}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,format=yuv420p[v]",
                        "-map", "[v]", "-map", "0:a:0", "-shortest", "-tune", "stillimage"]);
                }
                else
                    args.AddRange(["-filter_complex", $"[0:a]showwaves=s={vw}x{vh / 2}:mode=cline:colors=0xF35B28:rate=30,pad={vw}:{vh}:0:(oh-ih)/2:color=black,format=yuv420p[v]",
                        "-map", "[v]", "-map", "0:a:0", "-shortest"]);
                break;
            }
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
        else if (Path.GetExtension(output) == ".m4a") args.AddRange(["-vn", "-c:a", "aac", "-b:a", "192k"]);
        else if (Path.GetExtension(output) == ".wav") args.AddRange(["-vn", "-c:a", "pcm_s16le"]);
        else if (Path.GetExtension(output) == ".flac") args.AddRange(["-vn", "-c:a", "flac"]);
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

    /// <summary>The still waveform picture a visualization would use, for the tool's preview.</summary>
    public static Task<string> WaveformPictureAsync(string file, int width, int height, CancellationToken ct) =>
        WaveformPictureAsync(FfmpegConverter.Locate()?.Ffmpeg ?? throw new InvalidOperationException("tool.error.ffmpeg"), file, width, height, ct);

    /// <summary>The waveform of the whole recording as one picture, for a still visualization.</summary>
    internal static async Task<string> WaveformPictureAsync(string ffmpeg, string file, int width, int height, CancellationToken ct)
    {
        var picture = Path.Combine(Path.GetTempPath(), "dilim-previews", Guid.NewGuid().ToString("N")[..8] + "-wave.png");
        Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
        var result = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file,
            "-filter_complex", $"showwavespic=s={width}x{height / 2}:colors=0xF35B28,pad={width}:{height}:0:(oh-ih)/2:color=black", "-frames:v", "1", picture],
            TimeSpan.FromMinutes(5), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(FfmpegRunner.Summarize(result.StandardError));
        return picture;
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
