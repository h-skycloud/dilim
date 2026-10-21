using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Filee.Core.Presets;

public sealed record FileConverterImportResult(IReadOnlyList<Preset> Presets, IReadOnlyList<string> Warnings);

/// <summary>Reads the user's XML configuration as data; no imported command is executed.</summary>
public static class FileConverterImport
{
    public static FileConverterImportResult Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 10_000_000, XmlResolver = null });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty FileConverter configuration.");
        if (root.Name.LocalName != "Settings") throw new InvalidDataException("Expected a FileConverter Settings document.");
        var presets = new List<Preset>();
        var warnings = new List<string>();
        foreach (var item in root.Elements("ConversionPreset"))
        {
            var fullName = (string?)item.Attribute("Name") ?? "Imported";
            var target = ((string?)item.Attribute("OutputType") ?? "").ToLowerInvariant();
            if (Formats.FormatRegistry.FindById(target) is not { Writable: true }) { warnings.Add(fullName + ": unsupported target " + target); continue; }
            var settings = item.Elements("Settings").ToDictionary(x => (string?)x.Attribute("Key") ?? "", x => (string?)x.Attribute("Value") ?? "");
            string[] known = ["AudioBitrate", "AudioChannelCount", "AudioEncodingMode", "EnableAudio", "EnableFFMPEGCustomCommand", "FFMPEGCustomCommand", "ImageClampSizePowerOf2", "ImageMaximumSize", "ImageQuality", "ImageRotation", "ImageScale", "VideoEncodingSpeed", "VideoFramesPerSecond", "VideoQuality", "VideoRotation", "VideoScale"];
            foreach (var key in settings.Keys.Except(known)) warnings.Add(fullName + ": unrecognized setting " + key);
            string S(string key, string fallback = "") => settings.GetValueOrDefault(key, fallback);
            double D(string key, double fallback) => double.TryParse(S(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
            int I(string key, int fallback) => (int)Math.Round(D(key, fallback));
            bool B(string key, bool fallback = false) => bool.TryParse(S(key), out var v) ? v : fallback;
            var split = fullName.LastIndexOf('/');
            var preset = new Preset
            {
                Id = "fc-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullName + "|" + target)))[..12].ToLowerInvariant(),
                Name = split < 0 ? fullName : fullName[(split + 1)..],
                Folder = split < 0 ? "FileConverter" : "FileConverter/" + fullName[..split],
                TargetFormat = target,
                SourceExtensions = item.Elements("InputTypes").Select(x => x.Value.ToLowerInvariant()).Distinct().ToList(),
            };
            preset.Image.Quality = Math.Clamp(I("ImageQuality", 90), 1, 100);
            preset.Image.ResizePercent = Math.Clamp((int)Math.Round(D("ImageScale", 1) * 100), 1, 400);
            preset.Image.Resize = preset.Image.ResizePercent == 100 ? ResizeMode.None : ResizeMode.Percent;
            preset.Image.Rotation = Rotation(I("ImageRotation", 0));
            preset.Image.MaximumSize = Math.Clamp(I("ImageMaximumSize", 0), 0, 32768);
            preset.Image.ClampPowerOfTwo = B("ImageClampSizePowerOf2");
            preset.Media.ScalePercent = Math.Clamp((int)Math.Round(D("VideoScale", 1) * 100), 1, 400);
            preset.Media.Rotation = Rotation(I("VideoRotation", 0));
            preset.Media.FrameRate = Math.Clamp(D("VideoFramesPerSecond", 0), 0, 240);
            preset.Media.RemoveAudio = !B("EnableAudio", true);
            preset.Media.AudioBitrateKbps = I("AudioBitrate", 0);
            preset.Media.AudioChannels = Math.Clamp(I("AudioChannelCount", 0), 0, 2);
            if (Enum.TryParse<VideoEncodingSpeed>(S("VideoEncodingSpeed"), true, out var speed)) preset.Media.EncodingSpeed = speed;
            if (settings.ContainsKey("VideoQuality"))
            {
                if (target is "mp4" or "mkv") preset.Media.Crf = Math.Clamp(51 - I("VideoQuality", 28), 0, 51);
                else if (target is "avi" or "webm")
                {
                    if (target == "webm") preset.Media.Crf = Math.Clamp(63 - I("VideoQuality", 40), 0, 63);
                    else preset.Media.LegacyVideoQuality = Math.Clamp(31 - I("VideoQuality", 27), 1, 31);
                }
                else if (target == "ogv") preset.Media.LegacyVideoQuality = Math.Clamp(I("VideoQuality", 8), 0, 10);
            }
            var encoding = S("AudioEncodingMode");
            preset.Media.AudioEncoding = encoding.Contains("VBR", StringComparison.OrdinalIgnoreCase) ? AudioEncodingMode.VariableBitrate
                : encoding.Contains("CBR", StringComparison.OrdinalIgnoreCase) ? AudioEncodingMode.ConstantBitrate : AudioEncodingMode.Default;
            preset.Media.WavFormat = encoding switch { "Wav8" => WavSampleFormat.Pcm8, "Wav24" => WavSampleFormat.Pcm24, "Wav32" => WavSampleFormat.Pcm32, _ => WavSampleFormat.Pcm16 };
            if (preset.Media.AudioEncoding == AudioEncodingMode.VariableBitrate)
            {
                var bitrate = preset.Media.AudioBitrateKbps;
                int[] rates = target == "mp3" ? [245, 225, 190, 175, 165, 130, 115, 100, 85, 65] : [32, 48, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 500];
                preset.Media.AudioVbrQuality = Enumerable.Range(0, rates.Length).MinBy(i => Math.Abs(rates[i] - bitrate)) - (target == "mp3" ? 0 : 2);
            }
            if (B("EnableFFMPEGCustomCommand"))
            {
                try { FfmpegOptions.Parse(S("FFMPEGCustomCommand")); preset.Media.UseCustomArguments = true; preset.Media.FfmpegArguments = S("FFMPEGCustomCommand"); }
                catch (ArgumentException ex) { warnings.Add(fullName + ": " + ex.Message); }
            }
            var post = item.Element("InputPostConversionAction")?.Value;
            if (post is not (null or "None")) warnings.Add(fullName + ": source action " + post + " imported as Keep; enable it after reviewing the preset.");
            var template = item.Element("OutputFileNameTemplate")?.Value ?? "(p)(f)";
            preset.Output.Location = OutputLocation.SameFolder;
            var folderEnd = template.LastIndexOf('\\');
            if (folderEnd >= 0)
            {
                var special = template.StartsWith("(p:m)", StringComparison.Ordinal) ? Environment.SpecialFolder.MyMusic
                    : template.StartsWith("(p:v)", StringComparison.Ordinal) ? Environment.SpecialFolder.MyVideos : (Environment.SpecialFolder?)null;
                preset.Output.Location = special.HasValue ? OutputLocation.CustomFolder : OutputLocation.Subfolder;
                preset.Output.SubfolderName = template[..folderEnd].Replace("(p:m)", "").Replace("(p:v)", "").Replace("(p)", "");
                if (special.HasValue)
                {
                    var home = Environment.GetFolderPath(special.Value);
                    if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), special == Environment.SpecialFolder.MyMusic ? "Music" : "Videos");
                    preset.Output.CustomFolder = Path.Combine(home, preset.Output.SubfolderName);
                }
                template = template[(folderEnd + 1)..];
            }
            preset.Output.FileNamePattern = template.Replace("(p)", "").Replace("(f)", "{name}").Replace("(n:i)", "{index}").Replace("(n:c)", "{count}");
            if (System.Text.RegularExpressions.Regex.IsMatch(preset.Output.FileNamePattern, @"\((?:p|n):[^)]*\)")) warnings.Add(fullName + ": review unsupported output template " + template);
            presets.Add(preset);
        }
        return new(presets.DistinctBy(p => p.Id).ToArray(), warnings);
    }

    private static ImageRotation Rotation(int counterClockwise) => (((counterClockwise % 360) + 360) % 360) switch
    {
        90 => ImageRotation.Left90,
        180 => ImageRotation.Rotate180,
        270 => ImageRotation.Right90,
        _ => ImageRotation.None,
    };
}
