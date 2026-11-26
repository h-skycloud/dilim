using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Dilim.App.Services;
using Dilim.App.Views.Tools;
using Dilim.Core.Localization;
using Dilim.Core.Platform;
using Dilim.Core.Shelf;

namespace Dilim.App.Views;

/// <summary>
/// The shelf (Raf): a small always-on-top window that keeps references to files and folders between sessions. Files are
/// dropped on it from Explorer or sent from the pie, and dragged out of it into any application. It never copies, moves
/// or deletes anything itself; a file moved away by a drag simply leaves the shelf.
/// </summary>
/// <remarks>
/// Rows are plain borders, not buttons: a Button handles PointerPressed itself and captures the pointer, so a drag could
/// never start from it. Here the press selects, a small movement starts the native drag of the whole selection, and a
/// double-click opens.
/// </remarks>
public sealed class ShelfWindow : Window
{
    private const double DragThreshold = 6;

    private readonly ShelfList _list;
    private readonly ILocalizer _loc;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly Border _drop = new();
    private readonly Control _empty;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _count = new();
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ToggleButton _pin = new();
    private readonly Button _add = new(), _clear = new(), _convert = new();
    private readonly Border _handle = new();
    private readonly TextBlock _handleText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly HashSet<string> _selected = new(FileSystemPaths.Comparer);
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _thumbs = new(FileSystemPaths.Comparer);
    private readonly Button _sets = new();
    private readonly TextBox _newSet = new() { IsVisible = false, Margin = new Thickness(0, 0, 0, 10) };
    private string? _anchor;
    private (string Path, Point Start, bool WasSelected, PointerPressedEventArgs Event)? _press;
    private bool _draggingOut;
    private bool _positioned;

