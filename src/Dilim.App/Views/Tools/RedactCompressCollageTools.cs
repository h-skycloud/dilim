using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dilim.App.Services;
using Dilim.Core.Formats;
using Dilim.Core.Localization;
using Dilim.Core.Profiles;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>Colour swatches for a redaction fill.</summary>
public sealed class Swatches : StackPanel
{
    public static readonly string[] Colors = ["#000000", "#FFFFFF", "#6B6B6B", "#F35B28"];
    private readonly List<Button> _buttons = [];

    public string Selected { get; private set; } = Colors[0];
    public event Action<string>? Changed;

    public Swatches()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        foreach (var color in Colors)
        {
            var button = new Button { Background = new SolidColorBrush(Color.Parse(color)), Tag = color };
            button.Classes.Add("swatch");
            button.Click += (_, _) => Select(color);
            _buttons.Add(button);
            Children.Add(button);
        }
        Select(Colors[0], quiet: true);
    }

    public void Select(string color, bool quiet = false)
    {
        Selected = color;
        foreach (var b in _buttons) b.Classes.Set("on", (string)b.Tag! == color);
        if (!quiet) Changed?.Invoke(color);
    }
}

/// <summary>"Redact photo": several boxes drawn on the picture, filled with a colour or pixelated.</summary>
public class RedactPhotoTool : MediaTool
{
    protected readonly BoxCanvas Canvas = new() { CanDraw = true, Height = 460 };
    protected readonly Segmented Style;
    protected readonly Swatches Colors = new();
    protected readonly StackPanel Extra = new();

    public RedactPhotoTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("redactImage", files, loc, run, 760, 820)
    {
        Style = new Segmented([loc["tool.redact.solid"], loc["tool.redact.blur"]]);
        Style.Changed += i => { Canvas.Blur = i == 1; Colors.IsEnabled = i == 0; Canvas.Refresh(); };
        Colors.Changed += c => { Canvas.Fill = Color.Parse(c); Canvas.Refresh(); };
        Canvas.Changed += Update;
        // Faces and text found on this computer by Windows: each becomes a box to check and adjust.
        var detect = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var faces in new[] { true, false })
        {
            var button = new Button { Content = loc[faces ? "tool.redact.faces" : "tool.redact.text"], IsEnabled = Detection.Available };
            if (!Detection.Available) ToolTip.SetTip(button, loc["tool.redact.detect_unavailable"]);
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                Status = loc["tool.redact.detecting"];
                try
                {
                    var found = await Detection.FindAsync(Files[0], faces);
                    foreach (var area in found) Canvas.Boxes.Add(new CanvasBox { Area = Canvas.Clamp(area) });
                    Status = loc.Format(found.Count == 0 ? "tool.redact.found_none" : "tool.redact.found", found.Count);
                    Canvas.Refresh();
                    Update();
                }
                catch (Exception ex) { Status = Message(ex); }
                finally { button.IsEnabled = true; }
            };
            detect.Children.Add(button);
        }
        Extra.Children.Add(Row(loc["tool.redact.detect"], detect));
        Body = new StackPanel { Children = { Framed(Canvas), Row(loc["tool.redact.style"], Style, 18), Row(loc["tool.redact.color"], Colors), Extra, Note(loc["tool.redact.note"]) } };
        PrimaryText = loc["tool.redact.run"];
        SetSecondary(loc["tool.pdfshrink.reset"], () => { Canvas.Boxes.Clear(); Canvas.Select(null); Update(); });
        Update();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var (bitmap, width, height) = await CropService.LoadPreviewAsync(Files[0]);
            var blurred = await ToolPreviews.BlurredImageAsync(Files[0], 1600);
            Bitmap? blur = null;
            if (blurred is not null) { using var s = new MemoryStream(blurred); blur = new Bitmap(s); }
            Canvas.SetPicture(bitmap, new PixelSize(width, height), blur);
        }
        catch (Exception) { Status = Loc["error.unsupported_source"]; }
    }

    protected virtual void Update() => PrimaryEnabled = Canvas.Boxes.Count > 0;

    protected override ToolRequest Request() => new()
    {
        Boxes = [.. Canvas.Boxes.SelectMany(b => new[] { (int)b.Area.X, (int)b.Area.Y, (int)b.Area.Width, (int)b.Area.Height })],
        Blur = Style.Selected == 1,
        RedactColor = Colors.Selected,
    };
}

