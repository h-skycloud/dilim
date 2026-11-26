using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dilim.App.Services;
using Dilim.Core.Localization;
using Dilim.Engines.Tools;
using ImageMagick;
using FontWeight = Avalonia.Media.FontWeight;
using TextAlignment = Avalonia.Media.TextAlignment;
using FontStyle = Avalonia.Media.FontStyle;

namespace Dilim.App.Views.Tools;

// ───────────────────────── Metadata ─────────────────────────

/// <summary>"Edit metadata": every field grouped as the file stores it, searchable, editable or removable; "Remove All".</summary>
public sealed class MetadataTool : ToolShell
{
    private readonly IReadOnlyList<string> _files;
    private readonly TextBox _search = new() { Width = 260 };
    private readonly StackPanel _list = new();
    private readonly Dictionary<(string, string), string?> _changes = [];
    private IReadOnlyList<MetaField> _fields = [];
    private bool _removeAll;

    public MetadataTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.metadata.title", loc, 760, 820)
    {
        _files = files;
        _search.PlaceholderText = loc["tool.metadata.search"];
        _search.TextChanged += (_, _) => Rebuild();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        head.Children.Add(new TextBlock { Text = files.Count == 1 ? Path.GetFileName(files[0]) : Loc.Format("tool.metadata.many", Path.GetFileName(files[0]), files.Count - 1), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }.Also(t => t.Classes.Add("muted")));
        Grid.SetColumn(_search, 1); head.Children.Add(_search);
        Body = new StackPanel { Children = { head, _list, Note(loc["tool.metadata.note"]) } };
        PrimaryText = loc["common.save"];
        SetSecondary(loc["tool.metadata.remove_all"], () => { _removeAll = true; _ = StartAsync(); });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Status = Loc["tool.pdfshrink.calculating"];
        try
        {
            _fields = await MetadataEditor.ReadAsync(_files[0]);
            Status = "";
        }
        catch (Exception ex) { Status = Message(ex); }
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var query = (_search.Text ?? "").Trim();
        var shown = _fields.Where(f => query.Length == 0 || f.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || f.Value.Contains(query, StringComparison.CurrentCultureIgnoreCase) || f.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        foreach (var group in shown.GroupBy(f => f.Group))
        {
            var title = new TextBlock { Text = group.Key == MetadataEditor.FileGroup ? Loc["tool.metadata.file"] : group.Key, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, _list.Children.Count == 0 ? 0 : 18, 0, 8) };
            title.Classes.Add("muted");
            _list.Children.Add(title);
            foreach (var field in group) _list.Children.Add(Row(field));
            _list.Children.Add(Rule());
        }
        if (_list.Children.Count == 0 && _fields.Count > 0) _list.Children.Add(Note(Loc["tool.metadata.none_found"]));
    }

