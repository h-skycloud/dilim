using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dilim.Core.Localization;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>Runs a tool of the engine (ToolEngine) for the window: the same path the old form used.</summary>
public delegate Task<IReadOnlyList<string>> ToolRun(string id, IReadOnlyList<string> files, ToolRequest request, Window owner,
    IProgress<double> progress, CancellationToken ct);

/// <summary>A tool window that edits media through the engine: the run delegate and the shared video preview.</summary>
public abstract class MediaTool : ToolShell
{
    protected readonly string Id;
    protected readonly IReadOnlyList<string> Files;
    private readonly ToolRun _run;

    protected MediaTool(string id, IReadOnlyList<string> files, ILocalizer loc, ToolRun run, double width, double height)
        : base("tool." + id, loc, width, height)
    {
        Id = id;
        Files = files;
        _run = run;
    }

    protected VideoPreview NewPreview(double height, bool scrubber) =>
        new(Files[0], height, scrubber, Geometry("Icon.StepBack"), Geometry("Icon.Play"), Geometry("Icon.Pause"), Geometry("Icon.StepForward"));

    /// <summary>The engine request this window builds from its controls.</summary>
    protected abstract ToolRequest Request();

    /// <summary>Which files the tool works on: all of them, or the one previewed.</summary>
    protected virtual IReadOnlyList<string> Targets => [Files[0]];

    protected override Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct) =>
        _run(Id, Targets, Request(), this, progress, ct);

    protected static string Seconds(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>"Change video speed": stops from 0.25× to 4×, previewed at that speed. Sound keeps its pitch.</summary>
public sealed class VideoSpeedTool : MediaTool
{
    private static readonly double[] Stops = [0.25, 0.5, 1, 1.5, 2, 4];
    private readonly VideoPreview _preview;
    private readonly Slider _slider = new() { Minimum = 0, Maximum = Stops.Length - 1, TickFrequency = 1, IsSnapToTickEnabled = true, Value = 2 };
    private readonly TextBlock _value = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly Grid _names = new();

    public VideoSpeedTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("changeVideoSpeed", files, loc, run, 640, 900)
    {
        _preview = NewPreview(440, scrubber: true);
        _slider.Classes.Add("level");
        _slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Update(); };
        for (var i = 0; i < Stops.Length; i++)
        {
            _names.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var index = i;
            var name = new Button { Content = Stops[i].ToString("0.##", CultureInfo.InvariantCulture) + "×", HorizontalAlignment = HorizontalAlignment.Center };
            name.Classes.Add("level-name");
            name.Click += (_, _) => _slider.Value = index;
            Grid.SetColumn(name, i);
            _names.Children.Add(name);
        }
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 18, 0, 6) };
        head.Children.Add(new TextBlock { Text = loc["tool.speed.label"], VerticalAlignment = VerticalAlignment.Center }.Also(t => t.Classes.Add("muted")));
        Grid.SetColumn(_value, 1); head.Children.Add(_value);
        Body = new StackPanel { Children = { _preview, head, _slider, _names, Note(loc[files.Count > 1 ? "tool.speed.note_many" : "tool.speed.note"]) } };
        PrimaryText = loc["tool.crop.apply"];
        Update();
    }

    private double Speed => Stops[(int)Math.Round(_slider.Value)];

    private void Update()
    {
        _value.Text = Speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
        _preview.PlaybackRate = Speed;
        for (var i = 0; i < _names.Children.Count; i++) _names.Children[i].Classes.Set("on", i == (int)Math.Round(_slider.Value));
    }

    protected override IReadOnlyList<string> Targets => Files;
    protected override ToolRequest Request() => new() { Speed = Speed };
}

