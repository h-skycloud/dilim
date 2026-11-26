using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Dilim.App.Services;
using Dilim.Core.Localization;
using Dilim.Core.Presets;
using Dilim.Core.Settings;

namespace Dilim.App.Views.Tools;

/// <summary>
/// "Crop image": a box on the picture to drag and resize, preset or free ratios, exact width and height in pixels.
/// Several pictures can be cropped in one go: each keeps its own box (‹ › switches between them).
/// </summary>
public sealed class CropImageTool : ToolShell
{
    private static readonly (string Key, double? Ratio)[] Ratios =
        [("tool.crop.free", null), ("1:1", 1), ("16:9", 16.0 / 9), ("9:16", 9.0 / 16), ("4:3", 4.0 / 3), ("3:4", 3.0 / 4)];

    private readonly IReadOnlyList<string> _files;
    private readonly Preset _preset;
    private readonly Rect?[] _areas;
    private readonly PixelSize[] _sizes;
    private int _current;
    private readonly BoxCanvas _canvas = new() { CropMode = true, Height = 440 };
    private readonly Segmented _ratio;
    private readonly TextBox _width = new() { Width = 90 }, _height = new() { Width = 90 };
    private readonly TextBlock _which = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous = new() { Content = "‹" }, _next = new() { Content = "›" };
    private bool _updating;

    public CropImageTool(IReadOnlyList<string> files, Preset preset, ILocalizer loc) : base("tool.cropImage", loc, 720, 740)
    {
        _files = files;
        _preset = preset;
        _areas = new Rect?[files.Count];
        _sizes = new PixelSize[files.Count];
        _ratio = new Segmented(Ratios.Select(r => r.Key.StartsWith("tool.", StringComparison.Ordinal) ? loc[r.Key] : r.Key).ToArray());
        _ratio.Changed += i =>
        {
            _canvas.Ratio = Ratios[i].Ratio;
            if (_canvas.Boxes.FirstOrDefault() is { } box) box.Area = _canvas.Clamp(_canvas.Centered(Ratios[i].Ratio));
            Sync();
        };
        _canvas.Changed += Sync;
        foreach (var box in new[] { _width, _height })
        {
            box.LostFocus += (_, _) => TypeSize();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { TypeSize(); e.Handled = true; } };
        }
        _previous.Click += async (_, _) => await ShowAsync(_current - 1);
        _next.Click += async (_, _) => await ShowAsync(_current + 1);

        var size = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            Caption("W"), _width, Caption("H"), _height, Caption("px"),
        } };
        var body = new StackPanel { Children = { Framed(_canvas), Row(loc["tool.crop.ratio"], _ratio, 18), Row(loc["tool.crop.size"], size) } };
        if (files.Count > 1)
        {
            var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _previous, _which, _next } };
            body.Children.Insert(0, nav.Also(n => n.Margin = new Thickness(0, 0, 0, 12)));
        }
        Body = body;
        PrimaryText = loc["tool.crop.apply"];
        SetSecondary(loc["tool.pdfshrink.reset"], () =>
        {
            _ratio.Selected = 0;
            if (_canvas.Boxes.FirstOrDefault() is { } box) box.Area = _canvas.Centered(null);
            Sync();
        });
        _ = ShowAsync(0);
    }

    private static TextBlock Caption(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }.Also(t => t.Classes.Add("muted"));

    private async Task ShowAsync(int index)
    {
        if (index < 0 || index >= _files.Count) return;
        if (_canvas.Boxes.FirstOrDefault() is { } box) _areas[_current] = box.Area;
        _current = index;
        _which.Text = Loc.Format("tool.crop.which", index + 1, _files.Count, Path.GetFileName(_files[index]));
        _previous.IsEnabled = index > 0;
        _next.IsEnabled = index < _files.Count - 1;
        try
        {
            var (bitmap, width, height) = await CropService.LoadPreviewAsync(_files[index]);
            _sizes[index] = new PixelSize(width, height);
            _canvas.SetPicture(bitmap, _sizes[index]);
            _canvas.Boxes.Clear();
            _canvas.Boxes.Add(new CanvasBox { Area = _areas[index] ?? _canvas.Centered(_canvas.Ratio) });
            _canvas.Select(_canvas.Boxes[0]);
            Status = "";
            PrimaryEnabled = true;
        }
        catch (Exception)
        {
            Status = Loc["error.unsupported_source"];
            PrimaryEnabled = false;
        }
        Sync();
    }

    /// <summary>The size fields follow the box.</summary>
    private void Sync()
    {
        if (_canvas.Boxes.FirstOrDefault() is not { } box) return;
        _updating = true;
        _width.Text = ((int)box.Area.Width).ToString(CultureInfo.InvariantCulture);
        _height.Text = ((int)box.Area.Height).ToString(CultureInfo.InvariantCulture);
        _updating = false;
        _canvas.Refresh();
    }

    /// <summary>A size typed in pixels: the box keeps its centre; with a ratio, the height follows the width.</summary>
    private void TypeSize()
    {
        if (_updating || _canvas.Boxes.FirstOrDefault() is not { } box) return;
        if (!int.TryParse(_width.Text, out var w) || !int.TryParse(_height.Text, out var h)) { Sync(); return; }
        if (_canvas.Ratio is { } ratio)
        {
            if (Math.Abs(w - box.Area.Width) >= 1) h = (int)Math.Round(w / ratio);
            else w = (int)Math.Round(h * ratio);
        }
        var center = box.Area.Center;
        box.Area = _canvas.Clamp(new Rect(center.X - w / 2.0, center.Y - h / 2.0, w, h));
        Sync();
    }

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        if (_canvas.Boxes.FirstOrDefault() is { } box) _areas[_current] = box.Area;
        var store = AppHost.Get<UserDataStore>();
        var outputs = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            // A picture never shown keeps its whole area.
            var area = _areas[i];
            var result = await CropService.CropFileAsync(_files[i], (int)(area?.X ?? 0), (int)(area?.Y ?? 0),
                (int)(area?.Width ?? int.MaxValue), (int)(area?.Height ?? int.MaxValue), _preset, store, Loc);
            if (result.Status == CropStatus.Failed) throw new InvalidOperationException(result.ErrorKey ?? "error.conversion_failed");
            if (result.OutputPath is { } path) outputs.Add(path);
            progress.Report((i + 1.0) / _files.Count);
        }
        return outputs;
    }
}