    private Control Row(MetaField field)
    {
        var key = (field.Group, field.Key);
        var removed = _changes.TryGetValue(key, out var change) && change is null;
        // Dilim's own fields (size, format, codec…) in the user's language; the file's own tag names as they are.
        var known = Loc["tool.metadata.f_" + field.Key];
        var label = !field.Key.Contains('|') && known != "tool.metadata.f_" + field.Key ? known : field.Name;
        var name = new TextBlock { Text = label + ":", TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Control value;
        if (field.Editable && !removed)
        {
            var box = new TextBox { Text = _changes.TryGetValue(key, out var edited) && edited is not null ? edited : field.Value };
            box.TextChanged += (_, _) => { if (box.Text != field.Value) _changes[key] = box.Text ?? ""; else _changes.Remove(key); };
            value = box;
        }
        else value = new TextBlock { Text = field.Value, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextDecorations = removed ? TextDecorations.Strikethrough : null, Opacity = removed ? 0.45 : 1 };
        var toggle = new Button { Content = new PathIcon { Data = Geometry(removed ? "Icon.Plus" : "Icon.Minus"), Width = 12, Height = 12 }, IsEnabled = field.Removable, Opacity = field.Removable ? 1 : 0.3 };
        toggle.Classes.Add("transport");
        ToolTip.SetTip(toggle, Loc[removed ? "tool.metadata.keep" : "tool.metadata.remove"]);
        toggle.Click += (_, _) =>
        {
            if (removed) _changes.Remove(key); else _changes[key] = null;
            Rebuild();
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(name);
        Grid.SetColumn(value, 1); row.Children.Add(value);
        Grid.SetColumn(toggle, 2); toggle.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(toggle);
        return row;
    }

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var removeAll = _removeAll;
        _removeAll = false;
        var changes = _changes.Select(c => new MetaChange(c.Key.Item1, c.Key.Item2, c.Value)).ToList();
        if (!removeAll && changes.Count == 0) throw new InvalidOperationException("tool.metadata.nothing");
        var outputs = new List<string>();
        // "Remove All" cleans every selected file; changes to single fields apply to the file shown.
        foreach (var file in removeAll ? _files : [_files[0]])
        {
            ct.ThrowIfCancellationRequested();
            var output = OutputNextTo(file, Path.GetExtension(file));
            await MetadataEditor.SaveAsync(file, changes, removeAll, output, ct);
            outputs.Add(output);
            progress.Report((double)outputs.Count / _files.Count);
        }
        return outputs;
    }
}

// ───────────────────────── Annotate ─────────────────────────

/// <summary>
/// The picture with its annotations, drawn the way <see cref="AnnotationRenderer"/> will save them. The current tool
/// decides what a drag does: draw a shape, a stroke, place text or a number, or (Select) move the one under the pointer.
/// </summary>
public sealed class AnnotateCanvas : Control
{
    private Bitmap? _picture;
    private PixelSize _size;
    private readonly Dictionary<string, Bitmap> _pictures = [];
    private Annotation? _drawing;
    private int _moving = -1;
    private Point _grab;

    public List<Annotation> Annotations { get; } = [];
    public AnnotationKind? Tool { get; set; } = AnnotationKind.Arrow;
    public string Color { get; set; } = "#E53935";
    public double Thickness { get; set; } = 1;
    public string Text { get; set; } = "";
    public string PicturePath { get; set; } = "";
    public int Selected { get; private set; } = -1;
    public event Action? Changed;

    public AnnotateCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public PixelSize SourceSize => _size;

    public void SetPicture(Bitmap picture, PixelSize size)
    {
        _picture = picture;
        _size = size;
        InvalidateVisual();
    }

    /// <summary>The line width for this picture: a 200th of its longer side, times the chosen thickness.</summary>
    private double Width => Math.Max(_size.Width, _size.Height) / 200.0 * Thickness;

    private Rect PictureRect
    {
        get
        {
            if (_size.Width <= 0) return default;
            var scale = Math.Min(Bounds.Width / _size.Width, Bounds.Height / _size.Height);
            return new Rect((Bounds.Width - _size.Width * scale) / 2, (Bounds.Height - _size.Height * scale) / 2, _size.Width * scale, _size.Height * scale);
        }
    }

