using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dilim.Core.Localization;
using Dilim.Engines.Pdf;

namespace Dilim.App.Views.Tools;

/// <summary>
/// "Reduce size" for PDFs: Smart (pictures only) or Flatten (whole pages), five levels, and the size and quality each
/// one really gives (measured in the background on the actual files, not guessed). A target size picks the best
/// quality that stays under it, between levels too. "Compare quality" shows the same part of a page before and after.
/// </summary>
public sealed class PdfShrinkTool : ToolShell
{
    private readonly IReadOnlyList<string> _files;
    private readonly long _originalTotal;
    private readonly CultureInfo _culture;
    private readonly Dictionary<(string, PdfShrinkMethod, PdfShrinkSetting), Task<PdfShrinkResult>> _measured = [];
    private readonly Dictionary<(string, PdfShrinkMethod, long), Task<PdfShrinkResult?>> _fitted = [];

    private PdfShrinkMethod _method = PdfShrinkMethod.Smart;
    private int _level = 2;
    private double _targetMb;
    private CancellationTokenSource? _estimate;

    private readonly Button _smart = new(), _flatten = new();
    private readonly TextBlock _methodNote = new(), _meta = new();
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 4, TickFrequency = 1, IsSnapToTickEnabled = true, Value = 2 };
    private readonly Grid _levelNames = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
    private readonly TextBlock _sizeValue = new(), _sizeFoot = new(), _qualityValue = new(), _qualityFoot = new(), _hint = new(), _targetNote = new();
    private readonly ProgressBar _sizeBar = new() { Minimum = 0, Maximum = 1, Height = 6, MinWidth = 0 };
    private readonly ProgressBar _qualityBar = new() { Minimum = 0, Maximum = 1, Height = 6, MinWidth = 0 };
    private readonly TextBox _target = new() { Width = 96, PlaceholderText = "", HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Button _compare = new();
    private readonly Grid _comparison = new() { ColumnDefinitions = new ColumnDefinitions("*,12,*"), IsVisible = false };
    private readonly TextBlock _compareNote = new();

    public PdfShrinkTool(IReadOnlyList<string> files, ILocalizer loc) : base("tool.pdfshrink.title", loc, 560, 860)
    {
        _files = files;
        _originalTotal = files.Sum(f => new FileInfo(f).Length);
        _culture = SafeCulture(loc.Language);
        Body = Build();
        PrimaryText = loc["tool.pdfshrink.run"];
        SetSecondary(loc["tool.pdfshrink.reset"], Reset);
        Refresh();
    }

    // ───────── Layout ─────────

    private Control Build()
    {
        var body = new StackPanel { Spacing = 0 };

        // What is being reduced.
        var summary = new TextBlock
        {
            Text = _files.Count == 1
                ? Loc.Format("tool.pdfshrink.one", Path.GetFileName(_files[0]), Size(_originalTotal))
                : Loc.Format("tool.pdfshrink.many", _files.Count, Size(_originalTotal)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 18),
        };
        summary.Classes.Add("muted");
        body.Children.Add(summary);

        // Method.
        body.Children.Add(Label(Loc["tool.pdfshrink.method"]));
        _smart.Content = Loc["tool.pdfshrink.smart"];
        _flatten.Content = Loc["tool.pdfshrink.flatten"];
        _smart.Click += (_, _) => SetMethod(PdfShrinkMethod.Smart);
        _flatten.Click += (_, _) => SetMethod(PdfShrinkMethod.Flatten);
        var segment = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        segment.Children.Add(_smart);
        Grid.SetColumn(_flatten, 1); segment.Children.Add(_flatten);
        foreach (var b in new[] { _smart, _flatten }) { b.Classes.Add("seg-item"); b.HorizontalAlignment = HorizontalAlignment.Stretch; }
        var segmentFrame = new Border { Child = segment };
        segmentFrame.Classes.Add("seg");
        body.Children.Add(segmentFrame);
        _methodNote.TextWrapping = TextWrapping.Wrap;
        _methodNote.Margin = new Thickness(0, 10, 0, 22);
        _methodNote.Classes.Add("muted");
        body.Children.Add(_methodNote);

        // Level: a five-stop slider with names under it.
        body.Children.Add(Label(Loc["tool.pdfshrink.level"]));
        _slider.Classes.Add("level");
        _slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            var level = (int)Math.Round(_slider.Value);
            if (level == _level) return;
            _level = level;
            ClearTarget();
            Refresh();
        };
        body.Children.Add(_slider);
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var name = new Button { Content = Loc["tool.pdfshrink.level" + i], HorizontalAlignment = HorizontalAlignment.Center };
            name.Classes.Add("level-name");
            name.Click += (_, _) => _slider.Value = index;
            Grid.SetColumn(name, i);
            _levelNames.Children.Add(name);
        }
        body.Children.Add(_levelNames);
        _meta.Classes.Add("caption");
        _meta.Margin = new Thickness(0, 10, 0, 22);
        body.Children.Add(_meta);

        // Measured size and quality.
        body.Children.Add(Meter(Loc["tool.pdfshrink.size"], _sizeValue, _sizeBar, _sizeFoot));
        body.Children.Add(Meter(Loc["tool.pdfshrink.quality"], _qualityValue, _qualityBar, _qualityFoot).Also(m => m.Margin = new Thickness(0, 16, 0, 0)));
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.Margin = new Thickness(0, 12, 0, 0);
        _hint.Classes.Add("tool-hint");
        _hint.IsVisible = false;
        body.Children.Add(_hint);

        // Compare quality.
        _compare.Content = Loc["tool.pdfshrink.compare"];
        _compare.Margin = new Thickness(0, 16, 0, 0);
        _compare.Click += async (_, _) => await CompareAsync();
        body.Children.Add(_compare);
        _comparison.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(_comparison);
        _compareNote.Classes.Add("caption");
        _compareNote.IsVisible = false;
        _compareNote.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_compareNote);

        // Target size.
        body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 22, 0, 18) }.Also(b => b.Classes.Add("rule")));
        body.Children.Add(Label(Loc["tool.pdfshrink.target"]));
        _target.PlaceholderText = Loc["tool.pdfshrink.target_hint"];
        _target.InnerRightContent = new TextBlock { Text = "MB", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }.Also(t => t.Classes.Add("muted"));
        _target.TextChanged += (_, _) =>
        {
            var text = (_target.Text ?? "").Replace(',', '.').Trim();
            var value = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0 && mb < 100_000 ? Math.Round(mb, 1) : 0;
            if (Math.Abs(value - _targetMb) < 0.01) return;
            _targetMb = value;
            Refresh();
        };
        foreach (var mb in new[] { 5, 10, 20, 25 })
        {
            var chip = new Button { Content = mb.ToString(CultureInfo.InvariantCulture), Tag = mb };
            chip.Classes.Add("chip");
            chip.Click += (_, _) =>
            {
                _target.Text = Math.Abs(_targetMb - mb) < 0.01 ? "" : mb.ToString(CultureInfo.InvariantCulture);
            };
            _chips.Children.Add(chip);
        }
        var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _target, _chips } };
        body.Children.Add(targetRow);
        _targetNote.TextWrapping = TextWrapping.Wrap;
        _targetNote.Classes.Add("caption");
        _targetNote.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(_targetNote);
        return body;
    }

    private static Control Meter(string title, TextBlock value, ProgressBar bar, TextBlock foot)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Label(title).Also(l => l.Margin = new Thickness(0)));
        value.FontWeight = FontWeight.SemiBold;
        Grid.SetColumn(value, 1); head.Children.Add(value);
        bar.Classes.Add("meter");
        foot.Classes.Add("caption");
        foot.Margin = new Thickness(0, 6, 0, 0);
        return new StackPanel { Children = { head, bar, foot } };
    }

    // ───────── State ─────────

    private void SetMethod(PdfShrinkMethod method)
    {
        if (_method == method) return;
        _method = method;
        _comparison.IsVisible = _compareNote.IsVisible = false;
        Refresh();
    }

    private void ClearTarget()
    {
        if (_targetMb == 0) return;
        _target.Text = "";
    }

    private void Reset()
    {
        _method = PdfShrinkMethod.Smart;
        _slider.Value = 2;
        _target.Text = "";
        _comparison.IsVisible = _compareNote.IsVisible = false;
        Refresh();
    }

    private void Refresh()
    {
        _smart.Classes.Set("on", _method == PdfShrinkMethod.Smart);
        _flatten.Classes.Set("on", _method == PdfShrinkMethod.Flatten);
        _methodNote.Text = Loc[_method == PdfShrinkMethod.Smart ? "tool.pdfshrink.smart_note" : "tool.pdfshrink.flatten_note"];
        var targeting = _targetMb > 0;
        _slider.IsEnabled = !targeting;
        _slider.Opacity = _levelNames.Opacity = targeting ? 0.45 : 1;
        for (var i = 0; i < _levelNames.Children.Count; i++)
            _levelNames.Children[i].Classes.Set("on", !targeting && i == _level);
        var setting = PdfShrinker.Levels[_level];
        _meta.Text = targeting ? Loc.Format("tool.pdfshrink.target_note", Number(_targetMb))
            : Loc.Format(_method == PdfShrinkMethod.Smart ? "tool.pdfshrink.meta_smart" : "tool.pdfshrink.meta_flat", setting.Dpi, setting.Quality);
        foreach (var chip in _chips.Children.OfType<Button>())
            chip.Classes.Set("on", targeting && Math.Abs(_targetMb - (int)chip.Tag!) < 0.01);
        _ = EstimateAsync();
    }

    // ───────── Measuring ─────────

    private Task<PdfShrinkResult> Measure(string file, PdfShrinkMethod method, PdfShrinkSetting setting)
    {
        var key = (file, method, setting);
        if (!_measured.TryGetValue(key, out var task) || task.IsFaulted || task.IsCanceled)
            _measured[key] = task = PdfShrinker.MeasureAsync(file, method, setting, CancellationToken.None);
        return task;
    }

    private Task<PdfShrinkResult?> Fit(string file, PdfShrinkMethod method, long target)
    {
        var key = (file, method, target);
        if (!_fitted.TryGetValue(key, out var task) || task.IsFaulted || task.IsCanceled)
            _fitted[key] = task = PdfShrinker.FitAsync(file, method, target, CancellationToken.None,
                s => Measure(file, method, s));
        return task;
    }

    /// <summary>The result for each file at the current choice (level, or the best under the target).</summary>
    private async Task<(IReadOnlyList<PdfShrinkResult> Results, bool Missed)> CurrentAsync(CancellationToken ct)
    {
        var results = new List<PdfShrinkResult>();
        var missed = false;
        foreach (var file in _files)
        {
            ct.ThrowIfCancellationRequested();
            if (_targetMb > 0)
            {
                var fit = await Fit(file, _method, (long)(_targetMb * 1024 * 1024));
                if (fit is null) { missed = true; fit = await Measure(file, _method, PdfShrinker.Levels[^1]); }
                results.Add(fit);
            }
            else results.Add(await Measure(file, _method, PdfShrinker.Levels[_level]));
        }
        return (results, missed);
    }

    private async Task EstimateAsync()
    {
        _estimate?.Cancel();
        var cts = _estimate = new CancellationTokenSource();
        _sizeValue.Text = _qualityValue.Text = Loc["tool.pdfshrink.calculating"];
        _sizeFoot.Text = _qualityFoot.Text = "";
        _sizeBar.IsIndeterminate = _qualityBar.IsIndeterminate = true;
        _hint.IsVisible = false;
        _targetNote.Text = "";
        try
        {
            await Task.Delay(150, cts.Token); // a slider being dragged settles first
            var (results, missed) = await CurrentAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            var size = results.Sum(r => r.Size);
            var similarity = results.Sum(r => r.Similarity * r.Size) / Math.Max(1, size);
            _sizeBar.IsIndeterminate = _qualityBar.IsIndeterminate = false;
            _sizeValue.Text = Loc.Format("tool.pdfshrink.size_value", Size(_originalTotal), Size(size));
            _sizeBar.Value = _originalTotal > 0 ? (double)size / _originalTotal : 1;
            var saved = _originalTotal > 0 ? 1 - (double)size / _originalTotal : 0;
            _sizeFoot.Text = saved >= 0.005 ? Loc.Format("tool.pdfshrink.size_saved", Math.Round(saved * 100)) : Loc["tool.pdfshrink.not_smaller"];
            var grade = PdfShrinker.Grade(similarity);
            _qualityValue.Text = Loc["tool.pdfshrink.q" + grade];
            _qualityBar.Value = similarity;
            _qualityBar.Classes.Set("low", grade <= 1);
            _qualityFoot.Text = Loc.Format("tool.pdfshrink.quality_foot", Math.Round(similarity * 100));

            Prefetch();

            // Smart saves little on a file of drawings and text: suggest Flatten.
            if (_method == PdfShrinkMethod.Smart && _targetMb == 0 && saved < 0.15)
            {
                _hint.Text = Loc["tool.pdfshrink.vector_hint"];
                _hint.IsVisible = true;
            }
            if (_targetMb > 0)
            {
                _targetNote.Text = missed
                    ? Loc.Format("tool.pdfshrink.target_miss", Size(results.Max(r => r.Size))) + (_method == PdfShrinkMethod.Smart ? " " + Loc["tool.pdfshrink.try_flatten"] : "")
                    : results.Count == 1
                        ? Loc.Format("tool.pdfshrink.target_setting", results[0].Setting.Dpi, results[0].Setting.Quality)
                        : Loc.Format("tool.pdfshrink.target_files", Number(_targetMb));
                _targetNote.Classes.Set("danger", missed);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            _sizeBar.IsIndeterminate = _qualityBar.IsIndeterminate = false;
            _sizeValue.Text = _qualityValue.Text = "—";
            _sizeFoot.Text = ex.Message.StartsWith("tool.", StringComparison.Ordinal) ? Loc[ex.Message] : ex.Message;
        }
    }

    /// <summary>
    /// The other levels of this method, measured one after another in the background, so moving the slider shows
    /// their numbers at once. Each is measured once per window.
    /// </summary>
    private async void Prefetch()
    {
        if (_targetMb > 0) return;
        var method = _method;
        foreach (var file in _files)
        foreach (var level in PdfShrinker.Levels)
        {
            // The work runs off the UI thread (MeasureAsync); this loop only waits for one at a time.
            if (method != _method || !IsVisible) return;
            try { await Measure(file, method, level); }
            catch (Exception) { /* shown when that level is picked */ }
        }
    }

    private async Task CompareAsync()
    {
        _compare.IsEnabled = false;
        _compareNote.IsVisible = true;
        _compareNote.Text = Loc["tool.pdfshrink.compare_wait"];
        try
        {
            var file = _files[0];
            var (results, _) = await CurrentAsync(CancellationToken.None);
            var bytes = await PdfShrinker.ShrinkAsync(file, _method, results[0].Setting, CancellationToken.None);
            var (before, after) = await PdfShrinker.CompareAsync(file, bytes, 0, 360, CancellationToken.None);
            _comparison.Children.Clear();
            _comparison.Children.Add(Picture(before, Loc["tool.pdfshrink.before"]));
            _comparison.Children.Add(Picture(after, Loc["tool.pdfshrink.after"]).Also(p => Grid.SetColumn(p, 2)));
            _comparison.IsVisible = true;
            _compareNote.Text = Loc["tool.pdfshrink.compare_note"];
        }
        catch (Exception)
        {
            _compareNote.Text = Loc["tool.pdfshrink.compare_error"];
        }
        finally { _compare.IsEnabled = true; }
    }

    private static Control Picture(byte[] png, string caption)
    {
        using var stream = new MemoryStream(png);
        var image = new Image { Source = new Bitmap(stream), Stretch = Stretch.Uniform };
        var frame = new Border { Child = image, CornerRadius = new CornerRadius(12), ClipToBounds = true };
        frame.Classes.Add("order-picture");
        var label = new TextBlock { Text = caption, Margin = new Thickness(0, 0, 0, 6) };
        label.Classes.Add("tool-label");
        return new StackPanel { Children = { label, frame } };
    }

    // ───────── Running ─────────

    protected override void OnRunningChanged(bool running)
    {
        base.OnRunningChanged(running);
        if (running) _estimate?.Cancel();
    }

    protected override async Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var outputs = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = _files[i];
            PdfShrinkSetting setting;
            if (_targetMb > 0)
                setting = (await Fit(file, _method, (long)(_targetMb * 1024 * 1024)).WaitAsync(ct))?.Setting ?? PdfShrinker.Levels[^1];
            else setting = PdfShrinker.Levels[_level];
            var bytes = await PdfShrinker.ShrinkAsync(file, _method, setting, ct,
                new Progress<double>(p => progress.Report((i + p) / _files.Count)));
            // Never bigger than the original: then the original is saved as it is.
            if (bytes.LongLength >= new FileInfo(file).Length) bytes = await File.ReadAllBytesAsync(file, ct);
            var output = OutputNextTo(file, ".pdf");
            await File.WriteAllBytesAsync(output, bytes, ct);
            outputs.Add(output);
            progress.Report((i + 1.0) / _files.Count);
        }
        return outputs;
    }

    // ───────── Formatting ─────────

    private string Size(long bytes) => bytes >= 1024 * 1024
        ? (bytes / 1048576.0).ToString(bytes >= 100 * 1048576 ? "0" : "0.0", _culture) + " MB"
        : Math.Max(1, bytes / 1024).ToString(_culture) + " KB";

    private string Number(double value) => value.ToString("0.#", _culture);

    private static CultureInfo SafeCulture(string language)
    {
        try { return CultureInfo.GetCultureInfo(language == "auto" ? CultureInfo.CurrentUICulture.Name : language); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
