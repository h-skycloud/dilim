using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Engines.Dilim;

namespace Filee.App.Tests;

public sealed class ConversionSettingsTests
{
    [AvaloniaFact]
    public void Turkish_video_controls_bind_decimal_fps_and_render_in_a_scrollable_editor()
    {
        TestServices.EnsureInitialized("tr");
        var vm = new PresetEditorViewModel(new Preset
        {
            Name = "Video",
            TargetFormat = "mp4",
            Media = { FrameRate = 23.976, Crf = 21, EncodingSpeed = VideoEncodingSpeed.Slow }
        }, AppHost.Get<ILocalizer>());
        var window = new PresetEditorWindow { DataContext = vm, Height = 900 };
        try
        {
            window.Show();
            RenderTests.Pump();
            var controls = window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            var fps = Assert.Single(controls, n => n.Maximum == 240);
            Assert.Equal(23.976m, fps.Value);
            fps.Value = 29.97m;
            RenderTests.Pump();
            Assert.Equal(29.97, vm.FrameRate);
            RenderTests.Save(window, "dilim-video-tuning-tr.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Video_editor_saves_fine_settings_and_switching_to_hardware_removes_software_overrides()
    {
        TestServices.EnsureInitialized("tr");
        var preset = new Preset { TargetFormat = "mp4", Name = "Video" };
        var vm = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());
        Assert.True(vm.ShowVideoTransforms && vm.ShowVideoTuning && vm.ShowAudioChannels);
        vm.FrameRate = 23.976;
        vm.VideoScalePercent = 50;
        vm.VideoRotation = vm.Rotations.Single(r => r.Value == ImageRotation.Right90);
        vm.CustomCrf = true; vm.Crf = 21;
        vm.EncodingSpeed = vm.EncodingSpeeds.Single(s => s.Value == VideoEncodingSpeed.Slow);
        Assert.True(vm.Apply());
        var reopened = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());
        Assert.Equal(23.976, reopened.FrameRate);
        Assert.Equal(50, reopened.VideoScalePercent);
        Assert.Equal(ImageRotation.Right90, reopened.VideoRotation.Value);
        Assert.Equal(21, reopened.Crf);
        Assert.Equal(VideoEncodingSpeed.Slow, reopened.EncodingSpeed.Value);
        reopened.HardwareEncoding = true;
        Assert.False(reopened.ShowSoftwareTuning);
        Assert.True(reopened.Apply());
        Assert.Null(preset.Media.Crf);
        Assert.Equal(VideoEncodingSpeed.Default, preset.Media.EncodingSpeed);
        Assert.True(preset.Media.HardwareEncoding);
        Assert.Equal(23.976, preset.Media.FrameRate);
    }

    [AvaloniaFact]
    public void Gif_has_frame_controls_without_crf_and_audio_target_clears_hidden_video_settings()
    {
        TestServices.EnsureInitialized("en");
        var preset = new Preset { TargetFormat = "gif", Name = "Animation" };
        var vm = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());
        Assert.True(vm.ShowMedia && vm.ShowVideoTransforms);
        Assert.False(vm.ShowVideoTuning);
        vm.FrameRate = 8; vm.VideoScalePercent = 50;
        Assert.True(vm.Apply());
        Assert.Equal(8, preset.Media.FrameRate);
        vm.Target = vm.Targets.Single(t => t.Value == "mp3");
        Assert.False(vm.ShowVideoTransforms);
        Assert.True(vm.Apply());
        Assert.Equal(0, preset.Media.FrameRate);
        Assert.Equal(100, preset.Media.ScalePercent);
    }

    [AvaloniaFact]
    public void Pdf_editor_reopens_custom_picture_quality_and_resolution()
    {
        TestServices.EnsureInitialized("tr");
        var preset = new Preset { TargetFormat = "pdf", Name = "PDF" };
        var vm = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());
        vm.CompressionLevel = vm.CompressionLevels.Single(c => c.Value == PdfCompression.Ebook);
        vm.CustomPdfCompression = true; vm.PdfCompressionDpi = 120; vm.PdfCompressionQuality = 60;
        Assert.True(vm.Apply());
        var reopened = new PresetEditorViewModel(preset, AppHost.Get<ILocalizer>());
        Assert.True(reopened.CustomPdfCompression);
        Assert.Equal(120, reopened.PdfCompressionDpi);
        Assert.Equal(60, reopened.PdfCompressionQuality);
    }

    [Fact]
    public void Quick_compression_uses_the_requested_image_pdf_video_and_audio_settings()
    {
        var request = new ToolRequest
        {
            ImageFormat = "jpg",
            Quality = 82,
            ResizePercent = 50,
            PdfDpi = 120,
            PdfQuality = 60,
            VideoCrf = 24,
            VideoHeight = 720,
            VideoFrameRate = 24,
            VideoCodec = VideoCodec.Hevc,
            AudioBitrateKbps = 96
        };
        var image = DilimService.CompressionPreset(FormatCategory.Image, request);
        Assert.Equal("jpg", image.TargetFormat);
        Assert.Equal(82, image.Image.Quality);
        Assert.Equal(ResizeMode.Percent, image.Image.Resize);
        Assert.Equal(50, image.Image.ResizePercent);
        var pdf = DilimService.CompressionPreset(FormatCategory.Pdf, request);
        Assert.Equal(120, pdf.Pdf.CompressionDpi);
        Assert.Equal(60, pdf.Pdf.CompressionQuality);
        var video = DilimService.CompressionPreset(FormatCategory.Video, request);
        Assert.Equal("mp4", video.TargetFormat);
        Assert.Equal(VideoCodec.Hevc, video.Media.VideoCodec);
        Assert.Equal(24, video.Media.Crf);
        Assert.Equal(720, video.Media.MaxHeight);
        Assert.Equal(24, video.Media.FrameRate);
        Assert.Equal(96, video.Media.AudioBitrateKbps);
        Assert.Equal(96, DilimService.CompressionPreset(FormatCategory.Audio, request).Media.AudioBitrateKbps);
        Assert.Throws<ArgumentException>(() => DilimService.CompressionPreset(FormatCategory.Video, request with { VideoFrameRate = double.NaN }));
    }
}
