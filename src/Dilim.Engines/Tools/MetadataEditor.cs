// Reads and changes the information a file carries besides its content: a picture's EXIF, IPTC and XMP (and its colour
// profile), the tags of a recording or video (the file's, each track's, each chapter's) and a PDF's document
// information. Fields are grouped as the file stores them, so the same name can be told apart in two places.
//
// Changes are written to a copy. Pictures are saved again at their own quality (JPEG); recordings and videos are copied
// stream by stream (nothing encoded again); PDFs keep everything but the information changed.

using System.Globalization;
using System.Text.Json;
using Dilim.Core.Formats;
using Dilim.Core.Profiles;
using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Dilim.Engines.Tools;

/// <summary>One piece of information: where it lives, its key there, a readable name and its value.</summary>
public sealed record MetaField(string Group, string Key, string Name, string Value, bool Editable, bool Removable);

/// <summary>A change: a new value, or null to remove the field.</summary>
public sealed record MetaChange(string Group, string Key, string? Value);

public static class MetadataEditor
{
    public const string FileGroup = "File";

    public static Task<IReadOnlyList<MetaField>> ReadAsync(string file, CancellationToken ct = default) => Kind(file) switch
    {
        FormatCategory.Image => Task.Run(() => ReadPicture(file), ct),
        FormatCategory.Video or FormatCategory.Audio => ReadMediaAsync(file, ct),
        FormatCategory.Pdf => Task.Run(() => ReadPdf(file), ct),
        _ => throw new ArgumentException("tool.error.unsupported"),
    };

    /// <summary>Writes <paramref name="file"/> with the changes (or everything removed) to <paramref name="output"/>.</summary>
    public static Task SaveAsync(string file, IReadOnlyList<MetaChange> changes, bool removeAll, string output, CancellationToken ct = default) => Kind(file) switch
    {
        FormatCategory.Image => Task.Run(() => SavePicture(file, changes, removeAll, output), ct),
        FormatCategory.Video or FormatCategory.Audio => SaveMediaAsync(file, changes, removeAll, output, ct),
        FormatCategory.Pdf => Task.Run(() => SavePdf(file, changes, removeAll, output), ct),
        _ => throw new ArgumentException("tool.error.unsupported"),
    };

    private static FormatCategory? Kind(string file) => ProfileSelector.DetectFormats([file]).FirstOrDefault()?.Category;

    // ───────── Pictures ─────────

    private static IReadOnlyList<MetaField> ReadPicture(string file)
    {
        var fields = new List<MetaField>();
        using var image = new MagickImage(file);
        fields.Add(new(FileGroup, "size", "Dimensions", $"{image.Width} × {image.Height}", false, false));
        fields.Add(new(FileGroup, "format", "Format", image.Format.ToString(), false, false));
        fields.Add(new(FileGroup, "depth", "Bits Per Sample", image.Depth.ToString(CultureInfo.InvariantCulture), false, false));
        fields.Add(new(FileGroup, "colorspace", "Color Space", image.ColorSpace.ToString(), false, false));
        if (image.Format == MagickFormat.Jpeg && image.Quality > 0)
            fields.Add(new(FileGroup, "quality", "JPEG Quality", image.Quality.ToString(CultureInfo.InvariantCulture), false, false));
        if (image.GetExifProfile() is { } exif)
            foreach (var value in exif.Values)
            {
                var text = ExifText(value);
                if (text is null) continue;
                fields.Add(new("EXIF", value.Tag.ToString(), Spaced(value.Tag.ToString()), text, value.DataType == ExifDataType.String, true));
            }
        if (image.GetIptcProfile() is { } iptc)
            foreach (var value in iptc.Values)
                fields.Add(new("IPTC", value.Tag.ToString(), Spaced(value.Tag.ToString()), value.Value, true, true));
        if (image.GetXmpProfile() is { } xmp)
            fields.Add(new("XMP", "xmp", "XMP", $"{xmp.ToByteArray().Length / 1024.0:0.#} KB", false, true));
        if (image.GetColorProfile() is { } icc)
            fields.Add(new("ICC", "icc", "Color Profile", icc.Description ?? icc.Model ?? "ICC", false, true));
        return fields;
    }