    private double Scale => _size.Width > 0 ? PictureRect.Width / _size.Width : 1;
    private Point Screen(double x, double y) => new(PictureRect.X + x * Scale, PictureRect.Y + y * Scale);
    private Point Source(Point p) => new((p.X - PictureRect.X) / Scale, (p.Y - PictureRect.Y) / Scale);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Avalonia.Media.Color.Parse("#161616")), new Rect(Bounds.Size));
        if (_picture is null) return;
        var rect = PictureRect;
        context.DrawImage(_picture, new Rect(_picture.Size), rect);
        using (context.PushClip(rect))
        {
            for (var i = 0; i < Annotations.Count; i++) Draw(context, Annotations[i], i == Selected);
            if (_drawing is not null) Draw(context, _drawing, false);
        }
    }

    private void Draw(DrawingContext context, Annotation a, bool selected)
    {
        var p = a.Points;
        var color = Avalonia.Media.Color.Parse(a.Color);
        var brush = new SolidColorBrush(color);
        var width = a.Width * Scale;
        switch (a.Kind)
        {
            case AnnotationKind.Arrow when p.Length >= 4:
            {
                var from = Screen(p[0], p[1]); var to = Screen(p[2], p[3]);
                var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
                var head = Math.Max(width * 3.2, 14 * Scale);
                var baseAt = new Point(to.X - Math.Cos(angle) * head * 0.8, to.Y - Math.Sin(angle) * head * 0.8);
                context.DrawLine(new Pen(brush, width, lineCap: PenLineCap.Round), from, baseAt);
                var tip = new StreamGeometry();
                using (var g = tip.Open())
                {
                    g.BeginFigure(to, true);
                    g.LineTo(new Point(to.X - Math.Cos(angle - 0.45) * head, to.Y - Math.Sin(angle - 0.45) * head));
                    g.LineTo(new Point(to.X - Math.Cos(angle + 0.45) * head, to.Y - Math.Sin(angle + 0.45) * head));
                    g.EndFigure(true);
                }
                context.DrawGeometry(brush, null, tip);
                break;
            }
            case AnnotationKind.Pen or AnnotationKind.Highlight when p.Length >= 4:
            {
                var highlight = a.Kind == AnnotationKind.Highlight;
                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(Screen(p[0], p[1]), false);
                    for (var i = 2; i + 1 < p.Length; i += 2) g.LineTo(Screen(p[i], p[i + 1]));
                    g.EndFigure(false);
                }
                var stroke = highlight ? new SolidColorBrush(Avalonia.Media.Color.FromArgb(97, color.R, color.G, color.B)) : brush;
                context.DrawGeometry(null, new Pen(stroke, highlight ? width * 3 : width, lineCap: highlight ? PenLineCap.Square : PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
                break;
            }
            case AnnotationKind.Rectangle when p.Length >= 4:
                context.DrawRectangle(null, new Pen(brush, width), new Rect(Screen(p[0], p[1]), Screen(p[2], p[3])).Normalize(), width, width);
                break;
            case AnnotationKind.Ellipse when p.Length >= 4:
                context.DrawEllipse(null, new Pen(brush, width), new Rect(Screen(p[0], p[1]), Screen(p[2], p[3])).Normalize());
                break;
            case AnnotationKind.Text when p.Length >= 2:
            {
                var lines = a.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var text = new FormattedText(lines[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Arial, Inter"), Math.Max(1, width), brush);
                    context.DrawText(text, Screen(p[0], p[1] + a.Width * i * 1.15));
                }
                break;
            }
            case AnnotationKind.Number when p.Length >= 2:
            {
                var c = Screen(p[0], p[1]);
                context.DrawEllipse(brush, new Pen(Brushes.White, Math.Max(2, width / 9)), c, width, width);
                var text = new FormattedText(a.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI, Arial, Inter", FontStyle.Normal, FontWeight.Bold), Math.Max(1, width * 1.1), Brushes.White);
                context.DrawText(text, new Point(c.X - text.Width / 2, c.Y - text.Height / 2));
                break;
            }
            case AnnotationKind.Magnifier when p.Length >= 4 && _picture is not null:
            {
                var r = Math.Max(10, Math.Sqrt(Math.Pow(p[2] - p[0], 2) + Math.Pow(p[3] - p[1], 2)));
                var c = Screen(p[0], p[1]);
                var lens = new Rect(c.X - r * Scale, c.Y - r * Scale, 2 * r * Scale, 2 * r * Scale);
                var k = _picture.Size.Width / _size.Width;
                using (context.PushGeometryClip(new EllipseGeometry(lens)))
                    context.DrawImage(_picture, new Rect((p[0] - r / 2) * k, (p[1] - r / 2) * k, r * k, r * k), lens);
                context.DrawEllipse(null, new Pen(brush, Math.Max(3, a.Width / 2) * Scale), lens);
                break;
            }
            case AnnotationKind.Picture when p.Length >= 4:
                if (Picture(a.PicturePath) is { } bitmap)
                {
                    var box = new Rect(Screen(p[0], p[1]), Screen(p[2], p[3])).Normalize();
                    var fit = Math.Min(box.Width / bitmap.Size.Width, box.Height / bitmap.Size.Height);
                    var size = new Size(bitmap.Size.Width * fit, bitmap.Size.Height * fit);
                    context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(box.Center.X - size.Width / 2, box.Center.Y - size.Height / 2, size.Width, size.Height));
                }
                break;
        }
        if (selected && Area(a) is { } b)
            context.DrawRectangle(null, new Pen(this.TryFindResource("AccentBrush", out var accent) && accent is IBrush ab ? ab : Brushes.OrangeRed, 1.5, new DashStyle([4, 3], 0)), b.Inflate(6));
    }

    private Bitmap? Picture(string path)
    {
        if (path.Length == 0 || !File.Exists(path)) return null;
        if (!_pictures.TryGetValue(path, out var bitmap))
        {
            try { _pictures[path] = bitmap = new Bitmap(path); }
            catch (Exception) { return null; }
        }
        return bitmap;
    }

    /// <summary>Where an annotation is on screen, for picking and the selection frame.</summary>
    private Rect? Area(Annotation a)
    {
        var p = a.Points;
        if (p.Length < 2) return null;
        switch (a.Kind)
        {
            case AnnotationKind.Number: { var c = Screen(p[0], p[1]); var r = a.Width * Scale; return new Rect(c.X - r, c.Y - r, 2 * r, 2 * r); }
            case AnnotationKind.Text: return new Rect(Screen(p[0], p[1]), new Size(Math.Max(20, a.Text.Length * a.Width * 0.55 * Scale), a.Width * 1.3 * Scale * Math.Max(1, a.Text.Split('\n').Length)));
            case AnnotationKind.Magnifier when p.Length >= 4:
            {
                var r = Math.Sqrt(Math.Pow(p[2] - p[0], 2) + Math.Pow(p[3] - p[1], 2)) * Scale; var c = Screen(p[0], p[1]);
                return new Rect(c.X - r, c.Y - r, 2 * r, 2 * r);
            }
        }
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i + 1 < p.Length; i += 2) { var s = Screen(p[i], p[i + 1]); minX = Math.Min(minX, s.X); minY = Math.Min(minY, s.Y); maxX = Math.Max(maxX, s.X); maxY = Math.Max(maxY, s.Y); }
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY)).Inflate(a.Width * Scale);
    }

    private int NextNumber() => Annotations.Where(a => a.Kind == AnnotationKind.Number).Select(a => a.Number).DefaultIfEmpty(0).Max() + 1;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_picture is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var at = e.GetPosition(this);
        var source = Source(at);
        // Select: the topmost annotation under the pointer, to move.
        if (Tool is null)
        {
            Selected = -1;
            for (var i = Annotations.Count - 1; i >= 0; i--)
                if (Area(Annotations[i]) is { } b && b.Inflate(6).Contains(at)) { Selected = i; break; }
            if (Selected >= 0) { _moving = Selected; _grab = source; e.Pointer.Capture(this); }
            InvalidateVisual();
            Changed?.Invoke();
            return;
        }
        var kind = Tool.Value;
        if (kind is AnnotationKind.Text or AnnotationKind.Number)
        {
            if (kind == AnnotationKind.Text && Text.Trim().Length == 0) return;
            Annotations.Add(new Annotation
            {
                Kind = kind, Points = [source.X, source.Y], Color = Color, Text = Text,
                Width = kind == AnnotationKind.Text ? Width * 5 : Width * 3.5, Number = NextNumber(),
            });
            Selected = Annotations.Count - 1;
            InvalidateVisual();
            Changed?.Invoke();
            return;
        }
        if (kind == AnnotationKind.Picture && PicturePath.Length == 0) return;
        _drawing = new Annotation { Kind = kind, Points = [source.X, source.Y, source.X, source.Y], Color = Color, Width = kind == AnnotationKind.Magnifier ? Width : Width, PicturePath = PicturePath };
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var source = Source(e.GetPosition(this));
        var x = Math.Clamp(source.X, 0, _size.Width); var y = Math.Clamp(source.Y, 0, _size.Height);
        if (_moving >= 0)
        {
            var (dx, dy) = (source.X - _grab.X, source.Y - _grab.Y);
            _grab = source;
            var a = Annotations[_moving];
            Annotations[_moving] = a with { Points = a.Points.Select((v, i) => v + (i % 2 == 0 ? dx : dy)).ToArray() };
            InvalidateVisual();
            return;
        }
        if (_drawing is null) return;
        _drawing = _drawing.Kind is AnnotationKind.Pen or AnnotationKind.Highlight
            ? _drawing with { Points = Smooth(_drawing.Points, x, y) }
            : _drawing with { Points = [_drawing.Points[0], _drawing.Points[1], x, y] };
        InvalidateVisual();
    }

    /// <summary>A stroke gets a point every few pixels, not every pointer move.</summary>
    private double[] Smooth(double[] points, double x, double y)
    {
        var (lx, ly) = (points[^2], points[^1]);
        if (points.Length > 4 && Math.Abs(x - lx) + Math.Abs(y - ly) < 3 / Scale) return points;
        return points.Length == 4 && points[0] == points[2] && points[1] == points[3] ? [points[0], points[1], x, y] : [.. points, x, y];
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_moving >= 0) { _moving = -1; Changed?.Invoke(); return; }
        if (_drawing is null) return;
        var p = _drawing.Points;
        // A click without a drag draws nothing.
        if (Math.Abs(p[^2] - p[0]) + Math.Abs(p[^1] - p[1]) > 4 / Scale)
        {
            Annotations.Add(_drawing);
            Selected = Annotations.Count - 1;
        }
        _drawing = null;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && Selected >= 0) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Undo(); e.Handled = true; }
    }

    public void DeleteSelected()
    {
        if (Selected < 0 || Selected >= Annotations.Count) return;
        Annotations.RemoveAt(Selected);
        Selected = -1;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (Annotations.Count == 0) return;
        Annotations.RemoveAt(Annotations.Count - 1);
        Selected = -1;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void Clear()
    {
        Annotations.Clear();
        Selected = -1;
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>The selected text or number takes the new colour (and text) too.</summary>
    public void ApplyToSelected()
    {
        if (Selected < 0 || Selected >= Annotations.Count) return;
        var a = Annotations[Selected];
        Annotations[Selected] = a with { Color = Color, Text = a.Kind == AnnotationKind.Text && Text.Length > 0 ? Text : a.Text };
        InvalidateVisual();
    }
}

/// <summary>"Annotate photo": arrows, pen, text, shapes, highlighter, numbered labels, magnifier and pictures.</summary>
public sealed class AnnotateTool : ToolShell
{
    private static readonly (AnnotationKind? Kind, string Icon, string Key)[] Tools =
    [
        (null, "Icon.Cursor", "select"), (AnnotationKind.Arrow, "Icon.ArrowTopLeft", "arrow"), (AnnotationKind.Pen, "Icon.Pen", "pen"),
        (AnnotationKind.Text, "Icon.Text", "text"), (AnnotationKind.Rectangle, "Icon.Square", "rectangle"), (AnnotationKind.Ellipse, "Icon.Circle", "ellipse"),
        (AnnotationKind.Highlight, "Icon.Marker", "highlight"), (AnnotationKind.Number, "Icon.Numbered", "number"), (AnnotationKind.Magnifier, "Icon.Search", "magnifier"),
        (AnnotationKind.Picture, "Icon.ImagePlus", "picture"),
    ];
    private static readonly string[] Palette = ["#E53935", "#F35B28", "#FDD835", "#43A047", "#1E88E5", "#111111", "#FFFFFF"];

    private readonly string _file;
    private readonly AnnotateCanvas _canvas = new() { Height = 520 };
    private readonly List<Button> _toolButtons = [];
    private readonly List<Button> _colorButtons = [];
    private readonly TextBox _text = new() { MinWidth = 220, AcceptsReturn = false };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Segmented _thickness;
    private readonly Button _undo = new(), _delete = new();

    public AnnotateTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.annotate.title", loc, 900, 900)
    {
        _file = files[0];
        var tools = new WrapPanel();
        foreach (var (kind, icon, key) in Tools)
        {
            var button = new Button { Content = new PathIcon { Data = Geometry(icon), Width = 18, Height = 18 }, Margin = new Thickness(0, 0, 8, 8) };
            button.Classes.Add("tool-pick");
            ToolTip.SetTip(button, loc["tool.annotate." + key]);
            button.Click += async (_, _) => await PickAsync(kind, button, key);
            _toolButtons.Add(button);
            tools.Children.Add(button);
        }
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var color in Palette)
        {
            var button = new Button { Background = new SolidColorBrush(Color.Parse(color)), Tag = color };
            button.Classes.Add("swatch");
            button.Click += (_, _) => { _canvas.Color = color; Highlight(); _canvas.ApplyToSelected(); };
            _colorButtons.Add(button);
            colors.Children.Add(button);
        }
        _thickness = new Segmented([loc["tool.annotate.thin"], loc["tool.annotate.medium"], loc["tool.annotate.thick"]], 1);
        _thickness.Changed += i => _canvas.Thickness = i switch { 0 => 0.6, 2 => 1.8, _ => 1 };
        _text.PlaceholderText = loc["tool.annotate.text_hint"];
        _text.TextChanged += (_, _) => { _canvas.Text = _text.Text ?? ""; _canvas.ApplyToSelected(); };
        _undo.Content = new PathIcon { Data = Geometry("Icon.Undo"), Width = 16, Height = 16 };
        _undo.Click += (_, _) => _canvas.Undo();
        ToolTip.SetTip(_undo, loc["tool.annotate.undo"]);
        _delete.Content = new PathIcon { Data = Geometry("Icon.Delete"), Width = 16, Height = 16 };
        _delete.Click += (_, _) => _canvas.DeleteSelected();
        ToolTip.SetTip(_delete, loc["tool.annotate.delete"]);
        _canvas.Changed += () => { _delete.IsEnabled = _canvas.Selected >= 0; _undo.IsEnabled = _canvas.Annotations.Count > 0; };
        var styleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        styleRow.Children.Add(colors);
        Grid.SetColumn(_thickness, 2); _thickness.Margin = new Thickness(0, 0, 12, 0); styleRow.Children.Add(_thickness);
        Grid.SetColumn(_undo, 3); _undo.Margin = new Thickness(0, 0, 8, 0); styleRow.Children.Add(_undo);
        Grid.SetColumn(_delete, 4); styleRow.Children.Add(_delete);
        _hint.Classes.Add("muted");
        _hint.Margin = new Thickness(0, 12, 0, 0);
        Body = new StackPanel { Children = { Framed(_canvas), tools.Also(t => t.Margin = new Thickness(0, 14, 0, 0)), styleRow, _text.Also(t => t.Margin = new Thickness(0, 10, 0, 0)), _hint } };
        PrimaryText = loc["tool.annotate.run"];
        SetSecondary(loc["tool.pdfshrink.reset"], _canvas.Clear);
        _ = PickAsync(AnnotationKind.Arrow, _toolButtons[1], "arrow");
        _canvas.Color = Palette[0];
        Highlight();
        _delete.IsEnabled = _undo.IsEnabled = false;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var (bitmap, width, height) = await CropService.LoadPreviewAsync(_file);
            _canvas.SetPicture(bitmap, new PixelSize(width, height));
        }
        catch (Exception) { Status = Loc["error.unsupported_source"]; PrimaryEnabled = false; }
    }

    private async Task PickAsync(AnnotationKind? kind, Button button, string key)
    {
        if (kind == AnnotationKind.Picture)
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            _canvas.PicturePath = path;
        }
        _canvas.Tool = kind;
        foreach (var b in _toolButtons) b.Classes.Set("on", b == button);
        _text.IsVisible = kind == AnnotationKind.Text;
        _hint.Text = Loc["tool.annotate.hint_" + key];
        _canvas.Cursor = new Cursor(kind is null ? StandardCursorType.Arrow : StandardCursorType.Cross);
    }

    private void Highlight()
    {
        foreach (var b in _colorButtons) b.Classes.Set("on", (string)b.Tag! == _canvas.Color);
    }

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        if (_canvas.Annotations.Count == 0) throw new InvalidOperationException("tool.annotate.nothing");
        var output = OutputNextTo(_file, Path.GetExtension(_file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" ? Path.GetExtension(_file) : ".png");
        await AnnotationRenderer.SaveAsync(_file, _canvas.Annotations.ToList(), output, ct);
        return [output];
    }
}

