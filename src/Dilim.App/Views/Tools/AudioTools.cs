using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dilim.Core.Localization;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>The audio tools' shared parts: the recording's length and peaks, previews, and small builders.</summary>
public abstract class AudioTool : MediaTool
{
    protected readonly AudioPlayback Playback = new();
    protected double Duration { get; private set; }
    protected float[] Left { get; private set; } = [];
    protected float[] Right { get; private set; } = [];

    protected AudioTool(string id, IReadOnlyList<string> files, ILocalizer loc, ToolRun run, double width, double height)
        : base(id, files, loc, run, width, height)
    {
        Closing += (_, _) => Playback.Stop();
    }

    /// <summary>Reads the length and the waveform, then <see cref="OnLoaded"/>.</summary>
    protected async Task LoadAsync()
    {
        try
        {
            var info = await ToolEngine.ProbeAsync(Files[0], CancellationToken.None);
            Duration = info.Duration;
            (Left, Right) = await AudioPreview.WaveformAsync(Files[0], 1200);
            OnLoaded();
        }
        catch (Exception ex) { Status = Message(ex); PrimaryEnabled = false; }
    }

    protected abstract void OnLoaded();

    protected float[] Peaks => Left.Zip(Right, Math.Max).ToArray();

    /// <summary>A "▶ text" button; while it plays it shows "❚❚ text".</summary>
    protected Button PlayButton(string text, out object playing)
    {
        var button = new Button { Content = Labelled("Icon.Play", text), IsEnabled = AudioPlayback.Available };
        playing = Labelled("Icon.Pause", text);
        if (!AudioPlayback.Available) ToolTip.SetTip(button, Loc["tool.audio.no_player"]);
        return button;
    }

    protected Button IconButton(string icon, out object playing)
    {
        var button = new Button { Content = new PathIcon { Data = Geometry(icon), Width = 14, Height = 14 }, IsEnabled = AudioPlayback.Available };
        button.Classes.Add("transport");
        playing = new PathIcon { Data = Geometry("Icon.Pause"), Width = 14, Height = 14 };
        return button;
    }

    private Control Labelled(string icon, string text) => text.Length == 0
        ? new PathIcon { Data = Geometry(icon), Width = 14, Height = 14 }
        : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new PathIcon { Data = Geometry(icon), Width = 12, Height = 12 }, new TextBlock { Text = text } } };

    protected static TextBlock Mono(string text = "") => new TextBlock { Text = text, FontFeatures = FontFeatureCollection.Parse("tnum") }.Also(t => t.Classes.Add("muted"));

    /// <summary>A labelled slider with its value on the right ("Target loudness   −16 LUFS").</summary>
    protected static (Control View, Slider Slider) LabelledSlider(string label, double min, double max, double step, double value, Func<double, string> format)
    {
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Value = value };
        var shown = new TextBlock { FontWeight = FontWeight.SemiBold, Text = format(value) };
        slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) shown.Text = format(slider.Value); };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16, 0, 0) };
        head.Children.Add(new TextBlock { Text = label }.Also(t => t.Classes.Add("muted")));
        Grid.SetColumn(shown, 1); head.Children.Add(shown);
        return (new StackPanel { Children = { head, slider } }, slider);
    }

    protected static string Number(double value, string format = "0.#") => value.ToString(format, CultureInfo.CurrentCulture).Replace("-", "−");
}

/// <summary>"Trim audio": the waveform with handles, exact start and end, optionally silent ends removed.</summary>
public sealed class TrimAudioTool : AudioTool
{
    private readonly WaveformView _wave = new() { RangeMode = true };
    private readonly TextBlock _selected = new();
    private readonly TextBox _start = new() { Width = 110 }, _end = new() { Width = 110 };
    private readonly CheckBox _silence = new();