    private static string? ExifText(IExifValue value)
    {
        try
        {
            var raw = value.GetValue();
            return raw switch
            {
                null => null,
                string s => s.TrimEnd('\0'),
                Rational r => r.ToDouble().ToString("0.####", CultureInfo.InvariantCulture),
                Rational[] rs => string.Join(", ", rs.Select(r => r.ToDouble().ToString("0.####", CultureInfo.InvariantCulture))),
                byte[] bytes => bytes.Length > 32 ? $"{bytes.Length} bytes" : Convert.ToHexString(bytes),
                Array array => string.Join(", ", array.Cast<object>()),
                _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception) { return null; }
    }

    private static void SavePicture(string file, IReadOnlyList<MetaChange> changes, bool removeAll, string output)
    {
        using var image = new MagickImage(file);
        var quality = image.Quality;
        if (removeAll)
        {
            // Everything but the picture: camera data, thumbnails, maker notes, XMP, IPTC and the colour profile
            // (converted to sRGB first, so the colours look the same without it).
            if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
            image.AutoOrient();
            image.Strip();
        }
        else
        {
            var exif = image.GetExifProfile();
            var iptc = image.GetIptcProfile();
            foreach (var change in changes)
            {
                switch (change.Group)
                {
                    case "EXIF" when exif is not null:
                        var exifValue = exif.Values.FirstOrDefault(v => v.Tag.ToString() == change.Key);
                        if (exifValue is null) break;
                        if (change.Value is null) exif.RemoveValue(exifValue.Tag);
                        else if (exifValue.DataType == ExifDataType.String) exifValue.SetValue(change.Value);
                        break;
                    case "IPTC" when iptc is not null:
                        var iptcValue = iptc.Values.FirstOrDefault(v => v.Tag.ToString() == change.Key);
                        if (iptcValue is null) break;
                        if (change.Value is null) iptc.RemoveValue(iptcValue.Tag);
                        else iptcValue.Value = change.Value;
                        break;
                    case "XMP" when change.Value is null: image.RemoveProfile("xmp"); break;
                    case "ICC" when change.Value is null:
                        image.TransformColorSpace(ColorProfiles.SRGB);
                        image.RemoveProfile("icc");
                        break;
                }
            }
            if (exif is not null) image.SetProfile(exif);
            if (iptc is not null) image.SetProfile(iptc);
        }
        if (quality > 0) image.Quality = quality;
        image.Write(output);
    }

    // ───────── Recordings and videos ─────────

    private static async Task<IReadOnlyList<MetaField>> ReadMediaAsync(string file, CancellationToken ct)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var result = await ProcessRunner.RunAsync(tools.Ffprobe, ["-v", "error", "-show_format", "-show_streams", "-show_chapters", "-of", "json", file], TimeSpan.FromSeconds(30), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardError);
        using var doc = JsonDocument.Parse(result.StandardOutput);
        var fields = new List<MetaField>();
        var format = doc.RootElement.GetProperty("format");
        if (format.TryGetProperty("format_long_name", out var formatName)) fields.Add(new(FileGroup, "format", "Format", formatName.GetString() ?? "", false, false));
        if (format.TryGetProperty("duration", out var duration)) fields.Add(new(FileGroup, "duration", "Duration", duration.GetString() + " s", false, false));
        if (format.TryGetProperty("bit_rate", out var rate) && long.TryParse(rate.GetString(), out var bps)) fields.Add(new(FileGroup, "bitrate", "Bit Rate", $"{bps / 1000} kb/s", false, false));
        Tags(format, "Tags", "g");
        var index = 0;
        foreach (var stream in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var kind = stream.TryGetProperty("codec_type", out var type) ? type.GetString() : "data";
            var group = $"Track {index + 1} ({kind})";
            if (stream.TryGetProperty("codec_long_name", out var codec)) fields.Add(new(group, "codec", "Codec", codec.GetString() ?? "", false, false));
            if (stream.TryGetProperty("width", out var w)) fields.Add(new(group, "size", "Dimensions", $"{w.GetInt32()} × {stream.GetProperty("height").GetInt32()}", false, false));
            if (stream.TryGetProperty("sample_rate", out var sr)) fields.Add(new(group, "rate", "Sample Rate", sr.GetString() + " Hz", false, false));
            if (stream.TryGetProperty("channels", out var ch)) fields.Add(new(group, "channels", "Channels", ch.GetInt32().ToString(CultureInfo.InvariantCulture), false, false));
            Tags(stream, group, $"s:{index}");
            index++;
        }
        var chapter = 0;
        if (doc.RootElement.TryGetProperty("chapters", out var chapters))
            foreach (var c in chapters.EnumerateArray())
                Tags(c, $"Chapter {++chapter}", $"c:{chapter - 1}");
        return fields;

        void Tags(JsonElement element, string group, string specifier)
        {
            if (!element.TryGetProperty("tags", out var tags)) return;
            foreach (var tag in tags.EnumerateObject())
                fields.Add(new(group, specifier + "|" + tag.Name, Spaced(tag.Name), tag.Value.GetString() ?? "", true, true));
        }
    }

