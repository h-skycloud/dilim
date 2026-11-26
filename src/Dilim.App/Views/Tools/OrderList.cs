using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Dilim.App.Views.Tools;

/// <summary>
/// Files in the order a tool uses them: a number, a small picture, the name and ↑ / ↓. The order starts as the files
/// were selected. Alt+↑ / Alt+↓ move the focused row.
/// </summary>
public sealed class OrderList : StackPanel
{
    private readonly List<string> _paths;
    private readonly Func<string, CancellationToken, Task<byte[]?>>? _thumbnail;
    private readonly Geometry? _fallback;
    private readonly Dictionary<string, Bitmap> _pictures = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _loading = new();
    private readonly string _up, _down;
    private readonly bool _portrait;

    public OrderList(IEnumerable<string> paths, Func<string, CancellationToken, Task<byte[]?>>? thumbnail, Geometry? fallback,
        string upText, string downText, bool portrait = false)
    {
        _portrait = portrait;
        _paths = [.. paths];
        _thumbnail = thumbnail;
        _fallback = fallback;
        (_up, _down) = (upText, downText);
        Spacing = 8;
        Rebuild();
        DetachedFromVisualTree += (_, _) => _loading.Cancel();
        _ = LoadThumbnailsAsync();
    }

    public IReadOnlyList<string> Paths => _paths;

    public event Action? Changed;

    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _paths.Count || to >= _paths.Count) return;
        (_paths[from], _paths[to]) = (_paths[to], _paths[from]);
        Rebuild();
        if (Children[to] is Control row) row.Focus();
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        Children.Clear();
        for (var i = 0; i < _paths.Count; i++) Children.Add(Row(i));
    }

    private Control Row(int index)
    {
        var path = _paths[index];
        var number = new TextBlock { Text = (index + 1).ToString(), Width = 24, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        number.Classes.Add("muted");

        Control picture = _pictures.TryGetValue(path, out var bitmap)
            ? new Image { Source = bitmap, Stretch = Stretch.Uniform }
            : new PathIcon { Data = _fallback, Width = 20, Height = 20 };
        var frame = new Border { Width = _portrait ? 40 : 64, Height = _portrait ? 52 : 44, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = picture, Margin = new Thickness(8, 0, 12, 0) };
        frame.Classes.Add("order-picture");

        var name = new TextBlock { Text = Path.GetFileName(path), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, path);

        var up = Arrow("Icon.ArrowUp", _up, index > 0, () => Move(index, index - 1));
        var down = Arrow("Icon.ArrowDown", _down, index < _paths.Count - 1, () => Move(index, index + 1));
        var arrows = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { up, down } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        grid.Children.Add(number);
        Grid.SetColumn(frame, 1); grid.Children.Add(frame);
        Grid.SetColumn(name, 2); grid.Children.Add(name);
        Grid.SetColumn(arrows, 3); grid.Children.Add(arrows);
        var row = new Border { Child = grid, Focusable = true };
        row.Classes.Add("order-row");
        row.KeyDown += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
            if (e.Key == Key.Up) { Move(index, index - 1); e.Handled = true; }
            else if (e.Key == Key.Down) { Move(index, index + 1); e.Handled = true; }
        };
        return row;
    }

    private Button Arrow(string icon, string tip, bool enabled, Action move)
    {
        var button = new Button
        {
            Content = new PathIcon { Data = Application.Current?.TryFindResource(icon, out var g) == true ? g as Geometry : null, Width = 14, Height = 14 },
            IsEnabled = enabled,
        };
        button.Classes.Add("order-arrow");
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => move();
        return button;
    }

    private async Task LoadThumbnailsAsync()
    {
        if (_thumbnail is null) return;
        foreach (var path in _paths.ToArray())
        {
            try
            {
                var png = await _thumbnail(path, _loading.Token);
                if (png is null) continue;
                using var stream = new MemoryStream(png);
                _pictures[path] = new Bitmap(stream);
                Rebuild();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* no picture: the icon stays */ }
        }
    }
}
