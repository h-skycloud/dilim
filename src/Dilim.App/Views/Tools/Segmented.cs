using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Dilim.App.Views.Tools;

/// <summary>A row of exclusive choices in one soft pill ("Solid | Blur", "Free | 1:1 | 16:9").</summary>
public sealed class Segmented : Border
{
    private readonly List<Button> _buttons = [];
    private int _selected;

    public Segmented(IReadOnlyList<string> labels, int selected = 0, bool stretch = false)
    {
        Classes.Add("seg");
        var grid = new Grid();
        for (var i = 0; i < labels.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(stretch ? GridLength.Star : GridLength.Auto));
            var index = i;
            var button = new Button { Content = labels[i], HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add("seg-item");
            button.Click += (_, _) => Selected = index;
            Grid.SetColumn(button, i);
            grid.Children.Add(button);
            _buttons.Add(button);
        }
        Child = grid;
        HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        _selected = -1;
        Selected = selected;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            _selected = value;
            for (var i = 0; i < _buttons.Count; i++) _buttons[i].Classes.Set("on", i == value);
            Changed?.Invoke(value);
        }
    }

    /// <summary>Selects without raising <see cref="Changed"/> (when the state comes from elsewhere).</summary>
    public void SetQuietly(int value)
    {
        _selected = value;
        for (var i = 0; i < _buttons.Count; i++) _buttons[i].Classes.Set("on", i == value);
    }

    public event Action<int>? Changed;
}
