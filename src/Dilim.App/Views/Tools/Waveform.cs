using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>
/// A recording drawn as bars (silence as a dotted line), with the playhead. In range mode the kept part is framed by
/// two orange handles and the rest is faded (trim); clicking moves the playhead.
/// </summary>
public sealed class WaveformView : Control
{
    private const double HandleWidth = 16;
    private enum Drag { None, Seek, Start, End }
    private Drag _drag;

    public float[] Peaks { get; set; } = [];
    public double Duration { get; set; }
    public double Playhead { get; set; }
    public bool RangeMode { get; set; }
    public double Start { get; set; }
    public double End { get; set; }

    /// <summary>Bars in the accent colour (else a neutral gray, as for channel lanes).</summary>
    public bool Accent { get; set; } = true;

    public event Action<double>? Seeked;
    public event Action? Changed;

    public WaveformView()
    {
        Height = 180;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private Rect Area => RangeMode ? new Rect(HandleWidth, 0, Math.Max(1, Bounds.Width - 2 * HandleWidth), Bounds.Height) : new Rect(Bounds.Size);
    private double X(double t) => Area.X + (Duration > 0 ? t / Duration : 0) * Area.Width;
    private double T(double x) => Math.Clamp((x - Area.X) / Area.Width, 0, 1) * Duration;

    public override void Render(DrawingContext context)
    {
        var area = Area;
        IBrush Resource(string key, IBrush fallback) => this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;
        var accent = Resource("AccentBrush", Brushes.OrangeRed);
        var faint = Resource("AccentFaintBrush", Brushes.MistyRose);
        var panel = Resource("SurfaceAltBrush", Brushes.WhiteSmoke);
        var gray = Resource("TextSecondaryBrush", Brushes.Gray);
        context.DrawRectangle(RangeMode ? faint : panel, null, new RoundedRect(new Rect(Bounds.Size), 14));
        if (Peaks.Length > 0)
        {
            // A bar every 5 px: as many as fit, each the loudest of its share of the recording.
            var count = Math.Max(1, (int)(area.Width / 5));
            var middle = area.Center.Y;
            for (var i = 0; i < count; i++)
            {
                var from = i * Peaks.Length / count;
                var to = Math.Max(from + 1, (i + 1) * Peaks.Length / count);
                var peak = 0f;
                for (var k = from; k < to && k < Peaks.Length; k++) peak = Math.Max(peak, Peaks[k]);
                var x = area.X + (i + 0.5) * area.Width / count;
                var t = (i + 0.5) / count * Duration;
                var inside = !RangeMode || (t >= Start && t <= End);
                var brush = Accent ? (inside ? accent : new SolidColorBrush(Color.FromArgb(90, 243, 91, 40))) : gray;
                if (peak < 0.02)
                    context.DrawEllipse(brush, null, new Point(x, middle), 1.5, 1.5);
                else
                {
                    var half = Math.Max(2, peak * (area.Height / 2 - 14));
                    context.DrawRectangle(brush, null, new Rect(x - 1.25, middle - half, 2.5, half * 2), 1.25, 1.25);
                }
            }
        }
        if (RangeMode)
        {
            var kept = new Rect(X(Start) - HandleWidth, 0, X(End) - X(Start) + 2 * HandleWidth, Bounds.Height);
            context.DrawRectangle(null, new Pen(accent, 3), kept.Deflate(1.5), 14, 14);
            foreach (var (x, left) in new[] { (X(Start) - HandleWidth, true), (X(End), false) })
            {
                context.DrawRectangle(accent, null, new Rect(x, 0, HandleWidth, Bounds.Height), 14, 14);
                context.FillRectangle(accent, new Rect(left ? x + HandleWidth / 2 : x, 0, HandleWidth / 2, Bounds.Height));
                context.DrawRectangle(Brushes.White, null, new Rect(x + HandleWidth / 2 - 1.5, Bounds.Height / 2 - 14, 3, 28), 1.5, 1.5);
            }
        }
        context.FillRectangle(RangeMode ? Brushes.White : gray, new Rect(X(Playhead) - 1, 0, 2, Bounds.Height));
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
    }

    private void Apply(double x)
    {
        var t = T(x);
        var minimum = Math.Min(0.05, Duration / 20);
        switch (_drag)
        {
            case Drag.Start: Start = Math.Min(t, End - minimum); Playhead = Start; break;
            case Drag.End: End = Math.Max(t, Start + minimum); Playhead = End; break;
            default: Playhead = RangeMode ? Math.Clamp(t, Start, End) : t; break;
        }
        Seeked?.Invoke(Playhead);
        Changed?.Invoke();
        InvalidateVisual();
    }
}

/// <summary>
/// The bleep layer: drag across it to add a section, drag a section to move it or its edges to adjust it; Delete or a
/// right click removes the selected one.
/// </summary>
public sealed class BleepLane : Control
{
    private const double Edge = 8;
    private enum Drag { None, New, Move, Start, End }
    private Drag _drag;
    private int _index = -1;
    private double _grab, _origin;

