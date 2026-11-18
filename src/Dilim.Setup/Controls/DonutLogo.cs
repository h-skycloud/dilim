// Dilim's mark (src/Dilim.App/Assets/Icons/dilim.svg) drawn from its paths, so it can assemble itself: the white tile
// settles in, then the four slices fan out from the pivot under them, one after the other, with a little overshoot.
// Without animations (or after 1.4 s) it is simply the mark.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Dilim.Setup.Controls;

public sealed class DonutLogo : Control
{
    /// <summary>Plays the entrance when shown; off draws the finished logo.</summary>
    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<DonutLogo, bool>(nameof(Animate), true);

    private const double ViewBox = 512;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(1400);

    // dilim.svg: the tile, and the four slices inside <g transform="translate(24 78) scale(.8)">.
    private static readonly Geometry Tile = Geometry.Parse("M116 8 H396 A108 108 0 0 1 504 116 V396 A108 108 0 0 1 396 504 H116 A108 108 0 0 1 8 396 V116 A108 108 0 0 1 116 8 Z");
    private static readonly Geometry[] Slices =
    [
        Geometry.Parse("M62 210C80 190 96 192 115 211L252 348H62C26 348 8 334 16 302C26 263 42 229 62 210Z"),
        Geometry.Parse("M143 128C174 110 222 89 244 90C266 92 272 113 272 140V327L133 186C112 165 117 144 143 128Z"),
        Geometry.Parse("M329 90C352 89 400 109 431 128C458 144 463 165 442 186L302 327V140C302 113 307 93 329 90Z"),
        Geometry.Parse("M463 211C482 192 499 190 517 210C538 230 553 264 563 302C572 334 553 348 517 348H326Z"),
    ];

    private static readonly Matrix Group = Matrix.CreateScale(0.8, 0.8) * Matrix.CreateTranslation(24, 78);

    /// <summary>Where the slices meet, in the group's coordinates: they fan out from here.</summary>
    private static readonly Point Pivot = new(287, 348);

    private static readonly IBrush TileBrush = Brush("#FFFFFF");
    private static readonly IPen TilePen = new Pen(Brush("#1A000000"), 6);
    private static readonly IBrush SliceBrush = Brush("#050505");

    private readonly FrameLoop _loop;
    private double _progress;

    static DonutLogo()
    {
        AffectsRender<DonutLogo>(AnimateProperty);
    }

    public DonutLogo()
    {
        _progress = SetupMotion.Enabled ? 0 : 1;
        _loop = new FrameLoop(this, elapsed =>
        {
            _progress = Math.Min(1, elapsed / Duration);
            InvalidateVisual();
            return _progress < 1;
        });
    }

    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }

    /// <summary>Plays the entrance again.</summary>
    public void Replay()
    {
        if (!SetupMotion.Enabled || !Animate)
            return;
        _progress = 0;
        _loop.Restart();
    }

    private static IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 96 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var progress = Animate && SetupMotion.Enabled ? _progress : 1;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
            return;
        var scale = side / ViewBox;
        var origin = new Point((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        var center = new Point(ViewBox / 2, ViewBox / 2);
        var view = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y);

        // The tile settles in first.
        var tile = EaseOut(Math.Min(1, progress / 0.35));
        var settle = Matrix.CreateTranslation(-center.X, -center.Y)
                     * Matrix.CreateScale(0.86 + 0.14 * tile, 0.86 + 0.14 * tile)
                     * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(settle * view))
        using (context.PushOpacity(tile))
            context.DrawGeometry(TileBrush, TilePen, Tile);

        // Then the slices fan out from the pivot, left to right.
        for (var i = 0; i < Slices.Length; i++)
        {
            var local = Math.Clamp((progress - 0.18 - i * 0.1) / 0.45, 0, 1);
            if (local <= 0)
                continue;
            var spring = BackOut(local);
            var turn = (1 - spring) * (i < 2 ? 0.5 : -0.5);
            var fan = Matrix.CreateTranslation(-Pivot.X, -Pivot.Y)
                      * Matrix.CreateRotation(turn)
                      * Matrix.CreateScale(0.4 + 0.6 * spring, 0.4 + 0.6 * spring)
                      * Matrix.CreateTranslation(Pivot.X, Pivot.Y);
            using (context.PushTransform(fan * Group * view))
            using (context.PushOpacity(Math.Min(1, local * 1.8)))
                context.DrawGeometry(SliceBrush, null, Slices[i]);
        }
    }

    internal static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    internal static double BackOut(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