/// <summary>"Save video frames": step to exact frames, add each, save them as full-size PNG pictures.</summary>
public sealed class VideoFramesTool : MediaTool
{
    private readonly VideoPreview _preview;
    private readonly List<double> _times = [];
    private readonly WrapPanel _chosen = new();
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };

    public VideoFramesTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("videoSnapshots", files, loc, run, 640, 980)
    {
        _preview = NewPreview(460, scrubber: true);
        _count.Classes.Add("muted");
        var add = new Button { Content = loc["tool.frames.add"] };
        add.Click += async (_, _) => await AddAsync();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 10) };
        head.Children.Add(_count);
        Grid.SetColumn(add, 1); head.Children.Add(add);
        Body = new StackPanel { Children = { _preview, Rule(), head, _chosen } };
        Update();
    }

    private async Task AddAsync()
    {
        var time = Math.Round(_preview.Time * _preview.FrameRate) / _preview.FrameRate;
        if (_times.Any(t => Math.Abs(t - time) < 0.5 / _preview.FrameRate)) return;
        _times.Add(time);
        var png = await ToolPreviews.VideoFrameAsync(Files[0], time, 200);
        var picture = new Border { Width = 150, Height = 92, CornerRadius = new CornerRadius(12), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 10) };
        picture.Classes.Add("order-picture");
        var image = new Image { Stretch = Stretch.UniformToFill };
        if (png is not null) { using var s = new MemoryStream(png); image.Source = new Bitmap(s); }
        var label = new Border { Child = new TextBlock { Text = VideoPreview.Clock(time), Foreground = Brushes.White, FontWeight = FontWeight.SemiBold },
            Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8) };
        var remove = new Button { Content = new PathIcon { Data = Geometry("Icon.Close"), Width = 10, Height = 10 }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
        remove.Classes.Add("page-action");
        picture.Child = new Panel { Children = { image, label, remove } };
        picture.PointerPressed += (_, _) => _preview.Seek(time);
        remove.Click += (_, e) => { e.Handled = true; _times.Remove(time); _chosen.Children.Remove(picture); Update(); };
        _chosen.Children.Add(picture);
        Update();
    }

    private void Update()
    {
        _count.Text = Loc.Format("tool.frames.count", _times.Count);
        PrimaryText = Loc.Format(_times.Count == 1 ? "tool.frames.save_one" : "tool.frames.save", _times.Count);
        PrimaryEnabled = _times.Count > 0;
    }

    protected override ToolRequest Request() => new() { Times = [.. _times] };
}

/// <summary>"Crop video": a box on the frame with ratios and exact size; the sound is kept as it is.</summary>
public sealed class CropVideoTool : MediaTool
{
    private static readonly (string Key, double? Ratio)[] Ratios =
        [("tool.crop.free", null), ("1:1", 1), ("16:9", 16.0 / 9), ("9:16", 9.0 / 16), ("4:3", 4.0 / 3), ("3:4", 3.0 / 4)];

    private readonly VideoPreview _preview;
    private readonly Segmented _ratio;
    private readonly TextBox _width = new() { Width = 90 }, _height = new() { Width = 90 };