// ───────────────────────── Edit photo ─────────────────────────

/// <summary>"Edit photo": light, colour and detail sliders on a live preview; the result is a new file.</summary>
public sealed class EditPhotoTool : ToolShell
{
    private sealed record Setting(string Key, double Min, double Max, string Unit, Func<PhotoSettings, double, PhotoSettings> Set);

    private static readonly (string Group, Setting[] Settings)[] Groups =
    [
        ("light", [
            new("exposure", -2, 2, " EV", (s, v) => s with { Exposure = v }), new("brightness", -100, 100, "", (s, v) => s with { Brightness = v }),
            new("contrast", -100, 100, "", (s, v) => s with { Contrast = v }), new("highlights", -100, 100, "", (s, v) => s with { Highlights = v }),
            new("shadows", -100, 100, "", (s, v) => s with { Shadows = v }), new("whites", -100, 100, "", (s, v) => s with { Whites = v }),
            new("blacks", -100, 100, "", (s, v) => s with { Blacks = v }), new("gamma", -100, 100, "", (s, v) => s with { Gamma = v }),
        ]),
        ("color", [
            new("temperature", -100, 100, "", (s, v) => s with { Temperature = v }), new("tint", -100, 100, "", (s, v) => s with { Tint = v }),
            new("saturation", -100, 100, "", (s, v) => s with { Saturation = v }), new("vibrance", -100, 100, "", (s, v) => s with { Vibrance = v }),
            new("hue", -180, 180, "°", (s, v) => s with { Hue = v }),
        ]),
        ("detail", [
            new("clarity", -100, 100, "", (s, v) => s with { Clarity = v }), new("dehaze", -100, 100, "", (s, v) => s with { Dehaze = v }),
            new("grain", 0, 100, "", (s, v) => s with { Grain = v }), new("noise", 0, 100, "", (s, v) => s with { NoiseReduction = v }),
        ]),
    ];

