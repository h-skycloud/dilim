using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dilim.Core.Localization;
using Dilim.Engines.Pdf;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>
/// "Organize PDF": the pages of one PDF (or of several, one after another) as cards to drag into a new order, turn,
/// duplicate or remove; "Reset order" brings the original back. The result is a new PDF; the originals stay as they are.
/// </summary>
public sealed class PdfOrganizeTool : ToolShell
{
    private const double DragThreshold = 6;
    private const double ThumbWidth = 132, ThumbHeight = 172;

    /// <summary>One card: which page of which file, turned how far, with a key that stays with it through moves.</summary>
    private sealed record Card(int Key, string File, int Page, int Rotation);

    private readonly IReadOnlyList<string> _files;
    private readonly List<Card> _original = [];
    private List<Card> _cards = [];
    private int _nextKey;
    private int? _selected;
    private readonly Dictionary<(string, int), Bitmap> _thumbs = [];
    private readonly HashSet<(string, int)> _loading = [];
    private readonly WrapPanel _grid = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _count = new();
    private readonly Button _reset = new();
    private readonly CheckBox _sameWidth = new();
    private (int Index, Point Start, PointerPressedEventArgs Press)? _press;
    private bool _dragging;

    public PdfOrganizeTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.organizePDF", loc, 860, 760)
    {
        _files = files;
        PrimaryText = loc["tool.organize.run"];
        PrimaryEnabled = false;
        Body = Build();
        Focusable = true;
        KeyDown += OnKey;
        _ = LoadPagesAsync();
    }

