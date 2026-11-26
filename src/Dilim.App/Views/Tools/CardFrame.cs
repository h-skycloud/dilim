using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.VisualTree;
using Dilim.App.Services;

namespace Dilim.App.Views.Tools;

/// <summary>
/// The card every Dilim window shares with the tools (<see cref="ToolShell"/>): a round close button and a centred
/// title on top, the content in the middle and an optional footer for the window's buttons. Put it as a window's
/// content and the window loses its system title bar: it becomes one soft card that is dragged by its header and
/// closed with Escape.
/// </summary>
public sealed class CardFrame : Border
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<CardFrame, string?>(nameof(Title));

    private readonly TextBlock _title = new();
    private readonly ContentControl _body = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border _footer = new() { IsVisible = false };
    private readonly Grid _grid = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
    private bool _scrolls = true;

    public CardFrame()
    {
        Classes.Add("tool-card");
        var close = new Button { Content = new PathIcon { Data = Resource("Icon.Close"), Width = 14, Height = 14 } };
        close.Classes.Add("tool-close");
        close.Click += (_, _) => (TopLevel.GetTopLevel(this) as Window)?.Close();
        if (Application.Current?.TryFindResource("tool.shell.close", out var tip) == true) ToolTip.SetTip(close, tip);
        _title.Classes.Add("tool-title");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 64 };
        header.Children.Add(close);
        Grid.SetColumn(_title, 1); header.Children.Add(_title);
        var spacer = new Border { Width = 36 };
        Grid.SetColumn(spacer, 2); header.Children.Add(spacer);
        var headerBar = new Border { Child = header, Background = Brushes.Transparent, Padding = new Thickness(20, 0) };
        headerBar.Classes.Add("tool-header");
        headerBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(headerBar).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null)
                (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
        };
        _body.Margin = new Thickness(20, 16);
        _scroll.Content = _body;
        _footer.Classes.Add("tool-footer");
        _grid.Children.Add(headerBar);
        Grid.SetRow(_scroll, 1); _grid.Children.Add(_scroll);
        Grid.SetRow(_footer, 2); _grid.Children.Add(_footer);
        Child = _grid;
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>The window's content between header and footer.</summary>
    [Content]
    public Control? Body { get => _body.Content as Control; set => _body.Content = value; }

    /// <summary>The buttons at the bottom (Cancel / Save…), laid out by the window.</summary>
    public Control? Footer
    {
        get => _footer.Child;
        set
        {
            _footer.Child = value;
            _footer.IsVisible = value is not null;
            if (value is not null) value.Margin = new Thickness(20, 14);
        }
    }

    /// <summary>False for content that scrolls on its own (a list that fills the card).</summary>
    public bool Scrolls
    {
        get => _scrolls;
        set
        {
            if (_scrolls == value) return;
            _scrolls = value;
            _grid.Children.Remove(_scroll);
            _grid.Children.Remove(_body);
            _scroll.Content = null;
            Control middle = value ? _scroll : _body;
            if (value) _scroll.Content = _body;
            Grid.SetRow(middle, 1);
            _grid.Children.Insert(1, middle);
        }
    }

    /// <summary>The space around the content (20,16 by default).</summary>
    public Thickness BodyMargin { get => _body.Margin; set => _body.Margin = value; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty) _title.Text = Title;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window) Dress(window);
    }

    private static readonly AttachedProperty<bool> DressedProperty = AvaloniaProperty.RegisterAttached<CardFrame, Window, bool>("Dressed");

    /// <summary>The window around the card: no system frame, see-through corners, Escape closes it.</summary>
    internal static void Dress(Window window)
    {
        if (window.GetValue(DressedProperty)) return;
        window.SetValue(DressedProperty, true);
        window.WindowDecorations = WindowDecorations.None;
        window.Background = Brushes.Transparent;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled) { window.Close(); e.Handled = true; }
        };
    }

    private static Geometry? Resource(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null;
}