    private readonly string _file;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly Dictionary<string, Slider> _sliders = [];
    private readonly ToggleButton _original = new();
    private MagickImage? _proxy;
    private Bitmap? _edited, _plain;
    private int _request;

    public EditPhotoTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.editphoto.title", loc, 760, 1000)
    {
        _file = files[0];
        var body = new StackPanel();
        var stage = new Border { Height = 440, Child = _preview, Background = new SolidColorBrush(Color.Parse("#161616")) };
        body.Children.Add(Framed(stage));
        _original.Content = loc["tool.editphoto.original"];
        _original.HorizontalAlignment = HorizontalAlignment.Right;
        _original.Margin = new Thickness(0, 10, 0, 0);
        _original.IsCheckedChanged += (_, _) => _preview.Source = _original.IsChecked == true ? _plain : _edited;
        body.Children.Add(_original);
        foreach (var (group, settings) in Groups)
        {
            body.Children.Add(new TextBlock { Text = loc["tool.editphoto." + group], FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 6) }.Also(t => t.Classes.Add("muted")));
            foreach (var setting in settings) body.Children.Add(SliderRow(setting));
        }
        Body = body;
        PrimaryText = loc["tool.editphoto.run"];
        SetSecondary(loc["tool.pdfshrink.reset"], () => { foreach (var s in _sliders.Values) s.Value = 0; });
        _ = LoadAsync();
    }

    private Control SliderRow(Setting setting)
    {
        var slider = new Slider { Minimum = setting.Min, Maximum = setting.Max, Value = 0, Margin = new Thickness(12, 0) };
        var value = new TextBlock { Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFeatures = FontFeatureCollection.Parse("tnum") };
        var reset = new Button { Content = new PathIcon { Data = Geometry("Icon.Undo"), Width = 12, Height = 12 } };
        reset.Classes.Add("transport");
        reset.Click += (_, _) => slider.Value = 0;
        void Show() => value.Text = (setting.Key == "exposure" ? slider.Value.ToString("+0.00;−0.00;0.00", CultureInfo.CurrentCulture) : slider.Value.ToString("+0;−0;0", CultureInfo.CurrentCulture)) + setting.Unit;
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            if (setting.Key != "exposure") slider.Value = Math.Round(slider.Value);
            Show();
            Render();
        };
        Show();
        _sliders[setting.Key] = slider;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = Loc["tool.editphoto." + setting.Key], VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        Grid.SetColumn(value, 2); row.Children.Add(value);
        Grid.SetColumn(reset, 3); reset.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(reset);
        return row;
    }

    private PhotoSettings Settings()
    {
        var settings = new PhotoSettings();
        foreach (var (_, group) in Groups)
            foreach (var setting in group) settings = setting.Set(settings, _sliders[setting.Key].Value);
        return settings;
    }

    private async Task LoadAsync()
    {
        try
        {
            _proxy = await Task.Run(() =>
            {
                var image = new MagickImage(_file);
                image.AutoOrient();
                if (Math.Max(image.Width, image.Height) > 1100) image.Resize(new MagickGeometry(1100, 1100));
                return image;
            });
            using var s = new MemoryStream(_proxy.ToByteArray(MagickFormat.Png));
            _plain = new Bitmap(s);
            _edited = _plain;
            _preview.Source = _plain;
        }
        catch (Exception) { Status = Loc["error.unsupported_source"]; PrimaryEnabled = false; }
    }

    /// <summary>The preview follows the sliders: the last change wins, older renders are dropped.</summary>
    private async void Render()
    {
        if (_proxy is null) return;
        var request = ++_request;
        var settings = Settings();
        await Task.Delay(60);
        if (request != _request) return;
        var proxy = _proxy;
        try
        {
            var png = await Task.Run(() => { using var copy = (MagickImage)proxy.Clone(); PhotoAdjust.Apply(copy, settings); return copy.ToByteArray(MagickFormat.Png); });
            if (request != _request) return;
            using var s = new MemoryStream(png);
            _edited = new Bitmap(s);
            if (_original.IsChecked != true) _preview.Source = _edited;
        }
        catch (Exception ex) { Status = Message(ex); }
    }

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var settings = Settings();
        if (settings.IsNeutral) throw new InvalidOperationException("tool.editphoto.nothing");
        var output = OutputNextTo(_file, Path.GetExtension(_file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".tif" or ".tiff" ? Path.GetExtension(_file) : ".jpg");
        await PhotoAdjust.SaveAsync(_file, settings, output, ct);
        return [output];
    }
}

// ───────────────────────── Detection ─────────────────────────

/// <summary>Faces and lines of text in a picture, found by Windows itself through the Dilim.Detect helper.</summary>
public static class Detection
{
    private static string Helper => Path.Combine(AppContext.BaseDirectory, "detect", "Dilim.Detect.exe");

    public static bool Available => OperatingSystem.IsWindows() && File.Exists(Helper);

    public static async Task<IReadOnlyList<Rect>> FindAsync(string file, bool faces, CancellationToken ct = default)
    {
        if (!Available) throw new InvalidOperationException("tool.redact.detect_unavailable");
        var info = new ProcessStartInfo(Helper) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(faces ? "faces" : "text");
        info.ArgumentList.Add(file);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("tool.redact.detect_unavailable");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim().Length > 0 ? error.Trim() : "tool.redact.detect_failed");
        return Parse(output);
    }

    /// <summary>[{"x":..,"y":..,"w":..,"h":..}] as rectangles.</summary>
    public static IReadOnlyList<Rect> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => new Rect(e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(), e.GetProperty("w").GetInt32(), e.GetProperty("h").GetInt32())).ToList();
    }
}
