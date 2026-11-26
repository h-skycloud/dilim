// Choosing faster: number keys, choosing by direction past the ring, going back from a group's ring.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Dilim.App.Controls;
using Dilim.App.ViewModels;

namespace Dilim.App.Tests;

[Collection("Motion")]
public class PieSpeedTests
{
    private static DonutItem Item(string label) => new() { Id = label, Label = label, IsEnabled = true };

    private static (DonutMenu Donut, Window Window) Open()
    {
        TestServices.EnsureInitialized("en");
        var donut = new DonutMenu
        {
            Items = [Item("PNG"), Item("JPG"), new DonutItem { Id = "group", Label = "More", Children = [Item("WEBP"), Item("PDF")] }, Item("BMP")],
            ShowKeyNumbers = true,
            DirectionalSelect = true,
        };
        var window = new Window { Content = donut, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (donut, window);
    }

    [AvaloniaFact]
    public void Number_keys_choose_slices_and_open_groups()
    {
        var (donut, window) = Open();
        int? chosen = null;
        (int, int)? sub = null;
        donut.SegmentInvoked += (_, i) => chosen = i;
        donut.SubSegmentInvoked += (_, s) => sub = s;

        Assert.Equal(-1, DonutMenu.NumberOf(Key.A));
        Assert.Equal(9, DonutMenu.NumberOf(Key.D0));
        Assert.True(donut.HandleKey(Key.D2));
        Assert.Equal(1, chosen);
        Assert.True(donut.HandleKey(Key.D3)); // the group opens its ring
        Assert.NotNull(donut.ExpandedChildren);
        Assert.True(donut.HandleKey(Key.NumPad2));
        Assert.Equal((2, 1), sub);
        Assert.True(donut.HandleKey(Key.D9)); // no ninth slice: taken, nothing happens
        window.Close();
    }

    [AvaloniaFact]
    public void Past_the_ring_the_direction_chooses_and_the_centre_goes_back()
    {
        var (donut, window) = Open();
        var center = new Point(donut.Bounds.Width / 2, donut.Bounds.Height / 2);
        var far = DonutGeometry.PointAt(center, donut.OuterRadius + 30, DonutGeometry.MidAngle(3, 4));
        var hit = donut.HitTest(far);
        Assert.Equal((DonutHitKind.Segment, 3), (hit.Kind, hit.Index));

        donut.DirectionalSelect = false;
        Assert.Equal(DonutHitKind.Outside, donut.HitTest(far).Kind);

        donut.HandleKey(Key.D3);
        Assert.NotNull(donut.ExpandedChildren);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Assert.Null(donut.ExpandedChildren);
        window.Close();
    }
}
