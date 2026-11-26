using Dilim.Engines.Infrastructure;
using Dilim.Engines.Media;
using Dilim.Engines.Tools;

namespace Dilim.Engines.Tests;

public sealed class AudioToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dilim-audio", Guid.NewGuid().ToString("N")[..8]);
    private int _outputs;

    public AudioToolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Output() => Path.Combine(_root, "out" + ++_outputs);

    private static string Ffmpeg => FfmpegConverter.Locate()?.Ffmpeg ?? throw new InvalidOperationException("Fetch FFmpeg with build/fetch-engines.ps1 -Only ffmpeg");

    /// <summary>"Voice with pauses": 0.5 s silence, 0.6 s tone, 0.4 s silence, 0.6 s tone, 0.4 s silence (2.5 s, stereo).</summary>
    private async Task<string> VoiceAsync(string name = "voice.wav")
    {
        var path = Path.Combine(_root, name);
        var made = await ProcessRunner.RunAsync(Ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i",
            "aevalsrc='0.6*sin(2*PI*300*t)*(between(t,0.5,1.1)+between(t,1.5,2.1))|0.3*sin(2*PI*300*t)*(between(t,0.5,1.1)+between(t,1.5,2.1))':s=44100:d=2.5",
            "-c:a", "pcm_s16le", path], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, made.ExitCode);
        return path;
    }

    [Fact]
    public async Task The_waveform_shows_the_pauses_and_each_channel()
    {
        var voice = await VoiceAsync();
        var (left, right) = await AudioPreview.WaveformAsync(voice, 50, TestContext.Current.CancellationToken);
        Assert.Equal(50, left.Length);
        Assert.True(left[2] < 0.01, "start is silent");
        Assert.True(left[15] > 0.5, "the first tone is loud");
        Assert.True(right[15] is > 0.2f and < 0.4f, "the right channel is quieter");
        Assert.True(left[26] < 0.01, "the pause is silent");
    }

    [Fact]
    public async Task Trimming_can_remove_silent_ends_and_keeps_a_wav_a_wav()
    {
        var voice = await VoiceAsync();
        var ct = TestContext.Current.CancellationToken;
        var output = Assert.Single(await ToolEngine.RunAsync("trimAudio", [voice], new ToolRequest { Start = 0, End = 2.5, TrimSilence = true }, Output(), null, ct));
        Assert.EndsWith(".wav", output);
        Assert.InRange((await ToolEngine.ProbeAsync(output, ct)).Duration, 1.45, 1.75); // 0.5–2.1 s kept
    }

    [Fact]
    public async Task Channel_volumes_and_mono_are_applied()
    {
        var voice = await VoiceAsync();
        var ct = TestContext.Current.CancellationToken;
        var output = Assert.Single(await ToolEngine.RunAsync("audioChannels", [voice], new ToolRequest { Channels = 2, LeftVolume = 0, RightVolume = 2 }, Output(), null, ct));
        var (left, right) = await AudioPreview.WaveformAsync(output, 50, ct);
        Assert.True(left.Max() < 0.01, "left muted");
        Assert.True(right.Max() > 0.5, "right doubled");
        var mono = Assert.Single(await ToolEngine.RunAsync("audioChannels", [voice], new ToolRequest { Channels = 1 }, Output(), null, ct));
        Assert.True((await ToolEngine.ProbeAsync(mono, ct)).Audio);
    }

    [Fact]
    public async Task Normalizing_reaches_the_chosen_loudness()
    {
        var voice = await VoiceAsync();
        var ct = TestContext.Current.CancellationToken;
        var before = await AudioPreview.MeasureAsync(voice, ct);
        var output = Assert.Single(await ToolEngine.RunAsync("normalizeAudio", [voice], new ToolRequest { TargetLufs = -20, LoudnessRange = 7, TruePeak = -2 }, Output(), null, ct));
        var after = await AudioPreview.MeasureAsync(output, ct);
        Assert.True(Math.Abs(after.Lufs - -20) < Math.Abs(before.Lufs - -20), $"before {before.Lufs}, after {after.Lufs}");
        Assert.True(after.Peak <= -1.5, $"peak {after.Peak}");
    }

    [Theory]
    [InlineData("waveform", "9:16", 720, 1280)]
    [InlineData("image", "1:1", 1080, 1080)]
    public async Task A_visualization_has_the_chosen_shape(string style, string shape, int width, int height)
    {
        var voice = await VoiceAsync();
        var ct = TestContext.Current.CancellationToken;
        var output = Assert.Single(await ToolEngine.RunAsync("audioToVideo", [voice], new ToolRequest { VisualStyle = style, Shape = shape }, Output(), null, ct));
        var probe = await ToolEngine.ProbeAsync(output, ct);
        Assert.Equal((width, height), (probe.Width, probe.Height));
        Assert.True(probe.Audio);
        Assert.InRange(probe.Duration, 2.2, 2.9);
    }

    [Fact]
    public async Task Several_bleeps_replace_speech_and_the_preview_filter_is_valid()
    {
        var voice = await VoiceAsync();
        var ct = TestContext.Current.CancellationToken;
        var output = Assert.Single(await ToolEngine.RunAsync("redactAudio", [voice], new ToolRequest { Ranges = [0.6, 0.9, 1.6, 1.9] }, Output(), null, ct));
        Assert.True((await ToolEngine.ProbeAsync(output, ct)).Audio);
        // The filters ffplay gets for "With bleeps", "Play output" and a channel alone are what ffmpeg accepts.
        foreach (var filter in new[] { AudioPreview.BleepFilter([(0.6, 0.9), (1.6, 1.9)]), AudioPreview.ChannelsFilter(1, 1, 0.5), AudioPreview.SoloFilter(false), AudioPreview.TrimSilenceFilter })
        {
            var run = await ProcessRunner.RunAsync(Ffmpeg, ["-v", "error", "-i", voice, "-af", filter, "-f", "null", "-"], TimeSpan.FromSeconds(30), ct);
            Assert.True(run.ExitCode == 0, filter + ": " + run.StandardError);
        }
    }
}