    public TrimAudioTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("trimAudio", files, loc, run, 640, 560)
    {
        _wave.Changed += Sync;
        Playback.Moved = t => { _wave.Playhead = t; _wave.InvalidateVisual(); };
        foreach (var (box, isStart) in new[] { (_start, true), (_end, false) })
        {
            box.FontFeatures = FontFeatureCollection.Parse("tnum");
            box.LostFocus += (_, _) => Type(box, isStart);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Type(box, isStart); e.Handled = true; } };
        }
        _silence.Content = loc["tool.trimaudio.silence"];
        var play = PlayButton(loc["tool.trimaudio.play"], out var playing);
        play.Click += (_, _) => Playback.Toggle(play, playing, Files[0], _wave.Start, _wave.End - _wave.Start,
            _silence.IsChecked == true ? AudioPreview.TrimSilenceFilter : null);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = loc["tool.trim.hint"] }.Also(t => t.Classes.Add("muted")));
        _selected.Classes.Add("muted");
        Grid.SetColumn(_selected, 1); head.Children.Add(_selected);
        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        times.Children.Add(Mono(loc["tool.trim.start"]).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 8, 0); }));
        Grid.SetColumn(_start, 1); times.Children.Add(_start);
        var endLabel = Mono(loc["tool.trim.end"]).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 8, 0); });
        Grid.SetColumn(endLabel, 3); times.Children.Add(endLabel);
        Grid.SetColumn(_end, 4); times.Children.Add(_end);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16, 0, 0) };
        bottom.Children.Add(_silence);
        Grid.SetColumn(play, 1); bottom.Children.Add(play);
        Body = new StackPanel { Children = { head, _wave, times, bottom } };
        PrimaryText = loc["tool.crop.apply"];
        _ = LoadAsync();
    }

    protected override void OnLoaded()
    {
        _wave.Peaks = Peaks;
        _wave.Duration = Duration;
        _wave.Start = 0;
        _wave.End = Duration;
        _wave.InvalidateVisual();
        Sync();
    }

    private void Sync()
    {
        _selected.Text = Loc.Format("tool.trim.selected", VideoPreview.Clock(_wave.End - _wave.Start));
        _start.Text = VideoPreview.Clock(_wave.Start);
        _end.Text = VideoPreview.Clock(_wave.End);
    }

    private void Type(TextBox box, bool isStart)
    {
        if (VideoPreview.ParseClock(box.Text) is { } t)
        {
            if (isStart) _wave.Start = Math.Clamp(t, 0, _wave.End - 0.05);
            else _wave.End = Math.Clamp(t, _wave.Start + 0.05, Duration);
            _wave.InvalidateVisual();
        }
        Sync();
    }

    protected override ToolRequest Request() => new() { Start = _wave.Start, End = _wave.End, TrimSilence = _silence.IsChecked == true };
}

/// <summary>"Convert audio channels": mono or stereo, each side's volume, either channel or the output previewed.</summary>
public sealed class AudioChannelsTool : AudioTool
{
    private readonly WaveformView _left = new() { Height = 76, Accent = false }, _right = new() { Height = 76, Accent = false };
    private readonly Segmented _output;
    private readonly Slider _leftVolume, _rightVolume;
    private readonly Control _leftView, _rightView;

    public AudioChannelsTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("audioChannels", files, loc, run, 640, 720)
    {
        _output = new Segmented([loc["tool.channels.mono"], loc["tool.channels.stereo"]], 1);
        (_leftView, _leftVolume) = LabelledSlider(loc["tool.channels.left"], 0, 200, 5, 100, v => $"%{v:0}");
        (_rightView, _rightVolume) = LabelledSlider(loc["tool.channels.right"], 0, 200, 5, 100, v => $"%{v:0}");
        Playback.Moved = t => { _left.Playhead = _right.Playhead = t; _left.InvalidateVisual(); _right.InvalidateVisual(); };
        Control Lane(string letter, WaveformView wave, bool left)
        {
            var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = new TextBlock { Text = letter, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold } };
            badge.Classes.Add("channel-badge");
            var play = IconButton("Icon.Play", out var playing);
            play.Click += (_, _) => Playback.Toggle(play, playing, Files[0], 0, null, AudioPreview.SoloFilter(left));
            ToolTip.SetTip(play, loc["tool.channels.solo"]);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
            grid.Children.Add(badge);
            Grid.SetColumn(wave, 1); wave.Margin = new Thickness(12, 0); grid.Children.Add(wave);
            Grid.SetColumn(play, 2); grid.Children.Add(play);
            return grid;
        }
        var preview = PlayButton(loc["tool.channels.play"], out var previewPlaying);
        preview.Click += (_, _) => Playback.Toggle(preview, previewPlaying, Files[0], 0, null,
            AudioPreview.ChannelsFilter(_output.Selected == 0 ? 1 : 2, _leftVolume.Value / 100, _rightVolume.Value / 100));
        Body = new StackPanel { Children =
        {
            Label(loc["tool.channels.preview"]), Lane("L", _left, true), Lane("R", _right, false), Rule(),
            Row(loc["tool.channels.output"], _output), _leftView, _rightView, Row(loc["tool.channels.preview_label"], preview, 18),
        } };
        PrimaryText = loc["tool.crop.apply"];
        _ = LoadAsync();
    }

    protected override void OnLoaded()
    {
        foreach (var (wave, peaks) in new[] { (_left, Left), (_right, Right) })
        {
            wave.Peaks = peaks;
            wave.Duration = Duration;
            wave.InvalidateVisual();
        }
    }

    protected override ToolRequest Request() => new() { Channels = _output.Selected == 0 ? 1 : 2, LeftVolume = _leftVolume.Value / 100, RightVolume = _rightVolume.Value / 100 };
}

