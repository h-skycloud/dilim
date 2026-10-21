using Filee.Core.Conversion;
using Filee.Core.Presets;

namespace Filee.Core.Tests;

public sealed class ConverterParityTests
{
    [Fact]
    public void Import_preserves_folders_vbr_quality_rotation_custom_options_and_leaves_destructive_actions_off()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "settings.xml");
        File.WriteAllText(path, """
            <Settings>
              <ConversionPreset Name="Video/720p" OutputType="Mp4">
                <InputTypes>mp4</InputTypes><InputTypes>mov</InputTypes>
                <Settings Key="VideoQuality" Value="30"/><Settings Key="VideoRotation" Value="90"/>
                <Settings Key="VideoEncodingSpeed" Value="Slow"/>
                <Settings Key="EnableFFMPEGCustomCommand" Value="True"/>
                <Settings Key="FFMPEGCustomCommand" Value="-vf &quot;scale=1280:720&quot; -crf 21"/>
                <InputPostConversionAction>Delete</InputPostConversionAction>
                <OutputFileNameTemplate>(p)(f) (n:i) of (n:c)</OutputFileNameTemplate>
              </ConversionPreset>
              <ConversionPreset Name="Audio/VBR" OutputType="Mp3">
                <Settings Key="AudioEncodingMode" Value="Mp3VBR"/><Settings Key="AudioBitrate" Value="165"/>
              </ConversionPreset>
            </Settings>
            """);
        var result = FileConverterImport.Read(path);
        var video = result.Presets[0];
        Assert.Equal("FileConverter/Video", video.Folder);
        Assert.Equal(21, video.Media.Crf);
        Assert.Equal(ImageRotation.Left90, video.Media.Rotation);
        Assert.True(video.Media.UseCustomArguments);
        Assert.Equal(SourceAction.Keep, video.Output.AfterConversion);
        Assert.Single(result.Warnings);
        Assert.Equal("{name} {index} of {count}", video.Output.FileNamePattern);
        Assert.Equal(4, result.Presets[1].Media.AudioVbrQuality);
        Assert.Equal(AudioEncodingMode.VariableBitrate, result.Presets[1].Media.AudioEncoding);
    }

    [Fact]
    public void Raw_options_are_arguments_not_a_shell_and_input_output_paths_are_owned_by_the_application()
    {
        Assert.Equal(["-vf", "scale=1280:720,format=yuv420p", "-af", "atempo=0.75"],
            FfmpegOptions.Parse("-vf \"scale=1280:720,format=yuv420p\" -af 'atempo=0.75'"));
        foreach (var text in new[] { "-i secret.mp4", "-y", "out.mp4", "-crf", "-af \"unclosed" })
            Assert.Throws<ArgumentException>(() => FfmpegOptions.Parse(text));
    }

    [Theory]
    [InlineData(SourceAction.Archive)]
    [InlineData(SourceAction.Delete)]
    public void Source_actions_require_a_successful_job_and_never_overwrite_archive_collisions(SourceAction action)
    {
        using var directory = new TempDir();
        var source = Path.Combine(directory.Path, "photo.png");
        var output = Path.Combine(directory.Path, "photo.webp");
        File.WriteAllText(source, "original"); File.WriteAllText(output, "converted");
        Directory.CreateDirectory(Path.Combine(directory.Path, "Originals"));
        File.WriteAllText(Path.Combine(directory.Path, "Originals", "photo.png"), "older");
        var preset = new Preset { Output = { AfterConversion = action } };
        var job = new ConversionJob([source], preset, "test");
        job.Files[0].State = FileState.Done; job.Files[0].Outputs.Add(output);
        job.State = JobState.CompletedWithErrors;
        SourceDisposition.Apply(job);
        Assert.True(File.Exists(source));
        job.State = JobState.Completed;
        SourceDisposition.Apply(job);
        Assert.False(File.Exists(source));
        Assert.Equal("older", File.ReadAllText(Path.Combine(directory.Path, "Originals", "photo.png")));
        if (action == SourceAction.Archive) Assert.Equal("original", File.ReadAllText(Path.Combine(directory.Path, "Originals", "photo (2).png")));
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Output_aliasing_a_source_never_deletes_it()
    {
        using var directory = new TempDir();
        var source = Path.Combine(directory.Path, "source.txt"); File.WriteAllText(source, "keep");
        var job = new ConversionJob([source], new Preset { Output = { AfterConversion = SourceAction.Delete } }, "test") { State = JobState.Completed };
        job.Files[0].State = FileState.Done; job.Files[0].Outputs.Add(source);
        SourceDisposition.Apply(job);
        Assert.True(File.Exists(source));
        Assert.Equal("notice.source_action_skipped", job.Files[0].NoticeKey);
    }
}
