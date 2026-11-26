using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>
/// The picture of a video at a time, with the controls every video tool shares: a step back and forward by one frame,
/// play / pause (a silent preview from frames taken ahead, at the tool's speed) and "0:01.60 / 0:04.00". The picture
/// is a <see cref="BoxCanvas"/>, so a tool can put boxes on it (crop, redaction). An exact frame is fetched whenever
/// the time stops changing.
/// </summary>
public sealed class VideoPreview : StackPanel
{
    private const int PlayFrames = 240, PlayWidth = 640, FrameWidth = 1280;

    private readonly string _file;
    private readonly Button _back = new(), _play = new(), _forward = new();
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider? _scrubber;
    private readonly Geometry? _playIcon, _pauseIcon;
    private IReadOnlyList<Bitmap> _playback = [];
    private DispatcherTimer? _timer;
    private DateTime _playStarted;
    private double _playFrom;
    private int _request;
    private bool _scrubbing;

    public BoxCanvas Canvas { get; } = new();
    public MediaProbe? Info { get; private set; }
    public double Duration => Info?.Duration ?? 0;
    public double FrameRate => Info?.FrameRate is > 1 and <= 240 ? Info.FrameRate : 30;
    public double Time { get; private set; }

    /// <summary>How fast "play" runs (the speed tool previews its speed).</summary>
    public double PlaybackRate { get; set; } = 1;

    /// <summary>Playback stops here (the end of a trimmed range); the whole video when null.</summary>
    public Func<(double Start, double End)>? PlayRange { get; set; }

    public event Action<double>? TimeChanged;
    public event Action? Loaded;

    public VideoPreview(string file, double height, bool scrubber, Geometry? back, Geometry? play, Geometry? pause, Geometry? forward)
    {
        _file = file;
        _playIcon = play; _pauseIcon = pause;
        Spacing = 12;
        Canvas.Height = height;
        Children.Add(new Border { Child = Canvas, CornerRadius = new CornerRadius(18), ClipToBounds = true });
        if (scrubber)
        {
            _scrubber = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(4, 0) };
            _scrubber.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty && !_scrubbing) Seek(_scrubber.Value, fromScrubber: true);
            };
            Children.Add(_scrubber);
        }
        foreach (var (button, icon) in new[] { (_back, back), (_play, play), (_forward, forward) })
        {
            button.Classes.Add("transport");
            button.Content = new PathIcon { Data = icon, Width = 16, Height = 16 };
        }
        _back.Click += (_, _) => Step(-1);
        _forward.Click += (_, _) => Step(1);
        _play.Click += (_, _) => TogglePlay();
        var transport = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto") };
        transport.Children.Add(_back);
        Grid.SetColumn(_play, 1); transport.Children.Add(_play);
        Grid.SetColumn(_forward, 2); transport.Children.Add(_forward);
        _clock.Classes.Add("muted");
        _clock.FontFeatures = FontFeatureCollection.Parse("tnum");
        Grid.SetColumn(_clock, 4); transport.Children.Add(_clock);
        Children.Add(transport);
        DetachedFromVisualTree += (_, _) => Stop();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Info = await ToolEngine.ProbeAsync(_file, CancellationToken.None);
            if (_scrubber is not null) _scrubber.Maximum = Math.Max(0.01, Duration);
            Canvas.SetPicture(null, new PixelSize(Math.Max(1, Info.Width), Math.Max(1, Info.Height)));
            UpdateClock();
            Loaded?.Invoke();
            await ShowFrameAsync(0);
            // Frames for playback, taken once in the background.
            var frames = await ToolPreviews.FramesAsync(_file, Duration, (int)Math.Min(PlayFrames, Math.Max(2, Duration * 12)), PlayWidth);
            _playback = frames.Select(bytes => { using var s = new MemoryStream(bytes); return new Bitmap(s); }).ToList();
        }
        catch (Exception)
        {
            _clock.Text = "—";
        }
    }

    public void Seek(double time, bool fromScrubber = false)
    {
        time = Math.Clamp(time, 0, Math.Max(0, Duration - 1 / FrameRate));
        Time = time;
        if (_scrubber is not null && !fromScrubber)
        {
            _scrubbing = true;
            _scrubber.Value = time;
            _scrubbing = false;
        }
        UpdateClock();
        TimeChanged?.Invoke(time);
        Canvas.Refresh();
        if (_timer is null) _ = ShowFrameAsync(time);
    }

    /// <summary>One frame back or forward.</summary>
    public void Step(int frames)
    {
        Stop();
        Seek(Math.Round((Time + frames / FrameRate) * FrameRate) / FrameRate);
    }

    private async Task ShowFrameAsync(double time)
    {
        var request = ++_request;
        await Task.Delay(40); // a slider being dragged: only the last position is fetched
        if (request != _request) return;
        var png = await ToolPreviews.VideoFrameAsync(_file, time, FrameWidth);
        if (request != _request || png is null || _timer is not null) return;
        using var stream = new MemoryStream(png);
        Canvas.SetPicture(new Bitmap(stream), Canvas.SourceSize);
    }

    public void TogglePlay()
    {
        if (_timer is not null) { Stop(); return; }
        if (_playback.Count == 0 || Duration <= 0) return;
        var (start, end) = PlayRange?.Invoke() ?? (0, Duration);
        if (Time < start || Time >= end - 1 / FrameRate) Seek(start);
        _playFrom = Time;
        _playStarted = DateTime.UtcNow;
        ((PathIcon)_play.Content!).Data = _pauseIcon;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) =>
        {
            var (_, until) = PlayRange?.Invoke() ?? (0, Duration);
            var t = _playFrom + (DateTime.UtcNow - _playStarted).TotalSeconds * PlaybackRate;
            if (t >= until) { Stop(); Seek(until - 1 / FrameRate); return; }
            Time = t;
            if (_scrubber is not null) { _scrubbing = true; _scrubber.Value = t; _scrubbing = false; }
            var index = Math.Clamp((int)(t / Duration * _playback.Count), 0, _playback.Count - 1);
            Canvas.SetPicture(_playback[index], Canvas.SourceSize);
            UpdateClock();
            TimeChanged?.Invoke(t);
        });
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer = null;
        ((PathIcon)_play.Content!).Data = _playIcon;
        _ = ShowFrameAsync(Time);
    }

    private void UpdateClock() => _clock.Text = $"{Clock(Time)} / {Clock(Duration)}";

    /// <summary>"0:01.60".</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    /// <summary>Reads "1:02.5", "62.5", "0:01:02.50" or "62,5" as seconds; null when it isn't a time.</summary>
    public static double? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Replace(',', '.').Split(':');
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0) return null;
            total = total * 60 + value;
        }
        return total;
    }
}