    private static async Task SaveMediaAsync(string file, IReadOnlyList<MetaChange> changes, bool removeAll, string output, CancellationToken ct)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file, "-map", "0", "-c", "copy" };
        if (removeAll) args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1", "-fflags", "+bitexact"]);
        else
            foreach (var change in changes)
            {
                var parts = change.Key.Split('|', 2);
                if (parts.Length != 2) continue;
                // -metadata (file), -metadata:s:N (track), -metadata:c:N (chapter); an empty value removes the tag.
                args.Add(parts[0] == "g" ? "-metadata" : "-metadata:" + parts[0]);
                args.Add($"{parts[1]}={change.Value ?? ""}");
            }
        args.Add(output);
        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, TimeSpan.FromMinutes(30), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(FfmpegRunner.Summarize(result.StandardError));
    }

    // ───────── PDF ─────────

    private static readonly string[] PdfKeys = ["/Title", "/Author", "/Subject", "/Keywords", "/Creator", "/Producer", "/CreationDate", "/ModDate"];

    private static IReadOnlyList<MetaField> ReadPdf(string file)
    {
        using var document = PdfReader.Open(file, PdfDocumentOpenMode.Import);
        var fields = new List<MetaField>
        {
            new(FileGroup, "pages", "Pages", document.PageCount.ToString(CultureInfo.InvariantCulture), false, false),
            new(FileGroup, "version", "PDF Version", (document.Version / 10.0).ToString("0.0", CultureInfo.InvariantCulture), false, false),
        };
        var info = document.Info.Elements;
        foreach (var key in PdfKeys)
            if (info.ContainsKey(key) && info.GetString(key) is { Length: > 0 } value)
                fields.Add(new("Document", key, Spaced(key.TrimStart('/')), value, !key.EndsWith("Date", StringComparison.Ordinal), true));
        if (document.Internals.Catalog.Elements.ContainsKey("/Metadata"))
            fields.Add(new("XMP", "xmp", "XMP", "XMP", false, true));
        return fields;
    }

    private static void SavePdf(string file, IReadOnlyList<MetaChange> changes, bool removeAll, string output)
    {
        using var document = PdfReader.Open(file, PdfDocumentOpenMode.Modify);
        var info = document.Info.Elements;
        if (removeAll)
        {
            foreach (var key in PdfKeys) info.Remove(key);
            document.Internals.Catalog.Elements.Remove("/Metadata");
        }
        foreach (var change in removeAll ? [] : changes)
        {
            if (change.Group == "XMP" && change.Value is null) document.Internals.Catalog.Elements.Remove("/Metadata");
            else if (change.Group == "Document")
            {
                if (change.Value is null) info.Remove(change.Key);
                else info.SetString(change.Key, change.Value);
            }
        }
        document.Save(output);
    }

    /// <summary>"ExifImageHeight" → "Exif Image Height", "creation_time" → "Creation Time".</summary>
    private static string Spaced(string name)
    {
        var text = name.Replace('_', ' ').Replace('-', ' ');
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1])) builder.Append(' ');
            builder.Append(i == 0 || text[i - 1] == ' ' ? char.ToUpperInvariant(text[i]) : text[i]);
        }
        return builder.ToString();
    }
}
