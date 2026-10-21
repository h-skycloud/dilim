using System.Globalization;
using System.Text.Json;
using Filee.Core.Formats;
using Filee.Core.Profiles;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;
using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Filee.Engines.Dilim;

/// <summary>Native image/PDF and FFmpeg edits. Each run owns a new output folder; originals are never rewritten.</summary>
public static class ToolEngine
{
    public static bool HasFfmpeg => FfmpegConverter.Locate() is not null;

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
            case "cropVideo": vf = $"crop={r.Width}:{r.Height}:{r.X}:{r.Y}"; break;
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
            case "splitVideo": args.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{N(r.End)})", "-f", "segment", "-segment_time", N(r.End), "-reset_timestamps", "1"]); break;
            case "audioChannels": args.AddRange(["-ac", r.Channels.ToString(CultureInfo.InvariantCulture)]); break;
            case "redactVideo": vf = $"drawbox=x={r.X}:y={r.Y}:w={r.Width}:h={r.Height}:color=black:t=fill:enable='between(t,{N(r.Start)},{N(r.End)})'"; break;
            case "redactAudio":
                // Gate both sources so speech is absent inside the censored interval.
                args.AddRange(["-filter_complex", $"[0:a]volume=0:enable='between(t,{N(r.Start)},{N(r.End)})'[clean];sine=frequency=1000:sample_rate=48000:duration={N(info.Duration)}[tone];[tone]volume=0.15:enable='between(t,{N(r.Start)},{N(r.End)})',volume=0:enable='not(between(t,{N(r.Start)},{N(r.End)}))'[bleep];[clean][bleep]amix=inputs=2:duration=first:normalize=0[out]", "-map", "[out]"]);
                break;
            case "audioToVideo": args.AddRange(["-filter_complex", "[0:a]showwaves=s=1280x720:mode=cline:colors=0xF35B28:rate=30,format=yuv420p[v]", "-map", "[v]", "-map", "0:a:0", "-shortest"]); break;
            case "joinVideos":
                var probes = new List<(double Duration, bool Audio, bool Video, int Width, int Height)> { info };
                foreach (var file in files.Skip(1)) probes.Add(await ProbeAsync(tools.Ffprobe, file, ct));
                if (probes.Any(p => !p.Video)) throw new ArgumentException("tool.error.unsupported");
                // Normalize dimensions, time bases and sample rates. Require all clips to agree about an audio track.
                if (probes.Any(p => p.Audio != audio)) throw new ArgumentException("tool.error.join_audio");
                var chains = new List<string>();
                for (var i = 0; i < files.Count; i++)
                {
                    chains.Add($"[{i}:v]scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,setpts=PTS-STARTPTS[v{i}]");
                    if (audio) chains.Add($"[{i}:a]aformat=sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS[a{i}]");
                }
                chains.Add(string.Concat(Enumerable.Range(0, files.Count).Select(i => audio ? $"[v{i}][a{i}]" : $"[v{i}]")) + $"concat=n={files.Count}:v=1:a={(audio ? 1 : 0)}[v]" + (audio ? "[a]" : ""));
                args.AddRange(["-filter_complex", string.Join(';', chains), "-map", "[v]"]);
                if (audio) args.AddRange(["-map", "[a]"]);
                break;
            default: throw new ArgumentException("tool.error.unsupported");
        }
        if (vf is not null) args.AddRange(["-vf", vf]);
        if (af is not null) args.AddRange(["-af", af]);
        if (Path.GetExtension(output) == ".mp4") args.AddRange(["-c:v", "libx264", "-preset", "fast", "-pix_fmt", "yuv420p", "-c:a", "aac"]);
        else if (Path.GetExtension(output) == ".m4a") args.AddRange(["-vn", "-c:a", "aac"]);
        args.Add(output);
        try
        {
            var result = await FfmpegRunner.RunAsync(tools.Ffmpeg, args,
                seconds => { if (info.Duration > 0) progress?.Report(Math.Clamp(seconds / info.Duration, 0, .99)); }, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(FfmpegRunner.Summarize(result.StandardError));
        }
        catch
        {
            // Only this invocation's incomplete output. A segment job may have finished earlier segments.
            if (File.Exists(output)) File.Delete(output);
            throw;
        }
    }

    private static async Task<(double Duration, bool Audio, bool Video, int Width, int Height)> ProbeAsync(string probe, string file, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", file], TimeSpan.FromSeconds(30), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError);
        using var doc = JsonDocument.Parse(result.StandardOutput);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        var hasVideo = video.ValueKind != JsonValueKind.Undefined;
        var duration = doc.RootElement.GetProperty("format").TryGetProperty("duration", out var d)
            && double.TryParse(d.GetString(), CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        return (duration, streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), hasVideo,
            hasVideo ? video.GetProperty("width").GetInt32() : 0, hasVideo ? video.GetProperty("height").GetInt32() : 0);
    }
}
