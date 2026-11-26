using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dilim.App.Services;
using Dilim.Core.Formats;
using Dilim.Core.Localization;
using Dilim.Core.Profiles;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>
/// The selected files as soft rows: a small picture (or the kind's icon), the name and its size or folder. Long
/// selections show the first rows and how many more there are.
/// </summary>
public sealed class FileStack : StackPanel
{
    private const int Shown = 5;
    private readonly CancellationTokenSource _loading = new();

    public FileStack(IReadOnlyList<string> files, ILocalizer loc)
    {
        Spacing = 8;
        foreach (var path in files.Take(Shown)) Children.Add(Row(path));
        if (files.Count > Shown)
        {
            var more = new TextBlock { Text = loc.Format("tool.quick.more", files.Count - Shown), Margin = new Thickness(4, 2, 0, 0) };
            more.Classes.Add("caption");
            Children.Add(more);
        }
        DetachedFromVisualTree += (_, _) => _loading.Cancel();
    }

    private Control Row(string path)
    {
        var folder = Directory.Exists(path);
        var category = folder ? (FormatCategory?)null : ProfileSelector.DetectFormats([path]).FirstOrDefault()?.Category;
        var icon = new PathIcon
        {
            Data = Resource(folder ? "Icon.Folder" : category switch
            {
                FormatCategory.Image => "Icon.Image",
                FormatCategory.Video => "Icon.Video",
                FormatCategory.Audio => "Icon.Audio",
                _ => "Icon.FileOutline",
            }),
            Width = 18, Height = 18,
        };
        var picture = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = icon, Margin = new Thickness(0, 0, 12, 0) };
        picture.Classes.Add("order-picture");
        if (!folder) _ = LoadPictureAsync(path, category, picture);

        var name = new TextBlock { Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold };
        var detail = new TextBlock { Text = folder ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? path : Size(path) + "  ·  " + Path.GetDirectoryName(path), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        detail.Classes.Add("caption");
        ToolTip.SetTip(name, path);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(picture);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        var row = new Border { Child = grid };
        row.Classes.Add("order-row");
        return row;
    }

    private async Task LoadPictureAsync(string path, FormatCategory? category, Border frame)
    {
        try
        {
            var png = category switch
            {
                FormatCategory.Image => await ToolPreviews.ImageAsync(path, 120, _loading.Token),
                FormatCategory.Pdf => await ToolPreviews.PdfPageAsync(path, 0, 90, _loading.Token),
                FormatCategory.Video => await ToolPreviews.VideoFrameAsync(path, 0.5, 120, _loading.Token),
                _ => null,
            };
            if (png is null) return;
            using var stream = new MemoryStream(png);
            frame.Child = new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill };
        }
        catch (Exception) { /* no picture: the icon stays */ }
    }

    private static string Size(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB" : Math.Max(1, bytes / 1024) + " KB";
        }
        catch (Exception) { return ""; }
    }

    private static Geometry? Resource(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true ? value as Geometry : null;
}

/// <summary>
/// The tools and file actions with few settings, in the same card as every other tool: the files on top, the settings
/// the action really has (path form, destination, background colour…) below, the action in the footer.
/// </summary>
public sealed class QuickTool : ToolShell
{
    public static readonly string[] Ids = ["copyPath", "copy", "move", "terminal", "share", "frameImage", "muteVideo", "splitPDF", "createPDF", "extractArchive"];

    private readonly string _id;
    private readonly IReadOnlyList<string> _files;
    private readonly ToolRun _run;
    private Segmented? _form, _separator;
    private CheckBox? _quotes;
    private TextBlock? _preview, _destinationText;
    private string? _destination;
    private Swatches? _colors;