    public CropVideoTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("cropVideo", files, loc, run, 640, 940)
    {
        _preview = NewPreview(460, scrubber: true);
        var canvas = _preview.Canvas;
        canvas.CropMode = true;
        _preview.Loaded += () =>
        {
            canvas.Boxes.Add(new CanvasBox { Area = canvas.Centered(null) });
            canvas.Select(canvas.Boxes[0]);
            Sync();
        };
        canvas.Changed += Sync;
        _ratio = new Segmented(Ratios.Select(r => r.Key.StartsWith("tool.", StringComparison.Ordinal) ? loc[r.Key] : r.Key).ToArray());
        _ratio.Changed += i =>
        {
            canvas.Ratio = Ratios[i].Ratio;
            if (canvas.Boxes.FirstOrDefault() is { } box) box.Area = canvas.Clamp(canvas.Centered(Ratios[i].Ratio));
            Sync();
        };
        foreach (var box in new[] { _width, _height })
        {
            box.LostFocus += (_, _) => TypeSize();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { TypeSize(); e.Handled = true; } };
        }
        var size = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Muted("W"), _width, Muted("H"), _height, Muted("px") } };
        Body = new StackPanel { Children = { _preview, Row(loc["tool.crop.ratio"], _ratio, 18), Row(loc["tool.crop.size"], size), Note(loc["tool.cropvideo.note"]) } };
        PrimaryText = loc["tool.crop.apply"];
        SetSecondary(loc["tool.pdfshrink.reset"], () => { _ratio.Selected = 0; if (canvas.Boxes.FirstOrDefault() is { } b) b.Area = canvas.Centered(null); Sync(); });
    }

    private static TextBlock Muted(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }.Also(t => t.Classes.Add("muted"));

    private void Sync()
    {
        if (_preview.Canvas.Boxes.FirstOrDefault() is not { } box) return;
        _width.Text = ((int)box.Area.Width).ToString(CultureInfo.InvariantCulture);
        _height.Text = ((int)box.Area.Height).ToString(CultureInfo.InvariantCulture);
        _preview.Canvas.Refresh();
    }

    private void TypeSize()
    {
        var canvas = _preview.Canvas;
        if (canvas.Boxes.FirstOrDefault() is not { } box) return;
        if (!int.TryParse(_width.Text, out var w) || !int.TryParse(_height.Text, out var h)) { Sync(); return; }
        if (canvas.Ratio is { } ratio) { if (Math.Abs(w - box.Area.Width) >= 1) h = (int)Math.Round(w / ratio); else w = (int)Math.Round(h * ratio); }
        var c = box.Area.Center;
        box.Area = canvas.Clamp(new Rect(c.X - w / 2.0, c.Y - h / 2.0, w, h));
        Sync();
    }

    protected override ToolRequest Request()
    {
        var area = _preview.Canvas.Boxes.FirstOrDefault()?.Area ?? new Rect(0, 0, _preview.Info?.Width ?? 2, _preview.Info?.Height ?? 2);
        return new() { X = (int)area.X, Y = (int)area.Y, Width = Math.Max(2, (int)area.Width), Height = Math.Max(2, (int)area.Height) };
    }
}

/// <summary>Shared by Trim video and Split video: the preview with a film strip under it.</summary>
public abstract class StripVideoTool : MediaTool
{
    protected readonly VideoPreview Preview;
    protected readonly Filmstrip Strip = new();

    protected StripVideoTool(string id, IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base(id, files, loc, run, 640, 900)
    {
        Preview = NewPreview(420, scrubber: false);
        Strip.Seeked += t => Preview.Seek(t);
        Preview.TimeChanged += t => { Strip.Playhead = t; Strip.InvalidateVisual(); };
        Preview.Loaded += async () =>
        {
            Strip.Duration = Preview.Duration;
            OnLoaded();
            Strip.InvalidateVisual();
            var frames = await ToolPreviews.FramesAsync(Files[0], Preview.Duration, 16, 160);
            Strip.SetFrames(frames.Select(b => { using var s = new MemoryStream(b); return new Bitmap(s); }).ToList());
        };
    }

    protected virtual void OnLoaded() { }

    /// <summary>The preview's transport row moved under the strip, as in the trim and split windows.</summary>
    protected Control Transport => Preview.Children[^1];
}

/// <summary>"Trim video": drag the ends of the strip or type exact times; keeps that section as a new file.</summary>
public sealed class TrimVideoTool : StripVideoTool
{
    private readonly TextBlock _selected = new();
    private readonly TextBox _start = new() { Width = 110 }, _end = new() { Width = 110 };

