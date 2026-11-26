using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Dilim.App.Views.Tools;

/// <summary>A rectangle on the picture, in source pixels. Redactions use its fill; video redactions its time range.</summary>
public sealed class CanvasBox
{
    public Rect Area { get; set; }
    public double Start { get; set; }
    public double End { get; set; } = double.MaxValue;
}

/// <summary>
/// A picture with rectangles on it to place precisely: drag a box to move it, its corner and edge handles to resize it
/// (keeping a ratio when one is set), and, where allowed, drag on the picture to draw a new one. Coordinates are in
/// the source's pixels whatever the preview's size. Used by Crop (one box, the rest dimmed), Redact (several boxes,
/// solid or blurred) and Crop video.
/// </summary>
public sealed class BoxCanvas : Control
{
    private const double HandleSize = 12, HitSlop = 10, MinBox = 8;

    private enum Grip { None, Move, N, S, E, W, NE, NW, SE, SW, New }

    private Bitmap? _picture, _blurred;
    private PixelSize _sourceSize;
    private (Grip Grip, Point Start, Rect Area, CanvasBox Box)? _drag;

    public List<CanvasBox> Boxes { get; } = [];
    public CanvasBox? Selected { get; private set; }

    /// <summary>Crop: one box, everything outside it dimmed. Otherwise boxes are filled (redaction).</summary>
    public bool CropMode { get; set; }

    /// <summary>Dragging on the picture outside a box draws a new one.</summary>
    public bool CanDraw { get; set; }

    /// <summary>Width / height to keep while resizing; null for free.</summary>
    public double? Ratio { get; set; }

    /// <summary>Fill of redaction boxes; with <see cref="Blur"/> the blurred picture shows through instead.</summary>
    public Color Fill { get; set; } = Colors.Black;
    public bool Blur { get; set; }

    /// <summary>Only boxes for which this returns true are shown and can be picked (a video's current time).</summary>
    public Func<CanvasBox, bool> IsActive { get; set; } = _ => true;

    public event Action? Changed;

    public PixelSize SourceSize => _sourceSize;

    public BoxCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Shows <paramref name="picture"/> (any size) standing for a source of <paramref name="sourceSize"/>.</summary>
    public void SetPicture(Bitmap? picture, PixelSize sourceSize, Bitmap? blurred = null)
    {
        _picture = picture;
        _blurred = blurred;
        _sourceSize = sourceSize;
        InvalidateVisual();
    }

    /// <summary>Replaces only the blurred look of the picture (for blurred redaction previews).</summary>
    public void SetBlurred(Bitmap? blurred)
    {
        _blurred = blurred;
        InvalidateVisual();
    }

