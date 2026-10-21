using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;

namespace Filee.Engines.Tests;

[Collection(nameof(FfmpegCollection))]
public sealed class VideoTuningTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static readonly VideoStreamInfo Source = new(320, 240, 1, 0, 24, "h264", "yuv420p");
    private static readonly MediaInfo Info = new(TimeSpan.FromSeconds(1), Source, new AudioStreamInfo(2, 48000, "aac"), null);
    private static IReadOnlyList<string> Args(string target, MediaOptions options) =>
        MediaEncoding.Plan("in.mp4", "out." + target, target, options, Info, "work").Single().Arguments;
    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Theory]
    [InlineData("mp4", VideoCodec.Default, "libx264", "slow")]
    [InlineData("mkv", VideoCodec.Hevc, "libx265", "slow")]
    [InlineData("mp4", VideoCodec.Av1, "libsvtav1", "6")]
    [InlineData("webm", VideoCodec.Default, "libvpx-vp9", "2")]
    public void Fine_quality_and_effort_force_encoding_and_reach_each_codec(string target, VideoCodec codec, string encoder, string speed)
    {
        var options = new MediaOptions { VideoCodec = codec, Crf = 27, EncodingSpeed = VideoEncodingSpeed.Slow, HardwareEncoding = true };
        var args = Args(target, options);
        Assert.Equal(encoder, After(args, "-c:v"));
        Assert.Equal("27", After(args, "-crf"));
        Assert.Equal(speed, After(args, encoder == "libvpx-vp9" ? "-cpu-used" : "-preset"));
        Assert.Null(MediaEncoding.HardwareCodec(target, options, Info));
    }

    [Fact]
    public void Unchanged_video_remuxes_but_each_explicit_transform_reencodes()
    {
        Assert.True(MediaEncoding.CanCopyVideo("mp4", Source, new MediaOptions()));
        foreach (var options in new[]
        {
            new MediaOptions { FrameRate = 23.976 }, new MediaOptions { ScalePercent = 50 },
            new MediaOptions { Rotation = ImageRotation.Right90 }, new MediaOptions { Crf = 0 },
            new MediaOptions { EncodingSpeed = VideoEncodingSpeed.VeryFast },
        })
        {
            Assert.False(MediaEncoding.CanCopyVideo("mp4", Source, options));
            Assert.Equal("libx264", After(Args("mp4", options), "-c:v"));
        }
    }

    [Fact]
    public void Decimal_fps_is_invariant_and_crf_is_limited_per_codec()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Contains("fps=23.976", After(Args("mp4", new MediaOptions { FrameRate = 23.976 }), "-vf"));
            Assert.Equal("51", After(Args("mp4", new MediaOptions { Crf = 99 }), "-crf"));
            Assert.Equal("63", After(Args("webm", new MediaOptions { Crf = 99 }), "-crf"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData("mp4", VideoCodec.Default, "h264")]
    [InlineData("mkv", VideoCodec.Hevc, "hevc")]
    [InlineData("webm", VideoCodec.Default, "vp9")]
    public async Task Real_conversion_applies_rotation_scale_fps_and_mono_without_touching_original(string target, VideoCodec codec, string expectedCodec)
    {
        var tools = FfmpegConverter.Locate();
        Assert.True(tools.HasValue, "Bundled FFmpeg is needed for this delivery check.");
        var folder = fx.NewFolder();
        var source = Path.Combine(folder, "ışık clip.mp4");
        var made = await ProcessRunner.RunAsync(tools.Value.Ffmpeg,
            ["-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x240:r=24:d=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-ac", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source],
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, made.ExitCode);
        var hash = SHA256.HashData(File.ReadAllBytes(source));
        var job = await fx.ConvertAsync([source], new Preset
        {
            TargetFormat = target,
            Media = { Crf = 28, EncodingSpeed = VideoEncodingSpeed.VeryFast, VideoCodec = codec,
                Rotation = ImageRotation.Right90, ScalePercent = 50, FrameRate = 12, MaxHeight = 100, AudioChannels = 1 },
            Output = { FileNamePattern = "{name}_tuned" },
        });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        var probe = await ProcessRunner.RunAsync(tools.Value.Ffprobe,
            ["-v", "error", "-show_streams", "-of", "json", Assert.Single(job.Outputs)], TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(0, probe.ExitCode);
        using var json = JsonDocument.Parse(probe.StandardOutput);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Equal(expectedCodec, video.GetProperty("codec_name").GetString());
        Assert.Equal(100, video.GetProperty("height").GetInt32());
        Assert.InRange(video.GetProperty("width").GetInt32(), 74, 76);
        Assert.Equal("12/1", video.GetProperty("avg_frame_rate").GetString());
        Assert.Equal(1, streams.Single(s => s.GetProperty("codec_type").GetString() == "audio").GetProperty("channels").GetInt32());
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
    }

    [Fact]
    public async Task Gif_conversion_honors_frame_rate_rotation_and_scale()
    {
        var tools = FfmpegConverter.Locate();
        Assert.True(tools.HasValue);
        var folder = fx.NewFolder();
        var input = Path.Combine(folder, "animation.mp4");
        var made = await ProcessRunner.RunAsync(tools.Value.Ffmpeg,
            ["-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x240:r=24:d=1", "-c:v", "libx264", input],
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, made.ExitCode);
        var job = await fx.ConvertAsync([input], new Preset { TargetFormat = "gif", Media = { FrameRate = 8, Rotation = ImageRotation.Left90, ScalePercent = 50 } });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        using var frames = new ImageMagick.MagickImageCollection(Assert.Single(job.Outputs));
        Assert.Equal(8, frames.Count);
        Assert.Equal(120u, frames[0].Width);
        Assert.Equal(160u, frames[0].Height);
    }
}