/// <summary>One redaction's time range: drag the bar or its ends; a click selects its box.</summary>
public sealed class RangeBar : Control
{
    private enum Drag { None, Move, Start, End }
    private Drag _drag;
    private double _grab, _origin;

    public CanvasBox Box { get; }
    public double Duration { get; set; }
    public double Playhead { get; set; }
    public bool IsSelected { get; set; }
    public string Label { get; set; } = "";
    public event Action? Changed;
    public event Action? Picked;

    public RangeBar(CanvasBox box)
    {
        Box = box;
        Height = 44;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    private double X(double t) => Duration > 0 ? Math.Clamp(t, 0, Duration) / Duration * Bounds.Width : 0;
    private double T(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Duration;

    public override void Render(DrawingContext context)
    {
        IBrush Resource(string key, IBrush fallback) => this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;
        context.DrawRectangle(Resource("SurfaceAltBrush", Brushes.WhiteSmoke), null, new RoundedRect(new Rect(Bounds.Size), 10));
        var r = new Rect(X(Box.Start), 0, Math.Max(8, X(Box.End) - X(Box.Start)), Bounds.Height);
        context.DrawRectangle(IsSelected ? Resource("AccentBrush", Brushes.OrangeRed) : new SolidColorBrush(Color.FromArgb(150, 243, 91, 40)), null, r, 10, 10);
        context.FillRectangle(Brushes.White, new Rect(r.X + 7, r.Center.Y - 10, 2.5, 20));
        context.FillRectangle(Brushes.White, new Rect(r.Right - 9.5, r.Center.Y - 10, 2.5, 20));
        var text = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);
        if (text.Width < r.Width - 28) context.DrawText(text, new Point(r.Center.X - text.Width / 2, r.Center.Y - text.Height / 2));
        context.FillRectangle(Resource("TextPrimaryBrush", Brushes.Black), new Rect(X(Playhead) - 1, 0, 2, Bounds.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var x = e.GetPosition(this).X;
        _drag = Math.Abs(x - X(Box.Start)) <= 10 ? Drag.Start : Math.Abs(x - X(Box.End)) <= 10 ? Drag.End : Drag.Move;
        _grab = T(x);
        _origin = Box.Start;
        e.Pointer.Capture(this);
        Picked?.Invoke();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == Drag.None) return;
        var t = T(e.GetPosition(this).X);
        switch (_drag)
        {
            case Drag.Start: Box.Start = Math.Min(t, Box.End - 0.1); break;
            case Drag.End: Box.End = Math.Max(t, Box.Start + 0.1); break;
            default:
                var length = Box.End - Box.Start;
                Box.Start = Math.Clamp(_origin + t - _grab, 0, Duration - length);
                Box.End = Box.Start + length;
                break;
        }
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }
}

/// <summary>"Redact video": boxes on the video, each with its own time range; solid colour or pixelated.</summary>
public sealed class RedactVideoTool : MediaTool
{
    private readonly VideoPreview _preview;
    private readonly Segmented _style;
    private readonly Swatches _colors = new();
    private readonly StackPanel _lanes = new() { Spacing = 8 };
    private readonly Filmstrip _strip = new() { Height = 56 };

    public RedactVideoTool(IReadOnlyList<string> files, ILocalizer loc, ToolRun run) : base("redactVideo", files, loc, run, 760, 1040)
    {
        _preview = NewPreview(420, scrubber: false);
        var canvas = _preview.Canvas;
        canvas.CanDraw = true;
        canvas.IsActive = b => _preview.Time >= b.Start - 1e-6 && _preview.Time <= b.End + 1e-6;
        canvas.Changed += () =>
        {
            // A new box starts at the current time and lasts to the end; it gets its own lane.
            foreach (var box in canvas.Boxes.Where(b => b.End == double.MaxValue)) { box.Start = _preview.Time; box.End = _preview.Duration; }
            Rebuild();
        };
        _style = new Segmented([loc["tool.redact.solid"], loc["tool.redact.blur"]]);
        _style.Changed += i => { canvas.Blur = i == 1; _colors.IsEnabled = i == 0; canvas.Refresh(); };
        _colors.Changed += c => { canvas.Fill = Color.Parse(c); canvas.Refresh(); };
        _preview.TimeChanged += t =>
        {
            _strip.Playhead = t; _strip.InvalidateVisual();
            foreach (var lane in _lanes.Children.OfType<Grid>().Select(g => g.Children.OfType<RangeBar>().Single())) { lane.Playhead = t; lane.InvalidateVisual(); }
        };
        _strip.Seeked += t => _preview.Seek(t);
        _preview.Loaded += async () =>
        {
            _strip.Duration = _preview.Duration;
            var frames = await ToolPreviews.FramesAsync(Files[0], _preview.Duration, 14, 160);
            _strip.SetFrames(frames.Select(b => { using var s = new MemoryStream(b); return new Bitmap(s); }).ToList());
            // The blurred look for the preview, from a frame.
            var blurred = await ToolPreviews.BlurredImageAsync(await FrameFileAsync(), 1280);
            if (blurred is not null) { using var s = new MemoryStream(blurred); canvas.SetBlurred(new Bitmap(s)); }
        };
        var transport = _preview.Children[^1];
        _preview.Children.Remove(transport);
        var film = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), Margin = new Thickness(0, 4, 0, 0) };
        film.Children.Add(new PathIcon { Data = Geometry("Icon.Shapes"), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }.Also(p => p.Classes.Add("muted")));
        Grid.SetColumn(_strip, 1); film.Children.Add(_strip);
        Body = new StackPanel { Children =
        {
            _preview, Row(loc["tool.redact.style"], _style, 16), Row(loc["tool.redact.color"], _colors), Rule(),
            new TextBlock { Text = loc["tool.redact.redactions"], FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 10) },
            _lanes, film, transport.Also(t => t.Margin = new Thickness(0, 10, 0, 0)), Note(loc["tool.redactvideo.note"]),
        } };
        PrimaryText = loc["tool.redact.run"];
        Rebuild();
    }

    /// <summary>A frame of the video as a file, for the blurred preview picture.</summary>
    private async Task<string> FrameFileAsync()
    {
        var png = await ToolPreviews.VideoFrameAsync(Files[0], Math.Min(1, _preview.Duration / 2), 1280) ?? [];
        var path = Path.Combine(Path.GetTempPath(), "dilim-previews", $"frame-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, png);
        return path;
    }

    private void Rebuild()
    {
        var canvas = _preview.Canvas;
        _lanes.Children.Clear();
        for (var i = 0; i < canvas.Boxes.Count; i++)
        {
            var box = canvas.Boxes[i];
            var bar = new RangeBar(box) { Duration = _preview.Duration, Playhead = _preview.Time, Label = Loc.Format("tool.redact.box", i + 1), IsSelected = box == canvas.Selected };
            bar.Changed += () => canvas.Refresh();
            bar.Picked += () => { canvas.Select(box); if (_preview.Time < box.Start || _preview.Time > box.End) _preview.Seek(box.Start); Rebuild(); };
            var lane = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*") };
            lane.Children.Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center }.Also(t => t.Classes.Add("muted")));
            Grid.SetColumn(bar, 1); lane.Children.Add(bar);
            _lanes.Children.Add(lane);
        }
        if (canvas.Boxes.Count == 0)
            _lanes.Children.Add(new Border { Height = 44, CornerRadius = new CornerRadius(10), Child = new TextBlock { Text = Loc["tool.redactvideo.empty"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Also(t => t.Classes.Add("muted")) }.Also(b => b.Classes.Add("order-picture")));
        PrimaryEnabled = canvas.Boxes.Count > 0;
    }

    protected override ToolRequest Request()
    {
        var boxes = _preview.Canvas.Boxes;
        return new()
        {
            Boxes = [.. boxes.SelectMany(b => new[] { (int)b.Area.X, (int)b.Area.Y, (int)b.Area.Width, (int)b.Area.Height })],
            Ranges = [.. boxes.SelectMany(b => new[] { Math.Max(0, b.Start), Math.Min(b.End, _preview.Duration) })],
            Blur = _style.Selected == 1,
            RedactColor = _colors.Selected,
        };
    }
}