    public ShelfWindow(ILocalizer loc, ShelfList list)
    {
        _loc = loc;
        _list = list;
        Width = 340; Height = 480; MinWidth = 280; MinHeight = 260;
        Topmost = true;
        Focusable = true;
        Motion.Track(this);

        // Header: title and count, then pin, add and clear.
        _title.Classes.Add("h2");
        _count.Classes.Add("caption");
        _pin.Classes.Add("shelf-pin");
        _pin.IsChecked = true;
        _pin.Content = Icon("Icon.Pin", 16);
        _pin.IsCheckedChanged += (_, _) => Topmost = _pin.IsChecked == true;
        foreach (var (button, icon) in new[] { (_add, "Icon.Plus"), (_clear, "Icon.Delete") })
        {
            button.Classes.Add("shelf-tool");
            button.Content = Icon(icon, 16);
        }
        _add.Click += async (_, _) => await PickAsync();
        _clear.Click += (_, _) => { _selected.Clear(); _list.Clear(); };
        var close = new Button { Content = Icon("Icon.Close", 11) };
        close.Classes.Add("tool-close"); close.Classes.Add("small");
        close.Margin = new Thickness(6, 0, 0, 0);
        close.Click += (_, _) => Hide();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), Margin = new Thickness(4, 0, 0, 12), Background = Brushes.Transparent };
        _sets.Classes.Add("shelf-sets");
        _sets.Content = new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _title, Icon("Icon.ArrowDown", 10).Also(i => i.VerticalAlignment = VerticalAlignment.Center) } },
                _count,
            },
        };
        _sets.Click += (_, _) => SetsMenu().Open(_sets);
        _newSet.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && ShelfList.CleanSetName(_newSet.Text) is { } name) { _newSet.IsVisible = false; _selected.Clear(); _list.Switch(name); e.Handled = true; }
            else if (e.Key == Key.Escape) { _newSet.IsVisible = false; e.Handled = true; }
        };
        _newSet.LostFocus += (_, _) => _newSet.IsVisible = false;
        header.Children.Add(_sets);
        Place(header, _pin, 1); Place(header, _add, 2); Place(header, _clear, 3); Place(header, close, 4);
        // No system title bar: the header moves the card.
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null
                && (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is null)
                BeginMoveDrag(e);
        };

        // Body: the drop surface holding the rows, or an empty state that says what the shelf is for.
        _scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _empty = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
        };
        _drop.Classes.Add("shelf-drop");
        _drop.Child = new Panel { Children = { _scroll, _empty } };
        DragDrop.SetAllowDrop(_drop, true);
        DragDrop.AddDragEnterHandler(_drop, OnDragOver);
        DragDrop.AddDragOverHandler(_drop, OnDragOver);
        DragDrop.AddDragLeaveHandler(_drop, (_, _) => _drop.Classes.Remove("over"));
        DragDrop.AddDropHandler(_drop, OnDrop);

        // Footer: drag everything out at once, or open the pie for the shelf's files.
        _handle.Classes.Add("shelf-handle");
        _handle.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icon("Icon.DragVertical", 14), _handleText } };
        // Native drags start from the press (Avalonia 12), after the pointer travelled a few pixels with it.
        (Point Start, PointerPressedEventArgs Event)? handlePress = null;
        _handle.PointerPressed += (_, e) => { if (e.GetCurrentPoint(_handle).Properties.IsLeftButtonPressed) handlePress = (e.GetPosition(_handle), e); };
        _handle.PointerReleased += (_, _) => handlePress = null;
        _handle.PointerMoved += async (_, e) =>
        {
            if (handlePress is not { } press || !Moved(press.Start, e.GetPosition(_handle))) return;
            handlePress = null;
            await DragOutAsync(press.Event, _selected.Count > 0 ? Ordered(_selected) : _list.Paths.ToArray());
        };
        _convert.Classes.Add("accent");
        _convert.Click += (_, _) => OpenPie(_convert, Targets());
        _hint.Classes.Add("caption");
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        Place(footer, _handle, 0); Place(footer, _convert, 2);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(16) };
        Place(root, header, 0, row: true); Place(root, _newSet, 1, row: true); Place(root, _drop, 2, row: true); Place(root, footer, 3, row: true);
        _hint.Margin = new Thickness(4, 10, 4, 0);
        Place(root, _hint, 4, row: true);
        var card = new Border { Child = root };
        card.Classes.Add("tool-card");
        Content = card;
        Tools.CardFrame.Dress(this);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        Opened += (_, _) => PlaceAtEdge();
        Activated += (_, _) => Refresh();
        _list.Changed += Refresh;
        loc.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Add(IEnumerable<string> files)
    {
        _list.Add(files);
        Refresh();
    }

    // ───────── Rendering ─────────

    private void Refresh()
    {
        Title = _loc["tool.float"];
        _title.Text = _list.Current.Length == 0 ? _loc["tool.float.short"] : _list.Current;
        _newSet.Watermark = _loc["tool.shelf.set_name"];
        ToolTip.SetTip(_sets, _loc["tool.shelf.sets"]);
        _count.Text = _loc.Format("tool.shelf.count", _list.Count);
        _hint.Text = _loc["tool.shelf.hint"];
        _handleText.Text = _selected.Count > 0 ? _loc.Format("tool.shelf.drag_selected", _selected.Count) : _loc["tool.shelf.drag_all"];
        _convert.Content = _loc["tool.shelf.convert"];
        ToolTip.SetTip(_pin, _loc["tool.shelf.pin"]);
        ToolTip.SetTip(_add, _loc["tool.shelf.add"]);
        ToolTip.SetTip(_clear, _loc["tool.shelf.clear"]);
        _selected.RemoveWhere(p => !_list.Contains(p));

        var hasItems = _list.Count > 0;
        _empty.IsVisible = !hasItems;
        _scroll.IsVisible = hasItems;
        _handle.IsEnabled = _convert.IsEnabled = _clear.IsEnabled = hasItems;
        _handle.Opacity = hasItems ? 1 : 0.5;
        if (_empty is StackPanel empty)
        {
            empty.Children.Clear();
            var icon = Icon("Icon.Shelf", 34);
            icon.Foreground = (IBrush?)this.FindResource("AccentBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(icon);
            empty.Children.Add(new TextBlock { Text = _loc["tool.shelf.empty"], FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            var sub = new TextBlock { Text = _loc["tool.shelf.empty_hint"], TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 220 };
            sub.Classes.Add("muted");
            empty.Children.Add(sub);
        }

        _rows.Children.Clear();
        foreach (var path in _list.Paths) _rows.Children.Add(Row(path));
    }

    private Border Row(string path)
    {
        var isFolder = Directory.Exists(path);
        var exists = isFolder || File.Exists(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;

        var badge = new Border();
        badge.Classes.Add("shelf-badge");
        var extension = Path.GetExtension(path).TrimStart('.');
        badge.Child = isFolder || extension.Length == 0 || extension.Length > 5
            ? Icon(isFolder ? "Icon.Folder" : "Icon.FileOutline", 18)
            : new TextBlock { Text = extension.ToUpperInvariant() };
        if (exists && !isFolder) ShowThumbnail(path, badge);

        var title = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock
        {
            Text = exists ? Path.GetDirectoryName(path) ?? path : _loc["tool.shelf.missing"],
            TextTrimming = TextTrimming.PathSegmentEllipsis,
            FontSize = 11,
        };
        detail.Classes.Add(exists ? "muted" : "danger");
        var text = new StackPanel { Spacing = 1, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };

        var remove = new Button { Content = Icon("Icon.Close", 12) };
        remove.Classes.Add("shelf-remove");
        ToolTip.SetTip(remove, _loc["tool.shelf.remove"]);
        remove.Click += (_, e) => { e.Handled = true; _selected.Remove(path); _list.Remove(path); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Place(grid, badge, 0); Place(grid, text, 1); Place(grid, remove, 2);

        var row = new Border { Child = grid, Tag = path };
        row.Classes.Add("shelf-item");
        row.Classes.Set("selected", _selected.Contains(path));
        row.Classes.Set("missing", !exists);
        ToolTip.SetTip(row, path);
        row.ContextMenu = Menu(path);
        row.PointerPressed += (_, e) => OnRowPressed(row, path, e);
        row.PointerMoved += async (_, e) => await OnRowMoved(row, e);
        row.PointerReleased += (_, e) => OnRowReleased(path, e);
        return row;
    }

    /// <summary>A small picture of an image, the first page of a PDF or a frame of a video, in place of its badge.</summary>
    private void ShowThumbnail(string path, Border badge)
    {
        if (_thumbs.TryGetValue(path, out var known))
        {
            if (known is not null) Picture(badge, known);
            return;
        }
        var category = Dilim.Core.Profiles.ProfileSelector.DetectFormats([path]).FirstOrDefault()?.Category;
        if (category is not (Dilim.Core.Formats.FormatCategory.Image or Dilim.Core.Formats.FormatCategory.Pdf or Dilim.Core.Formats.FormatCategory.Video))
            return;
        _thumbs[path] = null;
        _ = Load();

        async Task Load()
        {
            try
            {
                var png = category switch
                {
                    Dilim.Core.Formats.FormatCategory.Image => await Dilim.Engines.Tools.ToolPreviews.ImageAsync(path, 96),
                    Dilim.Core.Formats.FormatCategory.Pdf => await Dilim.Engines.Tools.ToolPreviews.PdfPageAsync(path, 0, 72),
                    _ => await Dilim.Engines.Tools.ToolPreviews.VideoFrameAsync(path, 0.5, 96),
                };
                if (png is null) return;
                using var stream = new MemoryStream(png);
                var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                _thumbs[path] = bitmap;
                Picture(badge, bitmap);
            }
            catch (Exception) { /* the badge stays */ }
        }
    }

    private static void Picture(Border badge, Avalonia.Media.Imaging.Bitmap bitmap)
    {
        badge.Classes.Add("picture");
        badge.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
    }

    /// <summary>The main shelf and the named sets, a new set and deleting the one shown.</summary>
    private ContextMenu SetsMenu()
    {
        var menu = new ContextMenu();
        foreach (var name in _list.Sets)
        {
            var item = new MenuItem { Header = name.Length == 0 ? _loc["tool.float.short"] : name, Icon = name == _list.Current ? Icon("Icon.Check", 12) : null };
            var target = name;
            item.Click += (_, _) => { _selected.Clear(); _list.Switch(target); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var create = new MenuItem { Header = _loc["tool.shelf.new_set"], Icon = Icon("Icon.Plus", 12) };
        create.Click += (_, _) => { _newSet.Text = ""; _newSet.IsVisible = true; _newSet.Focus(); };
        menu.Items.Add(create);
        if (_list.Current.Length > 0)
        {
            var delete = new MenuItem { Header = _loc.Format("tool.shelf.delete_set", _list.Current), Icon = Icon("Icon.Delete", 12) };
            delete.Click += (_, _) => { _selected.Clear(); _list.DeleteSet(_list.Current); };
            menu.Items.Add(delete);
        }
        return menu;
    }

    private ContextMenu Menu(string path)
    {
        MenuItem Item(string key, string icon, Action action)
        {
            var item = new MenuItem { Header = _loc[key], Icon = Icon(icon, 14) };
            item.Click += (_, _) => action();
            return item;
        }
        IReadOnlyList<string> Scope() => _selected.Contains(path) && _selected.Count > 1 ? Ordered(_selected) : [path];
        return new ContextMenu
        {
            Items =
            {
                Item("tool.shelf.open", "Icon.ChevronRight", () => Open(path)),
                Item("tool.shelf.reveal", "Icon.Folder", () => Reveal(path)),
                Item("tool.shelf.copy_path", "Icon.Copy", async () => await CopyPathsAsync(Scope())),
                Item("tool.shelf.convert", "Icon.Donut", () => OpenPie(null, Scope())),
                new Separator(),
                Item("tool.shelf.remove", "Icon.Close", () => { var scope = Scope(); _list.RemoveAll(scope); foreach (var p in scope) _selected.Remove(p); }),
            },
        };
    }

    // ───────── Selection and drag out ─────────

    private void OnRowPressed(Border row, string path, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(row);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!_selected.Contains(path)) Select(path, KeyModifiers.None);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();
        if (e.ClickCount == 2)
        {
            _press = null;
            Open(path);
            e.Handled = true;
            return;
        }
        var wasSelected = _selected.Contains(path);
        // A press on a selected row keeps the selection, so several rows can be dragged together.
        if (!wasSelected || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            Select(path, e.KeyModifiers);
        _press = (path, e.GetPosition(this), wasSelected, e);
        e.Handled = true;
    }

    private async Task OnRowMoved(Border row, PointerEventArgs e)
    {
        if (_press is not { } press || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || !Moved(press.Start, e.GetPosition(this)))
            return;
        _press = null;
        if (!_selected.Contains(press.Path)) Select(press.Path, KeyModifiers.None);
        await DragOutAsync(press.Event, Ordered(_selected));
    }

    private void OnRowReleased(string path, PointerReleasedEventArgs e)
    {
        // A plain click on a row that was part of a multi-selection narrows the selection to it.
        if (_press is { WasSelected: true } press && press.Path == path && e.KeyModifiers == KeyModifiers.None && _selected.Count > 1)
            Select(path, KeyModifiers.None);
        _press = null;
    }

    private void Select(string path, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Shift) && _anchor is not null && _list.Contains(_anchor))
        {
            var paths = _list.Paths.ToList();
            int a = paths.FindIndex(p => FileSystemPaths.Comparer.Equals(p, _anchor)), b = paths.FindIndex(p => FileSystemPaths.Comparer.Equals(p, path));
            if (!modifiers.HasFlag(KeyModifiers.Control)) _selected.Clear();
            for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) _selected.Add(paths[i]);
        }
        else if (modifiers.HasFlag(KeyModifiers.Control))
        {
            if (!_selected.Remove(path)) _selected.Add(path);
            _anchor = path;
        }
        else
        {
            _selected.Clear();
            _selected.Add(path);
            _anchor = path;
        }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        foreach (var row in _rows.Children.OfType<Border>())
            row.Classes.Set("selected", row.Tag is string p && _selected.Contains(p));
        _handleText.Text = _selected.Count > 0 ? _loc.Format("tool.shelf.drag_selected", _selected.Count) : _loc["tool.shelf.drag_all"];
    }

    private async Task DragOutAsync(PointerPressedEventArgs trigger, IReadOnlyList<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0) return;
        var data = new DataTransfer();
        foreach (var path in existing)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await StorageProvider.TryGetFolderFromPathAsync(path)
                : await StorageProvider.TryGetFileFromPathAsync(path);
            if (item is not null) data.Add(DataTransferItem.CreateFile(item));
        }
        if (data.Items.Count == 0) return;
        _draggingOut = true;
        // Shift+drag: the pie opens under the pointer, and the files go straight onto one of its slices.
        var toPie = trigger.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (toPie)
        {
            var at = this.PointToScreen(trigger.GetPosition(this));
            AppHost.Get<RadialController>().ShowForDrag(at.X, at.Y);
        }
        try
        {
            await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            if (toPie) AppHost.Get<RadialController>().OnDragGestureEnded();
            _draggingOut = false;
            _drop.Classes.Remove("over");
        }
        // The target may have moved the files away (Shift+drag in Explorer): those references are gone now.
        var gone = existing.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToArray();
        if (gone.Length > 0) _list.RemoveAll(gone);
        else Refresh();
    }

    // ───────── Drop in ─────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_draggingOut || !DraggedFiles.Offered(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        _drop.Classes.Add("over");
        e.DragEffects = DragDropEffects.Link | DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _drop.Classes.Remove("over");
        if (_draggingOut) return;
        var files = DraggedFiles.Local(e.DataTransfer);
        e.DragEffects = files.Count > 0 ? DragDropEffects.Link : DragDropEffects.None;
        if (files.Count > 0) _list.Add(files);
        e.Handled = true;
    }

    private async Task PickAsync()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = _loc["tool.shelf.add"] });
        _list.Add(picked.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    // ───────── Actions ─────────

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Delete or Key.Back when _selected.Count > 0:
                _list.RemoveAll(_selected.ToArray());
                _selected.Clear();
                break;
            case Key.A when ctrl:
                foreach (var p in _list.Paths) _selected.Add(p);
                UpdateSelection();
                break;
            case Key.C when ctrl && _selected.Count > 0:
                _ = CopyPathsAsync(Ordered(_selected));
                break;
            case Key.Enter when _selected.Count > 0:
                foreach (var p in Ordered(_selected)) Open(p);
                break;
            case Key.Up or Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Alt) && _selected.Count == 1:
                var paths = _list.Paths.ToList();
                var from = paths.FindIndex(p => _selected.Contains(p));
                _list.Move(from, from + (e.Key == Key.Down ? 1 : -1));
                break;
            case Key.Escape:
                if (_selected.Count > 0) { _selected.Clear(); UpdateSelection(); }
                else Hide();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private IReadOnlyList<string> Targets() => _selected.Count > 0 ? Ordered(_selected) : _list.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();

    private IReadOnlyList<string> Ordered(IEnumerable<string> set)
    {
        var lookup = new HashSet<string>(set, FileSystemPaths.Comparer);
        return _list.Paths.Where(lookup.Contains).ToArray();
    }

    private void OpenPie(Control? at, IReadOnlyList<string> files)
    {
        files = files.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (files.Count == 0) return;
        var anchor = at ?? (Control)_drop;
        var point = anchor.PointToScreen(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2));
        AppHost.Get<RadialController>().ShowForFiles(point.X, point.Y, files);
    }

    private static void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { /* No associated application: nothing to open it with. */ }
    }

    private static void Reveal(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        AppHost.Get<IPlatformServices>().RevealInFileManager(path);
    }

    private async Task CopyPathsAsync(IReadOnlyList<string> paths)
    {
        if (Clipboard is not { } clipboard || paths.Count == 0) return;
        var text = new DataTransfer();
        text.Add(DataTransferItem.CreateText(string.Join(Environment.NewLine, paths)));
        await clipboard.SetDataAsync(text);
    }

    // ───────── Helpers ─────────

    private void PlaceAtEdge()
    {
        if (_positioned || (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;
        _positioned = true;
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.Right - (int)(Width * screen.Scaling) - 24, area.Y + 80);
    }

    private static bool Moved(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) >= DragThreshold;

    private PathIcon Icon(string key, double size) => new()
    {
        Data = this.TryFindResource(key, out var value) && value is Geometry geometry ? geometry
            : Application.Current?.TryFindResource(key, out var app) == true && app is Geometry g ? g : null,
        Width = size,
        Height = size,
    };

    private static void Place(Grid grid, Control control, int index, bool row = false)
    {
        if (row) Grid.SetRow(control, index); else Grid.SetColumn(control, index);
        grid.Children.Add(control);
    }
}