/// <summary>"Normalize volume": target loudness, loudness range and true peak, with the input's levels measured.</summary>
public sealed class NormalizeTool : AudioTool
{
    private readonly WaveformView _wave = new() { Height = 200 };
    private readonly TextBlock _clock = Mono(), _levels = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Slider _target, _range, _peak;

    public NormalizeTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("normalizeAudio", files, loc, run, 640, 900)
    {
        Playback.Moved = t => { _wave.Playhead = t; _wave.InvalidateVisual(); _clock.Text = $"{VideoPreview.Clock(t)} / {VideoPreview.Clock(Duration)}"; };
        var (targetView, target) = LabelledSlider(loc["tool.normalize.target"], -24, -10, 1, -16, v => Number(v) + " LUFS");
        var (rangeView, range) = LabelledSlider(loc["tool.normalize.range"], 4, 20, 1, 11, v => Number(v) + " LU");
        var (peakView, peak) = LabelledSlider(loc["tool.normalize.peak"], -3, 0, 0.5, -1.5, v => Number(v) + " dBTP");
        (_target, _range, _peak) = (target, range, peak);
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
        name.Children.Add(new TextBlock { Text = Path.GetFileName(files[0]), TextTrimming = TextTrimming.CharacterEllipsis }.Also(t => t.Classes.Add("muted")));
        var length = Mono();
        Grid.SetColumn(length, 1); name.Children.Add(length);
        var play = IconButton("Icon.Play", out var playing);
        play.Click += (_, _) => Playback.Toggle(play, playing, Files[0], _wave.Playhead >= Duration - 0.05 ? 0 : _wave.Playhead, null);
        _wave.Seeked += t => _clock.Text = $"{VideoPreview.Clock(t)} / {VideoPreview.Clock(Duration)}";
        var transport = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        transport.Children.Add(play);
        Grid.SetColumn(_clock, 2); _clock.VerticalAlignment = VerticalAlignment.Center; transport.Children.Add(_clock);
        _levels.Classes.Add("caption");
        _levels.Margin = new Thickness(0, 14, 0, 0);
        Body = new StackPanel { Children = { name, _wave, transport, targetView, rangeView, peakView, Note(loc["tool.normalize.note"]), _levels } };
        PrimaryText = loc["tool.normalize.run"];
        _ = LoadAsync();
        _ = MeasureInputAsync(length);
    }

    private string _input = "";

    private async Task MeasureInputAsync(TextBlock length)
    {
        _levels.Text = Loc["tool.normalize.measuring"];
        try
        {
            var (lufs, range, peak) = await AudioPreview.MeasureAsync(Files[0]);
            _input = Loc.Format("tool.normalize.input", Number(lufs), Number(range), Number(peak));
            _levels.Text = _input;
        }
        catch (Exception) { _levels.Text = ""; }
        length.Text = VideoPreview.Clock(Duration);
    }

    protected override void OnLoaded()
    {
        _wave.Peaks = Peaks;
        _wave.Duration = Duration;
        _wave.InvalidateVisual();
        _clock.Text = $"{VideoPreview.Clock(0)} / {VideoPreview.Clock(Duration)}";
    }

    protected override ToolRequest Request() => new() { TargetLufs = _target.Value, LoudnessRange = _range.Value, TruePeak = _peak.Value };

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var outputs = await base.RunAsync(progress, ct);
        // Input and output side by side, so the change can be seen.
        if (outputs.Count > 0)
        {
            try
            {
                var (lufs, range, peak) = await AudioPreview.MeasureAsync(outputs[0], ct);
                _levels.Text = _input + Environment.NewLine + Loc.Format("tool.normalize.output", Number(lufs), Number(range), Number(peak));
            }
            catch (Exception) { }
        }
        return outputs;
    }
}

/// <summary>"Create an audio visualizer": an MP4 with a moving waveform or a still picture, landscape, portrait or square.</summary>
public sealed class VisualizerTool : AudioTool
{
    private static readonly string[] Shapes = ["16:9", "9:16", "1:1"];
    private readonly Segmented _style, _shape;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly Button _pick = new();
    private string _image = "";
    private int _request;