/// <summary>"Compress" for pictures, video and sound: Balanced or Strong, a smaller size, an optional target.</summary>
public sealed class CompressTool : ToolShell
{
    private readonly IReadOnlyList<string> _files;
    private readonly FormatCategory? _category;
    private readonly Segmented _preset;
    private readonly TextBlock _presetNote = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _size = new() { MinWidth = 220 };
    private readonly CheckBox _useTarget = new();
    private readonly Segmented _targetKind;
    private readonly TextBox _target = new() { Width = 100, Text = "5" };
    private readonly Control _targetRow;
    private readonly (string Key, int Value)[] _sizes;

    public CompressTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.compress", loc, 560, 600)
    {
        _files = files;
        _category = ProfileSelector.DetectFormats(files).FirstOrDefault()?.Category;
        var total = files.Sum(f => new FileInfo(f).Length);
        var name = new TextBlock { Text = files.Count == 1 ? Path.GetFileName(files[0]) : Loc.Format("tool.compress.files", files.Count), TextTrimming = TextTrimming.CharacterEllipsis };
        name.Classes.Add("muted");
        var size = new TextBlock { Text = Loc.Format("tool.compress.original", Size(total)), FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(0, 10, 0, 20) };
        _preset = new Segmented([loc["tool.compress.balanced"], loc["tool.compress.strong"]]);
        _preset.Changed += _ => UpdateNote();
        _presetNote.Classes.Add("muted");
        _presetNote.Margin = new Thickness(0, 10, 0, 0);
        _sizes = _category switch
        {
            FormatCategory.Video => [("tool.compress.keep_video", 0), ("1080p", 1080), ("720p", 720), ("480p", 480)],
            FormatCategory.Audio => [],
            _ => [("tool.compress.keep_image", 100), ("%75", 75), ("%50", 50), ("%25", 25)],
        };
        _size.ItemsSource = _sizes.Select(s => s.Key.StartsWith("tool.", StringComparison.Ordinal) ? loc[s.Key] : s.Key).ToArray();
        _size.SelectedIndex = 0;
        _useTarget.Content = loc["tool.compress.to_target"];
        _targetKind = new Segmented(["MB", "%"]);
        _targetKind.Changed += i => _target.Text = i == 0 ? "5" : "50";
        _targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(28, 10, 0, 0), Children = { _target, _targetKind } };
        _targetRow.IsVisible = false;
        _useTarget.IsCheckedChanged += (_, _) => _targetRow.IsVisible = _useTarget.IsChecked == true;
        var body = new StackPanel { Children = { name, size, Label(loc["tool.compress.preset"]), _preset.Also(p => p.HorizontalAlignment = HorizontalAlignment.Left), _presetNote } };
        if (_sizes.Length > 0) body.Children.Add(Row(loc[_category == FormatCategory.Video ? "tool.compress.video_size" : "tool.compress.image_size"], _size, 18));
        body.Children.Add(Rule());
        body.Children.Add(_useTarget.Also(c => c.Margin = new Thickness(0, 8, 0, 0)));
        body.Children.Add(_targetRow);
        body.Children.Add(Note(loc["tool.compress.note"]));
        Body = body;
        PrimaryText = loc["tool.compress.run"];
        UpdateNote();
    }

    private void UpdateNote() => _presetNote.Text = Loc[_preset.Selected == 0 ? "tool.compress.balanced_note" : "tool.compress.strong_note"];

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        long? bytes = null; double? share = null;
        if (_useTarget.IsChecked == true)
        {
            if (!double.TryParse((_target.Text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new ArgumentException("tool.error.number");
            if (_targetKind.Selected == 0) bytes = (long)(value * 1024 * 1024); else share = Math.Clamp(value, 1, 99) / 100;
        }
        var picked = _sizes.Length > 0 ? _sizes[Math.Max(0, _size.SelectedIndex)].Value : 0;
        var choice = new CompressChoice
        {
            Strong = _preset.Selected == 1,
            ResizePercent = _category == FormatCategory.Video ? 100 : picked is 0 ? 100 : picked,
            MaxHeight = _category == FormatCategory.Video ? picked : 0,
            TargetBytes = bytes,
            TargetShare = share,
        };
        var outputs = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            var folder = Path.Combine(Path.GetDirectoryName(_files[i])!, "Dilim");
            outputs.Add(await MediaCompressor.CompressAsync(_files[i], choice, folder, new Progress<double>(p => progress.Report((i + p) / _files.Count)), ct));
        }
        var before = _files.Sum(f => new FileInfo(f).Length);
        var after = outputs.Sum(f => new FileInfo(f).Length);
        Dispatch(() => Status = Loc.Format("tool.compress.result", Size(before), Size(after), Math.Round((1 - (double)after / before) * 100)));
        return outputs;
    }

    private static void Dispatch(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action, Avalonia.Threading.DispatcherPriority.Background);

    private static string Size(long bytes) => bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB" : Math.Max(1, bytes / 1024) + " KB";
}