    public List<(double Start, double End)> Ranges { get; } = [];
    public double Duration { get; set; }
    public int Selected { get; private set; } = -1;
    public string Label { get; set; } = "Bleep";
    public event Action? Changed;

    public BleepLane()
    {
        Height = 56;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    private double X(double t) => Duration > 0 ? t / Duration * Bounds.Width : 0;
    private double T(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Duration;

    public override void Render(DrawingContext context)
    {
        IBrush Resource(string key, IBrush fallback) => this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;
        context.DrawRectangle(Resource("SurfaceAltBrush", Brushes.WhiteSmoke), null, new RoundedRect(new Rect(Bounds.Size), 12));
        var accent = Resource("AccentBrush", Brushes.OrangeRed);
        var soft = new SolidColorBrush(Color.FromArgb(150, 243, 91, 40));
        for (var i = 0; i < Ranges.Count; i++)
        {
            var r = new Rect(X(Ranges[i].Start), 4, Math.Max(6, X(Ranges[i].End) - X(Ranges[i].Start)), Bounds.Height - 8);
            context.DrawRectangle(i == Selected ? accent : soft, null, r, 10, 10);
            context.FillRectangle(Brushes.White, new Rect(r.X + 6, r.Center.Y - 10, 2.5, 20));
            context.FillRectangle(Brushes.White, new Rect(r.Right - 8.5, r.Center.Y - 10, 2.5, 20));
            var text = new FormattedText(Label, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);
            if (text.Width < r.Width - 24) context.DrawText(text, new Point(r.Center.X - text.Width / 2, r.Center.Y - text.Height / 2));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var x = e.GetPosition(this).X;
        var point = e.GetCurrentPoint(this);
        _index = Ranges.FindIndex(r => x >= X(r.Start) - Edge && x <= X(r.End) + Edge);
        if (point.Properties.IsRightButtonPressed)
        {
            if (_index >= 0) { Ranges.RemoveAt(_index); Selected = -1; Changed?.Invoke(); InvalidateVisual(); }
            return;
        }
        if (_index >= 0)
        {
            var r = Ranges[_index];
            _drag = Math.Abs(x - X(r.Start)) <= Edge ? Drag.Start : Math.Abs(x - X(r.End)) <= Edge ? Drag.End : Drag.Move;
            _grab = T(x);
            _origin = r.Start;
        }
        else
        {
            Ranges.Add((T(x), T(x)));
            _index = Ranges.Count - 1;
            _drag = Drag.New;
            _grab = T(x);
        }
        Selected = _index;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == Drag.None || _index < 0) return;
        var t = T(e.GetPosition(this).X);
        var r = Ranges[_index];
        Ranges[_index] = _drag switch
        {
            Drag.New => (Math.Min(_grab, t), Math.Max(_grab, t)),
            Drag.Start => (Math.Min(t, r.End - 0.05), r.End),
            Drag.End => (r.Start, Math.Max(t, r.Start + 0.05)),
            _ => Shift(r, _origin + t - _grab),
        };
        Changed?.Invoke();
        InvalidateVisual();
    }

    private (double, double) Shift((double Start, double End) r, double start)
    {
        var length = r.End - r.Start;
        start = Math.Clamp(start, 0, Duration - length);
        return (start, start + length);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Drag.New && _index >= 0 && Ranges[_index].End - Ranges[_index].Start < 0.05)
        {
            // A click only: a short bleep where it was clicked.
            var at = Ranges[_index].Start;
            var length = Math.Min(0.5, Duration / 4);
            Ranges[_index] = Shift((at, at + length), at - length / 2);
        }
        _drag = Drag.None;
        e.Pointer.Capture(null);
        Ranges.Sort();
        Selected = -1;
        Changed?.Invoke();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && Selected >= 0 && Selected < Ranges.Count)
        {
            Ranges.RemoveAt(Selected);
            Selected = -1;
            Changed?.Invoke();
            InvalidateVisual();
            e.Handled = true;
        }
    }
}

/// <summary>
/// Plays a preview through ffplay and moves a playhead along with it. One preview at a time per window; closing the
/// window stops it.
/// </summary>
public sealed class AudioPlayback
{
    private Process? _process;
    private DispatcherTimer? _timer;
    private Button? _button;
    private object? _idleContent;

    public static bool Available => AudioPreview.Player is not null;

    /// <summary>Called with the time being played, while it plays.</summary>
    public Action<double>? Moved { get; set; }

    public bool Playing => _process is not null;

    public void Toggle(Button button, object playingContent, string file, double start, double? duration, string? filter = null)
    {
        var same = _button == button && Playing;
        Stop();
        if (same) return;
        _process = AudioPreview.Play(file, start, duration, filter);
        if (_process is null) return;
        _button = button;
        _idleContent = button.Content;
        button.Content = playingContent;
        var started = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            if (_process is null || _process.HasExited || (duration is { } d && elapsed > d + 0.3)) { Stop(); return; }
            Moved?.Invoke(start + elapsed);
        });
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        AudioPreview.Stop(_process);
        _process = null;
        if (_button is not null) _button.Content = _idleContent;
        _button = null;
    }
}