    public VisualizerTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("audioToVideo", files, loc, run, 640, 820)
    {
        _style = new Segmented([loc["tool.visual.waveform"], loc["tool.visual.image"]]);
        _shape = new Segmented([loc["tool.visual.landscape"], loc["tool.visual.portrait"], loc["tool.visual.square"]]);
        _style.Changed += _ => Update();
        _shape.Changed += _ => Update();
        _pick.Content = loc["tool.visual.pick"];
        _pick.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) { _image = path; Update(); }
        };
        var stage = new Border { Height = 400, Child = _preview, Background = Brushes.Black };
        Body = new StackPanel { Children =
        {
            Framed(stage), Row(loc["tool.visual.style"], _style, 18), Row(loc["tool.visual.shape"], _shape), Row(loc["tool.visual.picture"], _pick).Also(r => r.Bind(IsVisibleProperty, new Avalonia.Data.Binding { Source = _pick, Path = "IsVisible" })),
            Note(loc["tool.visual.note"]),
        } };
        PrimaryText = loc["tool.visual.run"];
        Update();
    }

    protected override void OnLoaded() { }

    private async void Update()
    {
        _pick.IsVisible = _style.Selected == 1;
        var request = ++_request;
        var (w, h) = Shapes[_shape.Selected] switch { "9:16" => (360, 640), "1:1" => (540, 540), _ => (640, 360) };
        try
        {
            string picture;
            if (_style.Selected == 1 && _image.Length > 0)
            {
                // The chosen picture fitted into the shape, as the video will show it.
                using var image = new ImageMagick.MagickImage(_image);
                image.AutoOrient();
                image.Resize(new ImageMagick.MagickGeometry((uint)w, (uint)h));
                image.BackgroundColor = ImageMagick.MagickColors.Black;
                image.Extent((uint)w, (uint)h, ImageMagick.Gravity.Center);
                picture = Path.Combine(Path.GetTempPath(), "dilim-previews", $"visual-{Guid.NewGuid():N}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(picture)!);
                image.Write(picture);
            }
            else picture = await ToolEngine.WaveformPictureAsync(Files[0], w, h, CancellationToken.None);
            if (request != _request) return;
            _preview.Source = new Bitmap(picture);
        }
        catch (Exception ex) { Status = Message(ex); }
    }

    protected override ToolRequest Request() => new()
    {
        VisualStyle = _style.Selected == 0 ? "waveform" : "image",
        Shape = Shapes[_shape.Selected],
        ImagePath = _image,
    };
}

/// <summary>"Bleep audio": sections on a bleep layer over the waveform; compare the original and the bleeped preview.</summary>
public sealed class BleepTool : AudioTool
{
    private readonly BleepLane _lane = new();
    private readonly WaveformView _wave = new() { Height = 130 };
    private readonly TextBlock _count = new() { FontWeight = FontWeight.SemiBold }, _from = Mono("0:00.00"), _to = Mono();

    public BleepTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("redactAudio", files, loc, run, 760, 640)
    {
        _lane.Label = loc["tool.bleep.label"];
        _lane.Changed += Update;
        Playback.Moved = t => { _wave.Playhead = t; _wave.InvalidateVisual(); };
        Control Lane(string label, Control content)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*"), Margin = new Thickness(0, 0, 0, 10) };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }.Also(t => t.Classes.Add("muted")));
            Grid.SetColumn(content, 1); grid.Children.Add(content);
            return grid;
        }
        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("80,Auto,*,Auto") };
        Grid.SetColumn(_from, 1); times.Children.Add(_from);
        Grid.SetColumn(_to, 3); times.Children.Add(_to);
        var original = PlayButton(loc["tool.bleep.original"], out var originalPlaying);
        original.Click += (_, _) => Playback.Toggle(original, originalPlaying, Files[0], 0, null);
        var bleeped = PlayButton(loc["tool.bleep.with"], out var bleepedPlaying);
        bleeped.Click += (_, _) => Playback.Toggle(bleeped, bleepedPlaying, Files[0], 0, null, _lane.Ranges.Count > 0 ? AudioPreview.BleepFilter(_lane.Ranges) : null);
        var compare = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { original, bleeped } };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { new TextBlock { Text = loc["tool.bleep.title"], FontWeight = FontWeight.SemiBold }, _count } };
        Body = new StackPanel { Children = { head, Lane(loc["tool.bleep.label"], _lane), Lane(loc["tool.bleep.audio"], _wave), times, Note(loc["tool.bleep.note"]), Rule(), Row(loc["tool.channels.preview_label"], compare) } };
        PrimaryText = loc["tool.bleep.run"];
        Update();
        _ = LoadAsync();
    }

    protected override void OnLoaded()
    {
        _wave.Peaks = Peaks;
        _wave.Duration = Duration;
        _lane.Duration = Duration;
        _to.Text = VideoPreview.Clock(Duration);
        _wave.InvalidateVisual();
        _lane.InvalidateVisual();
    }

    private void Update()
    {
        _count.Text = _lane.Ranges.Count.ToString(CultureInfo.InvariantCulture);
        PrimaryEnabled = _lane.Ranges.Count > 0;
    }

    protected override ToolRequest Request() => new() { Ranges = [.. _lane.Ranges.SelectMany(r => new[] { r.Start, r.End })] };
}