/// <summary>"Create a collage": layout, columns, canvas, width, fit, spacing, corners, background and order, previewed.</summary>
public sealed class CollageTool : ToolShell
{
    private static readonly (string Key, double Ratio)[] Canvases =
        [("tool.collage.square", 1), ("tool.collage.landscape43", 4.0 / 3), ("tool.collage.landscape169", 16.0 / 9), ("tool.collage.portrait34", 3.0 / 4), ("tool.collage.portrait916", 9.0 / 16)];
    private static readonly int[] Widths = [1080, 1600, 2048, 3000, 4096];
    private static readonly string[] Backgrounds = ["#FFFFFF", "#000000", "#F4F1EA", "transparent"];

    private readonly OrderList _order;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly ComboBox _layout = new(), _canvas = new(), _width = new(), _fit = new(), _background = new();
    private readonly NumericUpDown _columns = new() { Minimum = 1, Maximum = 8, Value = 2, Width = 120, FormatString = "0" };
    private readonly Slider _spacing = new() { Minimum = 0, Maximum = 6, Value = 1, Width = 220 }, _corners = new() { Minimum = 0, Maximum = 6, Value = 0, Width = 220 };
    private readonly Control _columnsRow;
    private List<ImageMagick.IMagickImage<byte>>? _thumbs;
    private int _request;

