using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Dilim.App.Controls;

// Speed: number keys choose a slice, a slice can be chosen by direction past the ring, and the pointer's direction is
// shown as a faint wedge there.
public partial class DonutMenu
{
    public static readonly StyledProperty<bool> ShowKeyNumbersProperty =
        AvaloniaProperty.Register<DonutMenu, bool>(nameof(ShowKeyNumbers));

    public static readonly StyledProperty<bool> DirectionalSelectProperty =
        AvaloniaProperty.Register<DonutMenu, bool>(nameof(DirectionalSelect));

    /// <summary>Small 1–9, 0 badges at the outer edge of the slices (the keys that choose them).</summary>
    public bool ShowKeyNumbers { get => GetValue(ShowKeyNumbersProperty); set => SetValue(ShowKeyNumbersProperty, value); }

    /// <summary>Past the ring, the slice in the pointer's direction is the one it points at (click mode).</summary>
    public bool DirectionalSelect { get => GetValue(DirectionalSelectProperty); set => SetValue(DirectionalSelectProperty, value); }

    /// <summary>The slice number <paramref name="key"/> stands for: 1–9 the first nine, 0 the tenth; -1 for other keys.</summary>
    public static int NumberOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
        Key.D0 or Key.NumPad0 => 9,
        _ => -1,
    };

    /// <summary>A number key: chooses that slice of the open ring, or of the donut (a group opens its ring).</summary>
    private bool HandleNumberKey(Key key)
    {
        var number = NumberOf(key);
        if (number < 0)
            return false;
        if (ExpandedChildren is { } children)
        {
            if (number >= children.Count) return true;
            SetSub(number);
            if (children[number].IsEnabled)
                SubSegmentInvoked?.Invoke(this, (_expanded, number));
            return true;
        }
        if (number >= Count) return true;
        SetHover(number);
        if (Items![number].CanExpand)
        {
            Expand(number);
            SetSub(0);
            _ringFromKeys = true;
        }
        else if (Items[number].IsEnabled)
        {
            SegmentInvoked?.Invoke(this, number);
        }
        return true;
    }

    /// <summary>The slice in the direction of <paramref name="p"/> when it is past the ring and that counts.</summary>
    private DonutHit Directional(DonutHit hit, Point p)
    {
        if (hit.Kind != DonutHitKind.Outside || !DirectionalSelect || IsEditMode || _progressMode || Count == 0 || ExpandedChildren is not null)
            return hit;
        return new DonutHit(DonutHitKind.Segment, DonutGeometry.IndexAt(DonutGeometry.AngleOf(Center, p), Count));
    }

    /// <summary>Number badges at the outer edge of each slice.</summary>
    private void DrawKeyNumber(DrawingContext context, int index, Point center, double r2, double mid, Typeface typeface, bool hot)
    {
        if (!ShowKeyNumbers || IsEditMode || index > 9)
            return;
        var text = MakeText(((index + 1) % 10).ToString(), typeface, 10, hot ? Foreground : SecondaryForeground, 20);
        var at = DonutGeometry.PointAt(center, r2 - 10, mid);
        using (context.PushOpacity(hot ? 0.95 : 0.6))
            context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
    }

    /// <summary>Past the ring: a faint wedge from the ring to the pointer over the slice it points at.</summary>
    private void DrawDirection(DrawingContext context, Point center, double r2)
    {
        if (!DirectionalSelect || IsEditMode || _keyboardHold || ExpandedChildren is not null || _hoverIndex < 0 || Count == 0)
            return;
        if (_lastPointer is not { } pointer)
            return;
        var distance = Distance(pointer, center);
        if (distance <= r2 + PopOut + 6)
            return;
        var wedge = CreateWedge(center, r2 + PopOut + 4, Math.Min(distance, r2 + 80), DonutGeometry.MidAngle(_hoverIndex, Count), DonutGeometry.Span(Count));
        context.DrawGeometry(WithOpacity(AccentBrush, 0.14), null, wedge);
    }

    /// <summary>Chooses the highlighted slice, as Enter does (turbo mode: the shortcut's keys were let go).</summary>
    public bool InvokeActive() => ActiveIndex >= 0 && HandleKeyCore(Key.Enter);
}
