using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;
using ImageMagick;

namespace Filee.Engines.Tests;

[Collection(nameof(FfmpegCollection))]
public sealed class FileConverterParityTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    [Theory]
    [InlineData(.75, 0, 440)]
    [InlineData(1, 12, 880)]
    [InlineData(1.25, -12, 220)]
    public async Task Audio_tempo_and_pitch_are_independent_and_source_is_preserved(double speed, double pitch, double expectedFrequency)
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("Bundled FFmpeg required.");
        var folder = fx.NewFolder();
        var input = Path.Combine(folder, "tone.wav");
        var made = await ProcessRunner.RunAsync(tools.Ffmpeg,
            ["-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=2", "-c:a", "pcm_s16le", input],
            TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(0, made.ExitCode);
        var original = SHA256.HashData(File.ReadAllBytes(input));
        var job = await fx.ConvertAsync([input], new Preset { TargetFormat = "wav", Media = { AudioSpeed = speed, PitchSemitones = pitch }, Output = { FileNamePattern = "{name}_changed" } });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        var output = Assert.Single(job.Outputs);
        var probe = await ProcessRunner.RunAsync(tools.Ffprobe, ["-v", "error", "-show_entries", "format=duration", "-of", "json", output], TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(probe.StandardOutput);
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(duration, 2 / speed - .15, 2 / speed + .15);
        var data = File.ReadAllBytes(output);
        var offset = 12;
        while (offset + 8 < data.Length && !data.AsSpan(offset, 4).SequenceEqual("data"u8)) offset += 8 + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4)) + (data[offset + 4] & 1);
        Assert.True(offset + 8 < data.Length);
        var crossings = 0;
        var first = offset + 8 + 4410 * 2;
        var last = Math.Min(data.Length - 2, first + 44100 * 2);
        for (var i = first + 2; i < last; i += 2)
            if (BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i - 2)) < 0 && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i)) >= 0) crossings++;
        Assert.InRange(crossings, expectedFrequency * .95, expectedFrequency * 1.05);
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(input)));
    }

    [Fact]
    public async Task Custom_ffmpeg_options_override_codec_filter_and_frame_rate()
    {
        var tools = FfmpegConverter.Locate() ?? throw new InvalidOperationException("FFmpeg required.");
        var folder = fx.NewFolder(); var input = Path.Combine(folder, "clip.mp4");
        var made = await ProcessRunner.RunAsync(tools.Ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x240:r=24:d=1", "-c:v", "libx264", input], TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(0, made.ExitCode);
        var job = await fx.ConvertAsync([input], new Preset { TargetFormat = "mp4", Media = { UseCustomArguments = true, FfmpegArguments = "-c:v libx264 -crf 21 -vf \"scale=128:96\" -r 10" } });
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Files.Select(f => f.ErrorDetail)));
        var probe = await ProcessRunner.RunAsync(tools.Ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,avg_frame_rate", "-of", "json", Assert.Single(job.Outputs)], TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(probe.StandardOutput);
        var video = json.RootElement.GetProperty("streams")[0];
        Assert.Equal(128, video.GetProperty("width").GetInt32()); Assert.Equal(96, video.GetProperty("height").GetInt32());
        Assert.Equal("10/1", video.GetProperty("avg_frame_rate").GetString());
    }

    [Fact]
    public async Task Square_power_of_two_and_independent_side_cap_are_applied()
    {
        var folder = fx.NewFolder(); var input = EngineFixture.MakeImage(folder, "picture.png", MagickFormat.Png, 300, 180);
        var job = await fx.ConvertAsync([input], new Preset { TargetFormat = "webp", Image = { ClampPowerOfTwo = true, MaximumSize = 100 } });
        Assert.Equal(JobState.Completed, job.State);
        using var output = new MagickImage(Assert.Single(job.Outputs));
        Assert.Equal(100u, output.Width); Assert.Equal(100u, output.Height);
    }

    [Fact]
    public void Cd_layout_and_wave_header_are_validated_without_accessing_user_drives()
    {
        var cda = new byte[44]; "RIFF"u8.CopyTo(cda); "CDDA"u8.CopyTo(cda.AsSpan(8)); cda[22] = 1;
        Assert.Equal(1, CdAudioConverter.TrackNumber(cda));
        var toc = new byte[20]; toc[2] = toc[3] = 1; toc[10] = 2; toc[18] = 3;
        Assert.Equal((0, 75), CdAudioConverter.TrackExtent(toc, 1));
        var header = CdAudioConverter.WaveHeader(75);
        Assert.Equal(176400, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40)));
        Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24)));
        Assert.Throws<InvalidDataException>(() => CdAudioConverter.TrackNumber(new byte[44]));
    }
}
