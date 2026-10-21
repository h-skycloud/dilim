using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Filee.App.ViewModels;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Engines.Dilim;

namespace Filee.App.Views;

/// <summary>A compact operation editor with explicit units, ordered inputs, progress, cancellation and results.</summary>
public sealed class ToolWindow : Window
{
    public ToolWindow(string id, IReadOnlyList<string> files, ILocalizer loc,
        Func<string, IReadOnlyList<string>, ToolRequest, Window, IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> run)
    {
        Title = loc["tool." + id]; Width = 660; Height = 680; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 26, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = loc["tool.hint." + id], TextWrapping = TextWrapping.Wrap });
        var ordered = files.ToList();
        var selection = new ListBox { ItemsSource = ordered.Select(Path.GetFileName).ToArray(), MaxHeight = 120, SelectedIndex = 0 };
        panel.Children.Add(selection);
        if (id is "joinVideos" or "createCollage" or "mergePDF")
        {
            var orderButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var delta in new[] { -1, 1 })
            {
                var button = new Button { Content = delta < 0 ? "↑" : "↓" };
                button.Click += (_, _) =>
                {
                    var i = selection.SelectedIndex; var next = i + delta;
                    if (i < 0 || next < 0 || next >= ordered.Count) return;
                    (ordered[i], ordered[next]) = (ordered[next], ordered[i]);
                    selection.ItemsSource = ordered.Select(Path.GetFileName).ToArray(); selection.SelectedIndex = next;
                };
                orderButtons.Children.Add(button);
            }
            panel.Children.Add(orderButtons);
        }
        var fields = new Dictionary<string, TextBox>();
        ComboBox? imageFormat = null, videoCodec = null;
        void Field(string name, string value)
        {
            panel.Children.Add(new TextBlock { Text = loc["tool.field." + name], FontWeight = FontWeight.SemiBold });
            var box = new TextBox { Text = value };
            fields[name] = box; panel.Children.Add(box);
        }
        if (id == "compress")
        {
            var category = ProfileSelector.DetectFormats(files).FirstOrDefault()?.Category;
            if (category == FormatCategory.Image)
            {
                panel.Children.Add(new TextBlock { Text = loc["presets.target"], FontWeight = FontWeight.SemiBold });
                imageFormat = new ComboBox
                {
                    ItemsSource = new[] { "webp", "jpg", "avif" },
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                panel.Children.Add(imageFormat);
                Field("quality", "70"); Field("resize", "100");
            }
            if (category == FormatCategory.Pdf) { Field("pdfDpi", "150"); Field("quality", "65"); }
            if (category == FormatCategory.Video)
            {
                panel.Children.Add(new TextBlock { Text = loc["presets.video_codec"], FontWeight = FontWeight.SemiBold });
                videoCodec = new ComboBox
                {
                    ItemsSource = new[] { new Choice<VideoCodec>(VideoCodec.Default, "H.264"), new Choice<VideoCodec>(VideoCodec.Hevc, "HEVC / H.265"), new Choice<VideoCodec>(VideoCodec.Av1, "AV1") },
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                panel.Children.Add(videoCodec);
                Field("crf", "30"); Field("maxHeight", "0"); Field("fps", "0");
            }
            if (category is FormatCategory.Video or FormatCategory.Audio) Field("bitrate", "128");
            panel.Children.Add(new TextBlock { Text = loc["tool.compress.settings_hint"], TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        if (id is "trimVideo" or "trimAudio" or "redactVideo" or "redactAudio") { Field("start", "0"); Field("end", "5"); }
        if (id == "videoSnapshots") Field("start", "0");
        if (id == "splitVideo") Field("segment", "10");
        if (id == "changeVideoSpeed") Field("speed", "2");
        if (id is "cropVideo" or "redactVideo" or "redactImage" or "annotateImage") { Field("x", "0"); Field("y", "0"); }
        if (id is "cropVideo" or "redactVideo" or "redactImage") { Field("width", "100"); Field("height", "100"); }
        if (id == "annotateImage") { Field("text", "Dilim"); Field("font", "32"); }
        if (id is "annotateImage" or "frameImage") Field("color", "#FFFFFF");
        if (id == "editImage")
        {
            Field("rotation", "0"); Field("resize", "100"); Field("brightness", "0");
            Field("contrast", "0"); Field("saturation", "100"); Field("mirror", "0");
        }
        if (id == "audioChannels") Field("channels", "1");
        if (id == "createCollage") Field("columns", "2");
        if (id == "organizePDF") Field("pages", "1");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, IsVisible = false };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        var apply = new Button { Content = loc[id == "move" ? "tool.move.choose" : "tool.run"], Margin = new Thickness(0, 0, 8, 8) };
        apply.Classes.Add("accent");
        var cancel = new Button { Content = loc["toast.cancel"], IsVisible = false, Margin = new Thickness(0, 0, 8, 8) };
        var open = new Button { Content = loc["home.open_folder"], IsVisible = false };
        string? outputFolder = null;
        open.Click += (_, _) => { if (outputFolder is not null) Process.Start(new ProcessStartInfo(outputFolder) { UseShellExecute = true }); };
        buttons.Children.Add(apply); buttons.Children.Add(cancel); buttons.Children.Add(open);
        panel.Children.Add(bar); panel.Children.Add(status); panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = loc[id == "move" ? "tool.move.notice" : "tool.output.notice"], TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Content = new ScrollViewer { Content = panel };
        CancellationTokenSource? cancellation = null;
        cancel.Click += (_, _) => cancellation?.Cancel();
        Closing += (_, e) => { if (cancellation is not null) { cancellation.Cancel(); e.Cancel = true; } };
        apply.Click += async (_, _) =>
        {
            cancellation = new CancellationTokenSource();
            apply.IsEnabled = false; selection.IsEnabled = false; cancel.IsVisible = true; open.IsVisible = false;
            bar.IsVisible = true; bar.IsIndeterminate = true; status.Text = loc["tool.running"];
            foreach (var field in fields.Values) field.IsEnabled = false;
            if (imageFormat is not null) imageFormat.IsEnabled = false;
            if (videoCodec is not null) videoCodec.IsEnabled = false;
            string S(string key, string fallback) => fields.TryGetValue(key, out var input) ? input.Text?.Trim() ?? "" : fallback;
            double D(string key, double fallback) => double.TryParse(S(key, fallback.ToString(CultureInfo.InvariantCulture)).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new ArgumentException("tool.error.number");
            int I(string key, int fallback) => int.TryParse(S(key, fallback.ToString(CultureInfo.InvariantCulture)), out var n) ? n : throw new ArgumentException("tool.error.number");
            try
            {
                var request = new ToolRequest
                {
                    Start = D("start", 0),
                    End = D(id == "splitVideo" ? "segment" : "end", 5),
                    Speed = D("speed", 2),
                    X = I("x", 0),
                    Y = I("y", 0),
                    Width = I("width", 100),
                    Height = I("height", 100),
                    Rotation = I("rotation", 90),
                    ResizePercent = I("resize", 100),
                    Quality = I("quality", 70),
                    ImageFormat = imageFormat?.SelectedItem as string ?? "webp",
                    PdfDpi = I("pdfDpi", 150),
                    PdfQuality = I("quality", 65),
                    VideoCrf = I("crf", 30),
                    VideoHeight = I("maxHeight", 0),
                    VideoFrameRate = D("fps", 0),
                    VideoCodec = (videoCodec?.SelectedItem as Choice<VideoCodec>)?.Value ?? VideoCodec.Default,
                    AudioBitrateKbps = I("bitrate", 128),
                    Brightness = I("brightness", 0),
                    Contrast = I("contrast", 0),
                    Saturation = I("saturation", 100),
                    Mirror = I("mirror", 0) != 0,
                    Channels = I("channels", 1),
                    Columns = I("columns", 2),
                    FontSize = I("font", 32),
                    Color = S("color", "#FFFFFF"),
                    Text = S("text", "Dilim"),
                    Pages = S("pages", "1")
                };
                request.Validate(id);
                var outputs = await run(id, ordered.ToArray(), request, this,
                    new Progress<double>(p => { bar.IsIndeterminate = false; bar.Value = p; }), cancellation.Token);
                status.Text = loc["tool.done"] + (outputs.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, outputs) : "");
                if (outputs.Count > 0)
                {
                    open.IsVisible = true;
                    outputFolder = Path.GetDirectoryName(outputs[0]);
                }
                bar.IsIndeterminate = false; bar.Value = 1;
            }
            catch (OperationCanceledException) { status.Text = loc["tool.cancelled"]; }
            catch (Exception ex) { status.Text = ex.Message.StartsWith("tool.", StringComparison.Ordinal) ? loc[ex.Message] : ex.Message; }
            finally
            {
                cancellation.Dispose(); cancellation = null; apply.IsEnabled = true; selection.IsEnabled = true; cancel.IsVisible = false; bar.IsIndeterminate = false;
                foreach (var field in fields.Values) field.IsEnabled = true;
                if (imageFormat is not null) imageFormat.IsEnabled = true;
                if (videoCodec is not null) videoCodec.IsEnabled = true;
            }
        };
    }
}