    public QuickTool(string id, IReadOnlyList<string> files, ILocalizer loc, ToolRun run)
        : base("tool." + id, loc, 560, Height(id, files.Count))
    {
        _id = id;
        _files = files;
        _run = run;
        var body = new StackPanel();
        var hint = new TextBlock { Text = loc["tool.hint." + id], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        hint.Classes.Add("muted");
        body.Children.Add(hint);
        body.Children.Add(Label(files.Count == 1 ? loc["tool.quick.file"] : loc.Format("tool.quick.files", files.Count)));
        body.Children.Add(new FileStack(files, loc));
        switch (id)
        {
            case "copyPath": CopyPathSettings(body); break;
            case "move": MoveSettings(body); break;
            case "terminal":
                body.Children.Add(Rule());
                body.Children.Add(Row(loc["tool.terminal.folder"], Caption(TerminalFolder(), trim: true), 14, stretch: true));
                break;
            case "frameImage": FrameSettings(body); break;
            case "splitPDF":
                body.Children.Add(Rule());
                body.Children.Add(Note(SplitNote()));
                break;
        }
        var footnote = id switch
        {
            "copyPath" or "copy" or "terminal" or "share" or "move" => null,
            _ => loc["tool.quick.where"],
        };
        if (footnote is not null) body.Children.Add(Note(footnote).Also(n => n.Margin = new Thickness(0, 18, 0, 0)));
        Body = body;
        PrimaryText = loc["tool." + id + ".run"];
        if (id == "move") PrimaryEnabled = false;
    }

    private static double Height(string id, int count)
    {
        var rows = Math.Min(count, 5) * 64 + (count > 5 ? 28 : 0);
        var extra = id switch { "copyPath" => 300, "move" or "frameImage" => 200, "terminal" or "splitPDF" => 120, _ => 90 };
        return Math.Clamp(260 + rows + extra, 380, 760);
    }

    private static TextBlock Caption(string text, bool trim = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = trim ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = trim ? TextTrimming.PathSegmentEllipsis : TextTrimming.None, HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(block, text);
        return block;
    }

    // ───────── Copy paths ─────────

    private void CopyPathSettings(StackPanel body)
    {
        body.Children.Add(Rule());
        _form = new Segmented([Loc["tool.copyPath.full"], Loc["tool.copyPath.name"]]);
        _separator = new Segmented([Loc["tool.copyPath.lines"], Loc["tool.copyPath.spaces"]]);
        _quotes = new CheckBox { Content = Loc["tool.copyPath.quotes"], Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(Row(Loc["tool.copyPath.form"], _form));
        body.Children.Add(Row(Loc["tool.copyPath.separator"], _separator));
        body.Children.Add(_quotes);
        _preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12 };
        var frame = new Border { Child = _preview, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(12), MaxHeight = 140, Margin = new Thickness(0, 8, 0, 0) };
        frame.Classes.Add("order-picture");
        body.Children.Add(Label(Loc["tool.copyPath.preview"]).Also(l => l.Margin = new Thickness(0, 18, 0, 0)));
        body.Children.Add(frame);
        _form.Changed += _ => UpdatePreview();
        _separator.Changed += _ => UpdatePreview();
        _quotes.IsCheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    /// <summary>The text "Copy paths" puts on the clipboard with the chosen form, separator and quotes.</summary>
    internal static string PathsText(IReadOnlyList<string> files, bool namesOnly, bool spaces, bool quotes)
    {
        var parts = files.Select(f => namesOnly ? Path.GetFileName(Path.TrimEndingDirectorySeparator(f)) : f)
            .Select(p => quotes ? "\"" + p + "\"" : p);
        return string.Join(spaces ? " " : Environment.NewLine, parts);
    }

    private string CurrentPaths() => PathsText(_files, _form?.Selected == 1, _separator?.Selected == 1, _quotes?.IsChecked == true);

    private void UpdatePreview()
    {
        if (_preview is null) return;
        var text = CurrentPaths();
        _preview.Text = text.Length > 600 ? text[..600] + "…" : text;
    }

    // ───────── Move ─────────

    private void MoveSettings(StackPanel body)
    {
        body.Children.Add(Rule());
        _destinationText = new TextBlock { Text = Loc["tool.move.none"], VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.PathSegmentEllipsis };
        _destinationText.Classes.Add("muted");
        var pick = new Button { Content = Loc["tool.move.pick"] };
        pick.Click += async (_, _) => await PickDestinationAsync();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        var name = new TextBlock { Text = Loc["tool.move.destination"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        name.Classes.Add("muted");
        grid.Children.Add(name);
        Grid.SetColumn(_destinationText, 1); grid.Children.Add(_destinationText);
        _destinationText.HorizontalAlignment = HorizontalAlignment.Right;
        _destinationText.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(pick, 2); grid.Children.Add(pick);
        body.Children.Add(grid);
    }

    private async Task PickDestinationAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc["tool.move.destination"], AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        SetDestination(path);
    }

    /// <summary>Where "Move" puts the files (the folder picker sets it; tests set it directly).</summary>
    internal void SetDestination(string path)
    {
        _destination = path;
        if (_destinationText is not null)
        {
            _destinationText.Text = path;
            _destinationText.Classes.Remove("muted");
            ToolTip.SetTip(_destinationText, path);
        }
        PrimaryEnabled = true;
        Status = "";
    }

    // ───────── Terminal, background, split ─────────

    private string TerminalFolder() => Directory.Exists(_files[0]) ? _files[0] : Path.GetDirectoryName(_files[0]) ?? _files[0];

    private void FrameSettings(StackPanel body)
    {
        body.Children.Add(Rule());
        _colors = new Swatches();
        _colors.Select("#FFFFFF", quiet: true);
        body.Children.Add(Row(Loc["tool.frameImage.color"], _colors));
    }

    private string SplitNote()
    {
        try
        {
            var pages = _files.Sum(ToolPreviews.PdfPageCount);
            return Loc.Format("tool.splitPDF.pages", pages);
        }
        catch (Exception) { return Loc["tool.hint.splitPDF"]; }
    }

    // ───────── Run ─────────

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        switch (_id)
        {
            case "copyPath":
                var clipboard = Clipboard ?? throw new InvalidOperationException(Loc["tool.error.clipboard"]);
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(CurrentPaths()));
                await clipboard.SetDataAsync(data);
                DoneText = Loc["tool.quick.copied"];
                return [];
            case "move":
                if (_destination is not { } destination) throw new InvalidOperationException(Loc["tool.move.none"]);
                await Task.Run(() => DilimService.MoveFiles(_files, destination, ct), ct);
                DoneText = Loc["tool.done"];
                return [.. _files.Select(f => Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(f))))];
            case "copy" or "share" or "terminal":
                await _run(_id, _files, new ToolRequest(), this, progress, ct);
                DoneText = Loc[_id == "copy" ? "tool.quick.copied" : "tool.done"];
                return [];
            default:
                var request = new ToolRequest { Color = _colors?.Selected ?? "#FFFFFF" };
                request.Validate(_id);
                return await _run(_id, _files, request, this, progress, ct);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Enter runs an action that needs no choice, as the pie's own slices do.
        if (e.Key == Key.Enter && PrimaryEnabled && !e.Handled && _id is not "move")
        {
            e.Handled = true;
            _ = StartAsync();
        }
    }
}

/// <summary>"How the pie works": the guide in the tools' card, closed with its one button.</summary>
public sealed class GuideWindow : ToolShell
{
    public GuideWindow(ILocalizer loc) : base("pie.help", loc, 620, 620)
    {
        Body = new TextBlock { Text = loc["pie.guide"], TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 26 };
        PrimaryText = loc["tool.guide.close"];
    }

    protected override Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}