    public TrimVideoTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("trimVideo", files, loc, run)
    {
        Strip.RangeMode = true;
        Strip.Changed += Sync;
        Preview.PlayRange = () => (Strip.Start, Strip.End);
        foreach (var (box, isStart) in new[] { (_start, true), (_end, false) })
        {
            box.FontFeatures = FontFeatureCollection.Parse("tnum");
            box.LostFocus += (_, _) => Type(box, isStart);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Type(box, isStart); e.Handled = true; } };
        }
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 8) };
        head.Children.Add(new TextBlock { Text = loc["tool.trim.hint"] }.Also(t => t.Classes.Add("muted")));
        _selected.Classes.Add("muted");
        Grid.SetColumn(_selected, 1); head.Children.Add(_selected);
        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        times.Children.Add(new TextBlock { Text = loc["tool.trim.start"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }.Also(t => t.Classes.Add("muted")));
        Grid.SetColumn(_start, 1); times.Children.Add(_start);
        var endLabel = new TextBlock { Text = loc["tool.trim.end"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        endLabel.Classes.Add("muted");
        Grid.SetColumn(endLabel, 3); times.Children.Add(endLabel);
        Grid.SetColumn(_end, 4); times.Children.Add(_end);
        var transport = Transport;
        Preview.Children.Remove(transport);
        Body = new StackPanel { Children = { Preview, head, Strip, times, transport.Also(t => t.Margin = new Thickness(0, 10, 0, 0)) } };
        PrimaryText = loc["tool.crop.apply"];
    }

    protected override void OnLoaded()
    {
        Strip.Start = 0;
        Strip.End = Preview.Duration;
        Sync();
    }

    private void Sync()
    {
        _selected.Text = Loc.Format("tool.trim.selected", VideoPreview.Clock(Strip.End - Strip.Start));
        _start.Text = VideoPreview.Clock(Strip.Start);
        _end.Text = VideoPreview.Clock(Strip.End);
        PrimaryEnabled = Strip.End - Strip.Start > 0.04;
    }

    private void Type(TextBox box, bool isStart)
    {
        if (VideoPreview.ParseClock(box.Text) is { } t)
        {
            if (isStart) Strip.Start = Math.Clamp(t, 0, Strip.End - 0.05);
            else Strip.End = Math.Clamp(t, Strip.Start + 0.05, Preview.Duration);
            Preview.Seek(isStart ? Strip.Start : Strip.End);
            Strip.InvalidateVisual();
        }
        Sync();
    }

    protected override ToolRequest Request() => new() { Start = Strip.Start, End = Strip.End };
}

/// <summary>"Split video": cut at chosen points or into equal parts; every part lands in one folder.</summary>
public sealed class SplitVideoTool : StripVideoTool
{
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly NumericUpDown _parts = new() { Minimum = 2, Maximum = 50, Value = 2, Width = 120, FormatString = "0" };

    public SplitVideoTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("splitVideo", files, loc, run)
    {
        Strip.Changed += Update;
        _count.Classes.Add("muted");
        var add = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new PathIcon { Data = Geometry("Icon.Scissors"), Width = 14, Height = 14 }, new TextBlock { Text = loc["tool.split.add"] } } } };
        add.Click += (_, _) => { var t = Preview.Time; if (t > 0.05 && t < Preview.Duration - 0.05 && !Strip.Points.Any(p => Math.Abs(p - t) < 0.05)) { Strip.Points.Add(t); Strip.Points.Sort(); Strip.InvalidateVisual(); Update(); } };
        var remove = new Button { Content = loc["tool.split.clear"] };
        remove.Click += (_, _) => { Strip.Points.Clear(); Strip.InvalidateVisual(); Update(); };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 8) };
        head.Children.Add(_count);
        Grid.SetColumn(remove, 1); remove.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(remove);
        Grid.SetColumn(add, 2); head.Children.Add(add);
        var set = new Button { Content = loc["tool.split.set"] };
        set.Click += (_, _) =>
        {
            var n = (int)(_parts.Value ?? 2);
            Strip.Points.Clear();
            for (var i = 1; i < n; i++) Strip.Points.Add(Preview.Duration * i / n);
            Strip.InvalidateVisual();
            Update();
        };
        var equal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _parts, set } };
        var transport = Transport;
        Preview.Children.Remove(transport);
        Body = new StackPanel { Children = { Preview, head, Strip, transport.Also(t => t.Margin = new Thickness(0, 10, 0, 0)), Rule(), Row(loc["tool.split.equal"], equal), Note(loc["tool.split.note"]) } };
        PrimaryText = loc["tool.split.run"];
        Update();
    }

    private void Update()
    {
        _count.Text = Loc.Format("tool.split.count", Strip.Points.Count);
        PrimaryEnabled = Strip.Points.Count > 0;
        Strip.InvalidateVisual();
    }

    protected override ToolRequest Request() => new() { Times = [.. Strip.Points.Order()] };
}