    public void Select(CanvasBox? box)
    {
        Selected = box;
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    /// <summary>Clamps a box into the source and to a minimum size.</summary>
    public Rect Clamp(Rect r)
    {
        var w = Math.Clamp(r.Width, Math.Min(MinBox, _sourceSize.Width), _sourceSize.Width);
        var h = Math.Clamp(r.Height, Math.Min(MinBox, _sourceSize.Height), _sourceSize.Height);
        var x = Math.Clamp(r.X, 0, _sourceSize.Width - w);
        var y = Math.Clamp(r.Y, 0, _sourceSize.Height - h);
        return new Rect(Math.Round(x), Math.Round(y), Math.Round(w), Math.Round(h));
    }

    /// <summary>The largest box of <paramref name="ratio"/> centred in the source (the whole source when null).</summary>
    public Rect Centered(double? ratio)
    {
        double w = _sourceSize.Width, h = _sourceSize.Height;
        if (ratio is { } r && r > 0)
        {
            if (w / h > r) w = h * r; else h = w / r;
        }
        return new Rect((_sourceSize.Width - w) / 2, (_sourceSize.Height - h) / 2, w, h);
    }

    // ───────── Geometry ─────────

    /// <summary>Where the picture is drawn: fitted and centred.</summary>
    private Rect PictureRect
    {
        get
        {
            if (_sourceSize.Width <= 0 || _sourceSize.Height <= 0) return default;
            var scale = Math.Min(Bounds.Width / _sourceSize.Width, Bounds.Height / _sourceSize.Height);
            var size = new Size(_sourceSize.Width * scale, _sourceSize.Height * scale);
            return new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
        }
    }

    private double Scale => _sourceSize.Width > 0 ? PictureRect.Width / _sourceSize.Width : 1;

    private Rect ToScreen(Rect source)
    {
        var p = PictureRect; var s = Scale;
        return new Rect(p.X + source.X * s, p.Y + source.Y * s, source.Width * s, source.Height * s);
    }

    private Point ToSource(Point screen)
    {
        var p = PictureRect; var s = Scale;
        return new Point((screen.X - p.X) / s, (screen.Y - p.Y) / s);
    }

    // ───────── Drawing ─────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#161616")), new Rect(Bounds.Size));
        var pictureRect = PictureRect;
        if (_picture is null || pictureRect.Width <= 0) return;
        context.DrawImage(_picture, new Rect(_picture.Size), pictureRect);
        var accent = this.TryFindResource("AccentBrush", out var a) && a is IBrush brush ? brush : Brushes.OrangeRed;

        var visible = Boxes.Where(IsActive).ToList();
        if (CropMode && visible.FirstOrDefault() is { } crop)
        {
            // Dim everything outside the box.
            var inside = ToScreen(crop.Area);
            var dim = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(pictureRect), new RectangleGeometry(inside));
            context.DrawGeometry(dim, null, geometry);
            // Rule of thirds, faint.
            var thirds = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
            for (var i = 1; i < 3; i++)
            {
                context.DrawLine(thirds, new Point(inside.X + inside.Width * i / 3, inside.Y), new Point(inside.X + inside.Width * i / 3, inside.Bottom));
                context.DrawLine(thirds, new Point(inside.X, inside.Y + inside.Height * i / 3), new Point(inside.Right, inside.Y + inside.Height * i / 3));
            }
        }
        foreach (var box in visible)
        {
            var r = ToScreen(box.Area);
            if (!CropMode)
            {
                if (Blur && _blurred is not null)
                {
                    var bs = _blurred.Size.Width / _sourceSize.Width;
                    using (context.PushClip(r))
                        context.DrawImage(_blurred, new Rect(box.Area.X * bs, box.Area.Y * bs, box.Area.Width * bs, box.Area.Height * bs), r);
                }
                else context.FillRectangle(new SolidColorBrush(Fill), r);
            }
            var selected = box == Selected || CropMode;
            context.DrawRectangle(null, new Pen(selected ? accent : new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), selected ? 2 : 1), r);
            if (selected) DrawHandles(context, r, accent);
        }
    }

    private static void DrawHandles(DrawingContext context, Rect r, IBrush accent)
    {
        var half = HandleSize / 2;
        foreach (var c in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight })
            context.FillRectangle(accent, new Rect(c.X - half, c.Y - half, HandleSize, HandleSize), 2);
        // Edge handles: short pills in the middle of each side.
        context.FillRectangle(accent, new Rect(r.Center.X - 14, r.Top - 3, 28, 6), 3);
        context.FillRectangle(accent, new Rect(r.Center.X - 14, r.Bottom - 3, 28, 6), 3);
        context.FillRectangle(accent, new Rect(r.Left - 3, r.Center.Y - 14, 6, 28), 3);
        context.FillRectangle(accent, new Rect(r.Right - 3, r.Center.Y - 14, 6, 28), 3);
    }

    // ───────── Interaction ─────────

    private Grip HitGrip(Rect r, Point p)
    {
        bool Near(double a, double b) => Math.Abs(a - b) <= HitSlop;
        var inX = p.X >= r.Left - HitSlop && p.X <= r.Right + HitSlop;
        var inY = p.Y >= r.Top - HitSlop && p.Y <= r.Bottom + HitSlop;
        if (Near(p.X, r.Left) && Near(p.Y, r.Top)) return Grip.NW;
        if (Near(p.X, r.Right) && Near(p.Y, r.Top)) return Grip.NE;
        if (Near(p.X, r.Left) && Near(p.Y, r.Bottom)) return Grip.SW;
        if (Near(p.X, r.Right) && Near(p.Y, r.Bottom)) return Grip.SE;
        if (Near(p.Y, r.Top) && inX) return Grip.N;
        if (Near(p.Y, r.Bottom) && inX) return Grip.S;
        if (Near(p.X, r.Left) && inY) return Grip.W;
        if (Near(p.X, r.Right) && inY) return Grip.E;
        return r.Contains(p) ? Grip.Move : Grip.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _sourceSize.Width == 0) return;
        Focus();
        var p = e.GetPosition(this);
        // The selected box first (its handles stick out), then the others from the top.
        var candidates = Boxes.Where(IsActive).OrderByDescending(b => b == Selected).ThenByDescending(Boxes.IndexOf);
        foreach (var box in candidates)
        {
            var grip = HitGrip(ToScreen(box.Area), p);
            if (grip == Grip.None || (grip != Grip.Move && box != Selected && !CropMode)) continue;
            Selected = box;
            _drag = (grip, p, box.Area, box);
            e.Pointer.Capture(this);
            InvalidateVisual();
            Changed?.Invoke();
            return;
        }
        if (CanDraw && PictureRect.Contains(p))
        {
            var start = ToSource(p);
            var box = new CanvasBox { Area = new Rect(start, new Size(1, 1)) };
            Boxes.Add(box);
            Selected = box;
            _drag = (Grip.New, p, box.Area, box);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }
        if (Selected is not null && !CropMode) { Selected = null; InvalidateVisual(); Changed?.Invoke(); }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag is not { } drag)
        {
            Cursor = CursorFor(p);
            return;
        }
        var s = Scale;
        var dx = (p.X - drag.Start.X) / s;
        var dy = (p.Y - drag.Start.Y) / s;
        var r = drag.Area;
        Rect next;
        switch (drag.Grip)
        {
            case Grip.Move:
                next = new Rect(r.X + dx, r.Y + dy, r.Width, r.Height);
                next = new Rect(Math.Clamp(next.X, 0, _sourceSize.Width - r.Width), Math.Clamp(next.Y, 0, _sourceSize.Height - r.Height), r.Width, r.Height);
                break;
            case Grip.New:
                var a = ToSource(drag.Start); var b = ToSource(p);
                next = new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                if (Ratio is { } nr) next = next.WithHeight(next.Width / nr);
                break;
            default:
                double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
                if (drag.Grip is Grip.W or Grip.NW or Grip.SW) left = Math.Min(r.Left + dx, right - MinBox);
                if (drag.Grip is Grip.E or Grip.NE or Grip.SE) right = Math.Max(r.Right + dx, left + MinBox);
                if (drag.Grip is Grip.N or Grip.NW or Grip.NE) top = Math.Min(r.Top + dy, bottom - MinBox);
                if (drag.Grip is Grip.S or Grip.SW or Grip.SE) bottom = Math.Max(r.Bottom + dy, top + MinBox);
                left = Math.Max(0, left); top = Math.Max(0, top);
                right = Math.Min(_sourceSize.Width, right); bottom = Math.Min(_sourceSize.Height, bottom);
                next = new Rect(left, top, right - left, bottom - top);
                if (Ratio is { } ratio) next = KeepRatio(next, ratio, drag.Grip, r);
                break;
        }
        drag.Box.Area = Clamp(next);
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Resizing with a ratio: the dragged side leads, the other follows, anchored at the opposite side.</summary>
    private Rect KeepRatio(Rect r, double ratio, Grip grip, Rect before)
    {
        var widthLeads = grip is Grip.E or Grip.W || (grip is Grip.NE or Grip.NW or Grip.SE or Grip.SW && Math.Abs(r.Width - before.Width) >= Math.Abs(r.Height - before.Height) * ratio);
        double w = widthLeads ? r.Width : r.Height * ratio;
        double h = w / ratio;
        // Never past the picture.
        var maxW = grip is Grip.W or Grip.NW or Grip.SW ? before.Right : _sourceSize.Width - before.Left;
        var maxH = grip is Grip.N or Grip.NW or Grip.NE ? before.Bottom : _sourceSize.Height - before.Top;
        if (grip is Grip.E or Grip.W) maxH = _sourceSize.Height;
        if (grip is Grip.N or Grip.S) maxW = _sourceSize.Width;
        if (w > maxW) { w = maxW; h = w / ratio; }
        if (h > maxH) { h = maxH; w = h * ratio; }
        var x = grip is Grip.W or Grip.NW or Grip.SW ? before.Right - w : grip is Grip.N or Grip.S ? before.Center.X - w / 2 : before.Left;
        var y = grip is Grip.N or Grip.NW or Grip.NE ? before.Bottom - h : grip is Grip.E or Grip.W ? before.Center.Y - h / 2 : before.Top;
        return new Rect(x, y, w, h);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not { } drag) return;
        _drag = null;
        e.Pointer.Capture(null);
        // A click that drew nothing is not a box.
        if (drag.Grip == Grip.New && (drag.Box.Area.Width < MinBox * 1.5 || drag.Box.Area.Height < MinBox * 1.5))
        {
            Boxes.Remove(drag.Box);
            Selected = null;
        }
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Selected is null) return;
        if (e.Key is Key.Delete or Key.Back && !CropMode)
        {
            Boxes.Remove(Selected);
            Selected = null;
        }
        else
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            var (dx, dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
            if (dx == 0 && dy == 0) return;
            var r = Selected.Area;
            Selected.Area = Clamp(new Rect(r.X + dx, r.Y + dy, r.Width, r.Height));
        }
        e.Handled = true;
        InvalidateVisual();
        Changed?.Invoke();
    }

    private Cursor CursorFor(Point p)
    {
        foreach (var box in Boxes.Where(IsActive).OrderByDescending(b => b == Selected))
        {
            var grip = HitGrip(ToScreen(box.Area), p);
            if (grip != Grip.None && (grip == Grip.Move || box == Selected || CropMode))
                return new Cursor(grip switch
                {
                    Grip.N or Grip.S => StandardCursorType.SizeNorthSouth,
                    Grip.E or Grip.W => StandardCursorType.SizeWestEast,
                    Grip.NW or Grip.SE => StandardCursorType.TopLeftCorner,
                    Grip.NE or Grip.SW => StandardCursorType.TopRightCorner,
                    _ => StandardCursorType.SizeAll,
                });
        }
        return CanDraw && PictureRect.Contains(p) ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
    }
}
