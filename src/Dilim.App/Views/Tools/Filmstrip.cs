using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Dilim.App.Views.Tools;

/// <summary>
/// Frames of a video side by side, with the playhead. In range mode the kept part sits between two orange handles
/// and the rest is dimmed (trim); in points mode white markers show where the video will be cut (split). Clicking or
/// dragging on the strip moves the playhead; the handles and markers can be dragged.
/// </summary>
public sealed class Filmstrip : Control
{
    private const double HandleWidth = 16;
    private IReadOnlyList<Bitmap> _frames = [];
    private enum Drag { None, Seek, Start, End, Point }
    private Drag _drag;
    private int _dragPoint = -1;

    public double Duration { get; set; }
    public double Playhead { get; set; }

    /// <summary>Range mode: the part kept, in seconds.</summary>
    public bool RangeMode { get; set; }
    public double Start { get; set; }
    public double End { get; set; }

    /// <summary>Points mode: where to cut, in seconds.</summary>
    public List<double> Points { get; } = [];

    public event Action<double>? Seeked;
    public event Action? Changed;

    public Filmstrip()
    {
        Height = 72;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void SetFrames(IReadOnlyList<Bitmap> frames)
    {
        _frames = frames;
        InvalidateVisual();
    }

    private Rect Strip => RangeMode ? new Rect(HandleWidth, 0, Math.Max(1, Bounds.Width - 2 * HandleWidth), Bounds.Height) : new Rect(Bounds.Size);
    private double X(double t) => Strip.X + (Duration > 0 ? t / Duration : 0) * Strip.Width;
    private double T(double x) => Math.Clamp((x - Strip.X) / Strip.Width, 0, 1) * Duration;

    public override void Render(DrawingContext context)
    {
        var strip = Strip;
        var accent = this.TryFindResource("AccentBrush", out var a) && a is IBrush brush ? brush : Brushes.OrangeRed;
        using (context.PushClip(new RoundedRect(strip, 10)))
        {
            context.FillRectangle(new SolidColorBrush(Color.Parse("#1E1E1E")), strip);
            if (_frames.Count > 0)
            {
                // As many frames as fit at their own aspect, each from its share of the video.
                var aspect = _frames[0].Size.Width / Math.Max(1, _frames[0].Size.Height);
                var cell = strip.Height * aspect;
                var count = Math.Max(1, (int)Math.Ceiling(strip.Width / cell));
                for (var i = 0; i < count; i++)
                {
                    var frame = _frames[Math.Min(_frames.Count - 1, (int)((i + 0.5) / count * _frames.Count))];
                    context.DrawImage(frame, new Rect(frame.Size), new Rect(strip.X + i * cell, strip.Y, cell, strip.Height));
                }
            }
        }
        if (RangeMode)
        {
            var dim = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            context.FillRectangle(dim, new Rect(strip.X, 0, X(Start) - strip.X, Bounds.Height));
            context.FillRectangle(dim, new Rect(X(End), 0, strip.Right - X(End), Bounds.Height));
            var kept = new Rect(X(Start) - HandleWidth, 0, X(End) - X(Start) + 2 * HandleWidth, Bounds.Height);
            context.DrawRectangle(null, new Pen(accent, 3), kept.Deflate(1.5), 10, 10);
            var white = new SolidColorBrush(Colors.White);
            foreach (var (x, left) in new[] { (X(Start) - HandleWidth, true), (X(End), false) })
            {
                context.DrawRectangle(accent, null, new Rect(x, 0, HandleWidth, Bounds.Height), left ? 10 : 0, left ? 10 : 0);
                context.FillRectangle(accent, new Rect(left ? x + HandleWidth / 2 : x, 0, HandleWidth / 2, Bounds.Height));
                context.DrawRectangle(white, null, new Rect(x + HandleWidth / 2 - 1.5, Bounds.Height / 2 - 12, 3, 24), 1.5, 1.5);
            }
        }
        foreach (var point in Points)
        {
            var x = X(point);
            context.FillRectangle(accent, new Rect(x - 2, 0, 4, Bounds.Height));
            context.DrawEllipse(accent, null, new Point(x, 6), 6, 6);
        }
        // Playhead.
        var p = X(Playhead);
        context.FillRectangle(Brushes.White, new Rect(p - 1, 0, 2, Bounds.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var x = e.GetPosition(this).X;
        _drag = Drag.Seek;
        if (RangeMode)
        {
            if (Math.Abs(x - (X(Start) - HandleWidth / 2)) <= HandleWidth) _drag = Drag.Start;
            else if (Math.Abs(x - (X(End) + HandleWidth / 2)) <= HandleWidth) _drag = Drag.End;
        }
        for (var i = 0; i < Points.Count && _drag == Drag.Seek; i++)
            if (Math.Abs(x - X(Points[i])) <= 6) { _drag = Drag.Point; _dragPoint = i; }
        e.Pointer.Capture(this);
        Apply(x);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != Drag.None) Apply(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = Drag.None;
        e.Pointer.Capture(null);
        Points.Sort();
        Changed?.Invoke();
    }

    private void Apply(double x)
    {
        var t = T(x);
        var minimum = Math.Min(0.1, Duration / 10);
        switch (_drag)
        {
            case Drag.Start: Start = Math.Min(t, End - minimum); Seeked?.Invoke(Start); break;
            case Drag.End: End = Math.Max(t, Start + minimum); Seeked?.Invoke(End); break;
            case Drag.Point: Points[_dragPoint] = t; Seeked?.Invoke(t); break;
            case Drag.Seek: Seeked?.Invoke(RangeMode ? Math.Clamp(t, Start, End) : t); break;
        }
        Changed?.Invoke();
        InvalidateVisual();
    }
}