    private Control Build()
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        _count.Classes.Add("muted");
        _count.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_count);
        _reset.Content = Loc["tool.organize.reset"];
        _reset.IsEnabled = false;
        _reset.Click += (_, _) => { _cards = _original.Select(c => c with { }).ToList(); _selected = null; Rebuild(); };
        Grid.SetColumn(_reset, 1); head.Children.Add(_reset);

        _grid.PointerMoved += OnPointerMoved;
        _grid.PointerReleased += OnPointerReleased;

        _sameWidth.Content = Loc["tool.organize.same_width"];
        var note = Note(Loc["tool.organize.same_width_note"]);
        note.Margin = new Thickness(28, 4, 0, 0);
        var options = new StackPanel { Margin = new Thickness(0, 18, 0, 0), Children = { new Border { Height = 1, Margin = new Thickness(0, 0, 0, 14) }.Also(b => b.Classes.Add("rule")), _sameWidth, note } };
        var keys = Note(Loc["tool.organize.keys"]);
        return new StackPanel { Children = { head, _grid, options, keys } };
    }

    private async Task LoadPagesAsync()
    {
        _count.Text = Loc["tool.pdfshrink.calculating"];
        try
        {
            foreach (var file in _files)
            {
                var count = await Task.Run(() => ToolPreviews.PdfPageCount(file));
                for (var page = 0; page < count; page++) _original.Add(new Card(_nextKey++, file, page, 0));
            }
        }
        catch (Exception)
        {
            Status = Loc["tool.organize.unreadable"];
        }
        _cards = _original.ToList();
        Rebuild();
    }

    // ───────── Cards ─────────

    private void Rebuild()
    {
        _grid.Children.Clear();
        for (var i = 0; i < _cards.Count; i++) _grid.Children.Add(CardView(i));
        _count.Text = Loc.Format("tool.organize.count", _cards.Count);
        _reset.IsEnabled = !_cards.Select(c => (c.File, c.Page, c.Rotation)).SequenceEqual(_original.Select(c => (c.File, c.Page, c.Rotation)));
        PrimaryEnabled = _cards.Count > 0;
        if (_cards.Count == 0) Status = Loc["tool.organize.empty"];
        else if (Status == Loc["tool.organize.empty"]) Status = "";
    }

    private Control CardView(int index)
    {
        var card = _cards[index];
        Control picture;
        if (_thumbs.TryGetValue((card.File, card.Page), out var bitmap))
            picture = new LayoutTransformControl
            {
                LayoutTransform = new RotateTransform(card.Rotation),
                Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxWidth = card.Rotation % 180 == 0 ? ThumbWidth : ThumbHeight, MaxHeight = card.Rotation % 180 == 0 ? ThumbHeight : ThumbWidth },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        else
        {
            picture = new PathIcon { Data = Geometry("Icon.FileOutline"), Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _ = LoadThumbAsync(card.File, card.Page);
        }
        var sheet = new Border { Width = ThumbWidth + 8, Height = ThumbHeight + 8, Child = picture };
        sheet.Classes.Add("page-sheet");

        var number = new Border { Child = new TextBlock { Text = (index + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        number.Classes.Add("page-number");

        var rotate = Action("Icon.RotateRight", Loc["tool.organize.rotate"], () => Rotate(index));
        var copy = Action("Icon.Copy", Loc["tool.organize.duplicate"], () => Duplicate(index));
        var remove = Action("Icon.Delete", Loc["tool.organize.remove"], () => Remove(index));
        copy.Classes.Add("hover-only"); remove.Classes.Add("hover-only");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6), Children = { remove, copy, rotate } };
        number.HorizontalAlignment = HorizontalAlignment.Left; number.VerticalAlignment = VerticalAlignment.Top; number.Margin = new Thickness(6);

        var label = new TextBlock
        {
            Text = _files.Count > 1 ? $"{Path.GetFileNameWithoutExtension(card.File)} · {card.Page + 1}" : Loc.Format("tool.organize.page", card.Page + 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            MaxWidth = ThumbWidth,
        };
        label.Classes.Add("caption");

        var view = new Border
        {
            Child = new StackPanel { Children = { new Panel { Children = { sheet, number, actions } }, label } },
            Margin = new Thickness(0, 0, 14, 14),
            Tag = index,
            Focusable = true,
        };
        view.Classes.Add("page-card");
        view.Classes.Set("selected", _selected == card.Key);
        view.PointerPressed += (_, e) => OnCardPressed(index, e);
        return view;
    }

    private Button Action(string icon, string tip, Action act)
    {
        var button = new Button { Content = new PathIcon { Data = Geometry(icon), Width = 14, Height = 14 } };
        button.Classes.Add("page-action");
        ToolTip.SetTip(button, tip);
        button.Click += (_, e) => { e.Handled = true; act(); };
        return button;
    }

    private async Task LoadThumbAsync(string file, int page)
    {
        if (!_loading.Add((file, page))) return;
        var png = await ToolPreviews.PdfPageAsync(file, page, 260);
        if (png is null) return;
        using var stream = new MemoryStream(png);
        _thumbs[(file, page)] = new Bitmap(stream);
        RequestRebuild();
    }

    private bool _rebuildPosted;

    /// <summary>
    /// Pictures arrive one by one: the grid is redrawn once for all that arrived meanwhile, and never in the middle of
    /// a drag (it would drop the card being dragged).
    /// </summary>
    private void RequestRebuild()
    {
        if (_rebuildPosted) return;
        _rebuildPosted = true;
        DispatcherTimer.RunOnce(() =>
        {
            _rebuildPosted = false;
            if (_press is not null) { RequestRebuild(); return; }
            Rebuild();
        }, TimeSpan.FromMilliseconds(120));
    }

    // ───────── Edits ─────────

    private void Rotate(int index)
    {
        _cards[index] = _cards[index] with { Rotation = (_cards[index].Rotation + 90) % 360 };
        _selected = _cards[index].Key;
        Rebuild();
    }

    private void Duplicate(int index)
    {
        var copy = _cards[index] with { Key = _nextKey++ };
        _cards.Insert(index + 1, copy);
        _selected = copy.Key;
        Rebuild();
    }

    private void Remove(int index)
    {
        _cards.RemoveAt(index);
        _selected = index < _cards.Count ? _cards[index].Key : _cards.Count > 0 ? _cards[^1].Key : null;
        Rebuild();
    }

    private void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _cards.Count || to >= _cards.Count) return;
        var card = _cards[from];
        _cards.RemoveAt(from);
        _cards.Insert(to, card);
        _selected = card.Key;
        Rebuild();
    }

    private int SelectedIndex() => _selected is { } key ? _cards.FindIndex(c => c.Key == key) : -1;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var index = SelectedIndex();
        if (index < 0) return;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Left when alt: Move(index, index - 1); break;
            case Key.Right when alt: Move(index, index + 1); break;
            case Key.Left: if (index > 0) { _selected = _cards[index - 1].Key; Rebuild(); } break;
            case Key.Right: if (index < _cards.Count - 1) { _selected = _cards[index + 1].Key; Rebuild(); } break;
            case Key.Delete or Key.Back: Remove(index); break;
            case Key.R when !ctrl: Rotate(index); break;
            case Key.D when ctrl: Duplicate(index); break;
            default: return;
        }
        e.Handled = true;
    }

    // ───────── Drag to reorder ─────────

    private void OnCardPressed(int index, PointerPressedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>() is not null) return;
        if (!e.GetCurrentPoint(_grid).Properties.IsLeftButtonPressed) return;
        _selected = _cards[index].Key;
        foreach (var child in _grid.Children) child.Classes.Set("selected", child.Tag is int i && i == index);
        _press = (index, e.GetPosition(_grid), e);
        _dragging = false;
        e.Pointer.Capture(_grid);
        Focus();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_press is not { } press) return;
        var at = e.GetPosition(_grid);
        if (!_dragging && Math.Abs(at.X - press.Start.X) + Math.Abs(at.Y - press.Start.Y) < DragThreshold) return;
        _dragging = true;
        if (_grid.Children[press.Index] is { } card)
        {
            card.ZIndex = 1;
            card.Opacity = 0.9;
            card.RenderTransform = new TranslateTransform(at.X - press.Start.X, at.Y - press.Start.Y);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_press is not { } press) return;
        _press = null;
        e.Pointer.Capture(null);
        if (!_dragging) return;
        _dragging = false;
        var at = e.GetPosition(_grid);
        var target = Nearest(at);
        if (_grid.Children[press.Index] is { } card) { card.RenderTransform = null; card.ZIndex = 0; card.Opacity = 1; }
        if (target != press.Index) Move(press.Index, target);
    }

    /// <summary>The card whose centre is nearest the point: where a dragged card lands.</summary>
    private int Nearest(Point point)
    {
        var best = 0;
        var distance = double.MaxValue;
        for (var i = 0; i < _grid.Children.Count; i++)
        {
            var bounds = _grid.Children[i].Bounds;
            var d = Math.Pow(bounds.Center.X - point.X, 2) + Math.Pow(bounds.Center.Y - point.Y, 2);
            if (d < distance) (best, distance) = (i, d);
        }
        return best;
    }

    // ───────── Saving ─────────

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var output = OutputNextTo(_files[0], ".pdf");
        var pages = _cards.Select(c => new PdfPageChoice(c.File, c.Page, c.Rotation)).ToList();
        await PdfOrganizer.SaveAsync(pages, _sameWidth.IsChecked == true, output, ct);
        return [output];
    }
}
