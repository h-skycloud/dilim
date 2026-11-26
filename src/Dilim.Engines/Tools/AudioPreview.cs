// What the audio tools show and play: the waveform of a recording (peaks per column, per channel) and a preview
// played through ffplay, which ships next to ffmpeg in the FFmpeg engine. A preview can apply the tool's filter, so
// "With bleeps" or "Play output" sound like the result before it is saved.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;

namespace Dilim.Engines.Tools;

public static class AudioPreview
{
    private static readonly string CacheFolder = Path.Combine(Path.GetTempPath(), "dilim-previews");

    /// <summary>
    /// Peaks of the recording in <paramref name="columns"/> columns, 0–1, for each of up to two channels (a mono
    /// recording gives the same peaks for both).
    /// </summary>
    public static async Task<(float[] Left, float[] Right)> WaveformAsync(string file, int columns, CancellationToken ct = default)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var stamp = File.GetLastWriteTimeUtc(file).Ticks + "|" + new FileInfo(file).Length;
        var raw = Path.Combine(CacheFolder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"wave|{file}|{stamp}")))[..24] + ".raw");
        Directory.CreateDirectory(CacheFolder);
        if (!File.Exists(raw))
        {
            // 4 000 samples a second per channel is plenty for a picture and quick to decode.
            var result = await ProcessRunner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file,
                "-vn", "-ac", "2", "-ar", "4000", "-f", "s16le", raw], TimeSpan.FromMinutes(5), ct);
            if (result.ExitCode != 0) { File.Delete(raw); throw new InvalidOperationException("tool.error.audio"); }
        }
        var bytes = await File.ReadAllBytesAsync(raw, ct);
        var frames = bytes.Length / 4;
        var left = new float[columns];
        var right = new float[columns];
        if (frames == 0) return (left, right);
        for (var c = 0; c < columns; c++)
        {
            var from = (long)c * frames / columns;
            var to = Math.Max(from + 1, (long)(c + 1) * frames / columns);
            int peakL = 0, peakR = 0;
            for (var f = from; f < to && f < frames; f++)
            {
                peakL = Math.Max(peakL, Math.Abs((int)BitConverter.ToInt16(bytes, (int)(f * 4))));
                peakR = Math.Max(peakR, Math.Abs((int)BitConverter.ToInt16(bytes, (int)(f * 4 + 2))));
            }
            left[c] = peakL / 32768f;
            right[c] = peakR / 32768f;
        }
        return (left, right);
    }

    /// <summary>ffplay next to ffmpeg; null when the FFmpeg engine has none (previews are then not offered).</summary>
    public static string? Player
    {
        get
        {
            if (FfmpegConverter.Locate() is not { } tools) return null;
            var player = Path.Combine(Path.GetDirectoryName(tools.Ffmpeg)!, EngineEnvironment.ProgramName("ffplay.exe"));
            return File.Exists(player) ? player : null;
        }
    }

    /// <summary>
    /// Plays <paramref name="file"/> from <paramref name="start"/> for <paramref name="duration"/> seconds (to the end
    /// when null), through <paramref name="filter"/> when given, without a window. Dispose the result to stop.
    /// </summary>
    public static Process? Play(string file, double start, double? duration, string? filter = null)
    {
        if (Player is not { } player) return null;
        var info = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-nodisp", "-autoexit", "-loglevel", "quiet", "-ss", N(start) }) info.ArgumentList.Add(arg);
        if (duration is { } d) { info.ArgumentList.Add("-t"); info.ArgumentList.Add(N(d)); }
        if (!string.IsNullOrEmpty(filter)) { info.ArgumentList.Add("-af"); info.ArgumentList.Add(filter); }
        info.ArgumentList.Add(file);
        try { return Process.Start(info); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>Stops a preview started by <see cref="Play"/>.</summary>
    public static void Stop(Process? process)
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }

    /// <summary>Integrated loudness (LUFS), loudness range (LU) and true peak (dBTP) of a recording, as loudnorm measures.</summary>
    public static async Task<(double Lufs, double Range, double Peak)> MeasureAsync(string file, CancellationToken ct = default, string? filter = null)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("tool.error.ffmpeg");
        var chain = (string.IsNullOrEmpty(filter) ? "" : filter + ",") + "loudnorm=print_format=json";
        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-nostdin", "-i", file, "-vn", "-af", chain, "-f", "null", "-"], TimeSpan.FromMinutes(10), ct);
        var text = result.StandardError + result.StandardOutput;
        var json = text[text.LastIndexOf('{')..(text.LastIndexOf('}') + 1)];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        double Get(string name) => double.Parse(doc.RootElement.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
        return (Get("input_i"), Get("input_lra"), Get("input_tp"));
    }

    // ───────── Filters the tools and their previews share ─────────

    /// <summary>Left and right volumes (0–2) into mono or stereo.</summary>
    public static string ChannelsFilter(int channels, double left, double right) => channels == 1
        ? $"aformat=channel_layouts=stereo,pan=mono|c0={N(left / 2)}*c0+{N(right / 2)}*c1"
        : $"aformat=channel_layouts=stereo,pan=stereo|c0={N(left)}*c0|c1={N(right)}*c1";

    /// <summary>One channel alone, heard on both sides (to preview it).</summary>
    public static string SoloFilter(bool left) => $"aformat=channel_layouts=stereo,pan=stereo|c0=c{(left ? 0 : 1)}|c1=c{(left ? 0 : 1)}";

    /// <summary>Speech silenced and a 1 kHz tone in its place inside each range.</summary>
    public static string BleepFilter(IReadOnlyList<(double Start, double End)> ranges)
    {
        var inside = string.Join('+', ranges.Select(r => $"between(t,{N(r.Start)},{N(r.End)})"));
        return $"volume=0:enable='{inside}'[clean];sine=frequency=1000:sample_rate=48000[tone];[tone]volume=0.15,volume=0:enable='not({inside})'[bleep];[clean][bleep]amix=inputs=2:duration=first:normalize=0";
    }

    /// <summary>Silence at the very start and end removed (the middle is left alone).</summary>
    public const string TrimSilenceFilter =
        "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.05,areverse,silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.05,areverse";

    private static string N(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