    public CollageTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.createCollage", loc, 640, 980)
    {
        _order = new OrderList(files, (path, ct) => ToolPreviews.ImageAsync(path, 160, ct), Geometry("Icon.FileOutline"), loc["tool.order.up"], loc["tool.order.down"]);
        _order.Changed += () => { _thumbs = null; Render(); };
        void Fill(ComboBox box, IEnumerable<string> items, int selected) { box.ItemsSource = items.ToArray(); box.SelectedIndex = selected; box.MinWidth = 200; box.SelectionChanged += (_, _) => Render(); }
        Fill(_layout, new[] { "grid", "row", "column", "featured" }.Select(k => loc["tool.collage." + k]), 0);
        Fill(_canvas, Canvases.Select(c => loc[c.Key]), 0);
        Fill(_width, Widths.Select(w => w.ToString("N0", CultureInfo.CurrentCulture) + " px"), 2);
        Fill(_fit, new[] { loc["tool.collage.fill"], loc["tool.collage.fit"] }, 0);
        Fill(_background, new[] { "white", "black", "paper", "transparent" }.Select(k => loc["tool.collage.bg_" + k]), 0);
        _columns.ValueChanged += (_, _) => Render();
        foreach (var slider in new[] { _spacing, _corners }) slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Render(); };
        _layout.SelectionChanged += (_, _) => _columnsRow!.IsVisible = _layout.SelectedIndex == 0;
        _columnsRow = Row(loc["tool.collage.columns"], _columns);
        var stage = new Border { Height = 380, Child = _preview };
        stage.Classes.Add("order-picture");
        var spacing = new Expander { Header = loc["tool.collage.spacing_title"], HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 16, 0, 0),
            Content = new StackPanel { Children = { Row(loc["tool.collage.spacing"], _spacing, 4), Row(loc["tool.collage.corners"], _corners), Row(loc["tool.collage.background"], _background) } } };
        var order = new Expander { Header = loc["tool.collage.order"], HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0), Content = _order };
        var reset = new Button { Content = loc["tool.collage.reset"], HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        reset.Click += (_, _) => { _layout.SelectedIndex = 0; _canvas.SelectedIndex = 0; _width.SelectedIndex = 2; _fit.SelectedIndex = 0; _columns.Value = 2; _spacing.Value = 1; _corners.Value = 0; _background.SelectedIndex = 0; };
        Body = new StackPanel { Children =
        {
            Framed(stage), Row(loc["tool.collage.style"], _layout, 18), _columnsRow, Row(loc["tool.collage.canvas"], _canvas), Row(loc["tool.collage.width"], _width),
            Row(loc["tool.collage.image_fit"], _fit), Rule(), spacing, order, reset,
        } };
        PrimaryText = loc["tool.collage.run"];
        PrimaryEnabled = files.Count >= 2;
        if (files.Count < 2) Status = loc["tool.order.need_two"];
        Render();
    }

    private CollageOptions Options() => new()
    {
        Layout = (CollageLayout)Math.Max(0, _layout.SelectedIndex),
        Columns = (int)(_columns.Value ?? 2),
        Ratio = Canvases[Math.Max(0, _canvas.SelectedIndex)].Ratio,
        Width = Widths[Math.Max(0, _width.SelectedIndex)],
        Fill = _fit.SelectedIndex == 0,
        Spacing = _spacing.Value / 100,
        Corners = _corners.Value / 100,
        Background = Backgrounds[Math.Max(0, _background.SelectedIndex)],
    };

    private async void Render()
    {
        var request = ++_request;
        var options = Options();
        var files = _order.Paths.ToArray();
        try
        {
            _thumbs ??= await Task.Run(() => CollageMaker.Load(files, 700));
            var thumbs = _thumbs;
            var png = await Task.Run(() => { using var c = CollageMaker.Render(thumbs, options, 900); return c.ToByteArray(ImageMagick.MagickFormat.Png); });
            if (request != _request) return;
            using var s = new MemoryStream(png);
            _preview.Source = new Bitmap(s);
        }
        catch (Exception ex) { Status = Message(ex); }
    }

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var options = Options();
        var transparent = options.Background == "transparent" || options.Corners > 0 && options.Background == "transparent";
        var output = OutputNextTo(_order.Paths[0], transparent ? ".png" : ".jpg");
        output = Path.Combine(Path.GetDirectoryName(output)!, Loc["tool.collage.file"] + Path.GetExtension(output));
        for (var n = 2; File.Exists(output); n++) output = Path.Combine(Path.GetDirectoryName(output)!, $"{Loc["tool.collage.file"]} ({n}){Path.GetExtension(output)}");
        await CollageMaker.SaveAsync(_order.Paths, options, output, ct);
        return [output];
    }
}
