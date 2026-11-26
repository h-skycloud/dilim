using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dilim.App.Services;
using Dilim.Core.Localization;
using Dilim.Core.Platform;

namespace Dilim.App.Views.Tools;

/// <summary>
/// The frame every tool window shares: a round close button and a centred title on top, the tool's content in the
/// middle, and a footer with an optional secondary action on the left and the tool's main action on the right. While
/// the tool runs the footer shows progress and the main button cancels; afterwards it says what was saved and offers to
/// show it. The window has no system title bar: it is one soft card that can be dragged by its header.
/// </summary>
public abstract class ToolShell : Window
{
    protected readonly ILocalizer Loc;
    private readonly ContentControl _body = new();
    private readonly Button _close = new(), _primary = new(), _secondary = new(), _show = new();
    private readonly TextBlock _title = new(), _status = new();
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 4, MinWidth = 0, IsVisible = false };
    private CancellationTokenSource? _running;
    private IReadOnlyList<string> _outputs = [];
    private string _primaryText = "";

    protected ToolShell(string titleKey, ILocalizer loc, double width, double height)
    {
        Loc = loc;
        Title = loc[titleKey];
        Width = width; Height = height; MinWidth = 380; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Motion.Track(this);

        // Header: drag anywhere on it; the close button stays a button.
        _close.Classes.Add("tool-close");
        _close.Content = new PathIcon { Data = Geometry("Icon.Close"), Width = 14, Height = 14 };
        _close.Click += (_, _) => Close();
        ToolTip.SetTip(_close, loc["tool.shell.close"]);
        _title.Text = Title;
        _title.Classes.Add("tool-title");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 64 };
        header.Children.Add(_close);
        Grid.SetColumn(_title, 1); header.Children.Add(_title);
        header.Children.Add(new Border { Width = 36 }.Also(b => Grid.SetColumn(b, 2)));
        var headerBar = new Border { Child = header, Background = Brushes.Transparent, Padding = new Thickness(20, 0) };
        headerBar.Classes.Add("tool-header");
        headerBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(headerBar).Properties.IsLeftButtonPressed && e.Source is not Button && !IsWithinButton(e.Source))
                BeginMoveDrag(e);
        };

        // Footer: [secondary]  status / progress  [show] [primary]
        _secondary.IsVisible = false;
        _primary.Classes.Add("accent");
        _primary.Click += async (_, _) => await OnPrimaryAsync();
        _show.Content = loc["tool.shell.show"];
        _show.IsVisible = false;
        _show.Click += (_, _) => ShowOutputs();
        _status.Classes.Add("caption");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.VerticalAlignment = VerticalAlignment.Center;
        var middle = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _status, _bar } };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(20, 14) };
        footer.Children.Add(_secondary);
        Grid.SetColumn(middle, 1); footer.Children.Add(middle);
        _show.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_show, 2); footer.Children.Add(_show);
        Grid.SetColumn(_primary, 3); footer.Children.Add(_primary);
        var footerBar = new Border { Child = footer };
        footerBar.Classes.Add("tool-footer");

        _body.Margin = new Thickness(20, 16);
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(headerBar);
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        Grid.SetRow(footerBar, 2); grid.Children.Add(footerBar);
        var card = new Border { Child = grid };
        card.Classes.Add("tool-card");
        Content = card;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _running is null) { Close(); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            // Closing while it runs cancels first; the window closes once the work has stopped.
            if (_running is null) return;
            _running.Cancel();
            e.Cancel = true;
        };
    }

    /// <summary>The tool's content between header and footer.</summary>
    protected Control Body { get => (Control)_body.Content!; set => _body.Content = value; }

    /// <summary>Text of the main action ("Merge PDFs").</summary>
    protected string PrimaryText
    {
        get => _primaryText;
        set { _primaryText = value; if (_running is null) _primary.Content = value; }
    }

    protected bool PrimaryEnabled { get => _primary.IsEnabled; set => _primary.IsEnabled = value; }

    /// <summary>An optional action on the left of the footer, such as "Reset".</summary>
    protected void SetSecondary(string text, Action action)
    {
        _secondary.Content = text;
        _secondary.IsVisible = true;
        _secondary.Click += (_, _) => { if (_running is null) action(); };
    }

    /// <summary>A short note in the footer (what will happen, or why the main action is off).</summary>
    protected string Status { get => _status.Text ?? ""; set => _status.Text = value; }

    /// <summary>What the footer says when the tool finished without saving a file ("Copied to the clipboard").</summary>
    protected string? DoneText { get; set; }

    /// <summary>Does the tool's work. Returns the files it saved.</summary>
    protected abstract Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct);

    /// <summary>The tool's own controls stop taking input while it runs.</summary>
    protected virtual void OnRunningChanged(bool running) => _body.IsEnabled = !running;

    /// <summary>Runs the tool as the main button does (a second action, such as "Remove All", that also saves).</summary>
    protected Task StartAsync() => _running is null ? OnPrimaryAsync() : Task.CompletedTask;

    private async Task OnPrimaryAsync()
    {
        if (_running is not null)
        {
            _running.Cancel();
            return;
        }
        _running = new CancellationTokenSource();
        _primary.Content = Loc["toast.cancel"];
        _primary.Classes.Remove("accent");
        _secondary.IsEnabled = false;
        _show.IsVisible = false;
        _bar.IsVisible = true;
        _bar.IsIndeterminate = true;
        _status.Text = Loc["tool.running"];
        _status.Classes.Remove("danger");
        OnRunningChanged(true);
        try
        {
            _outputs = await RunAsync(new Progress<double>(p => { _bar.IsIndeterminate = false; _bar.Value = p; }), _running.Token);
            _status.Text = _outputs.Count switch
            {
                0 => DoneText ?? Loc["tool.done"],
                1 => Loc.Format("tool.shell.saved_one", Path.GetFileName(_outputs[0])),
                _ => Loc.Format("tool.shell.saved_many", _outputs.Count),
            };
            _show.IsVisible = _outputs.Count > 0;
        }
        catch (OperationCanceledException) { _status.Text = Loc["tool.cancelled"]; }
        catch (Exception ex)
        {
            _status.Text = ex.Message.StartsWith("tool.", StringComparison.Ordinal) ? Loc[ex.Message] : ex.Message;
            _status.Classes.Add("danger");
        }
        finally
        {
            var wasClosing = _running.IsCancellationRequested && !IsActive;
            _running.Dispose();
            _running = null;
            _bar.IsVisible = false;
            _primary.Content = _primaryText;
            _primary.Classes.Add("accent");
            _secondary.IsEnabled = true;
            OnRunningChanged(false);
            if (wasClosing && !IsVisible) Close();
        }
    }

    /// <summary>Shows the saved files in their folder (one file selected).</summary>
    private void ShowOutputs()
    {
        if (_outputs.Count == 0) return;
        try
        {
            if (_outputs.Count == 1 && File.Exists(_outputs[0])) AppHost.Get<IPlatformServices>().RevealInFileManager(_outputs[0]);
            else if (Path.GetDirectoryName(_outputs[0]) is { } folder)
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception) { /* the folder is gone: nothing to show */ }
    }

    protected Geometry? Geometry(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null;

    private static bool IsWithinButton(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<Button>() is not null;

    /// <summary>
    /// Where a tool saves what it made from <paramref name="source"/>: the "Dilim" folder next to it, under the same
    /// name with <paramref name="extension"/>, numbered when the name is taken. The original is never overwritten.
    /// </summary>
    protected static string OutputNextTo(string source, string extension)
    {
        var folder = Path.Combine(Path.GetDirectoryName(source)!, "Dilim");
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(source);
        var path = Path.Combine(folder, stem + extension);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){extension}");
        return path;
    }

    // ───────── Small builders the tools share ─────────

    /// <summary>A section label ("Document order").</summary>
    protected static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 10) };
        label.Classes.Add("tool-label");
        return label;
    }

    /// <summary>A setting on one line: its name on the left, the control on the right ("Style    Solid | Blur").</summary>
    protected static Grid Row(string label, Control control, double top = 14, bool stretch = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, top, 0, 0) };
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        name.Classes.Add("muted");
        grid.Children.Add(name);
        control.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>A preview (picture, video, waveform) in a rounded dark frame, as in every tool.</summary>
    protected static Border Framed(Control content) => new() { Child = content, CornerRadius = new CornerRadius(18), ClipToBounds = true };

    /// <summary>A thin rule between sections.</summary>
    protected static Border Rule() => new Border { Height = 1, Margin = new Thickness(0, 18, 0, 4) }.Also(b => b.Classes.Add("rule"));

    /// <summary>Turns a localized error ("tool.error.*") or exception into text for the footer.</summary>
    protected string Message(Exception ex) => ex.Message.StartsWith("tool.", StringComparison.Ordinal) || ex.Message.StartsWith("error.", StringComparison.Ordinal) ? Loc[ex.Message] : ex.Message;

    /// <summary>A muted note under a control.</summary>
    protected static TextBlock Note(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        note.Classes.Add("caption");
        return note;
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
