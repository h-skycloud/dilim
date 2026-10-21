using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;
using Filee.Engines.Dilim;

namespace Filee.App.Services;

/// <summary>Dispatches the tools and file actions used by both the catalogue and the desktop pie.</summary>
public sealed class DilimService(ILocalizer loc, WindowService windows, ConversionService conversions, UserDataStore store)
{
    private ShelfWindow? _shelf;

    /// <summary>Upgrade only the untouched two-gesture defaults. Custom bindings remain intact.</summary>
    public static void EnsureGestures(UserDataStore store)
    {
        if (store.Settings.DilimGesturesInitialized) return;
        var gestures = store.Settings.Triggers;
        var defaults = TriggerGesture.Defaults();
        bool Equivalent(TriggerGesture a, TriggerGesture b) => a.Menu == PieMode.Convert && a.Enabled &&
            a.Kind == b.Kind && a.Modifiers == b.Modifiers && a.Button == b.Button && a.Key == b.Key && a.Scope == b.Scope;
        if (gestures.Count == defaults.Count && gestures.Zip(defaults).All(pair => Equivalent(pair.First, pair.Second)))
            store.Settings.Triggers = CreateGestures();
        store.Settings.DilimGesturesInitialized = true;
        store.SaveSettings();
    }

    public static List<TriggerGesture> CreateGestures()
    {
        var result = new List<TriggerGesture>();
        foreach (var (mode, modifier, key) in new[] { (PieMode.Convert, ModifierKeys.Shift, "Space"), (PieMode.Tools, ModifierKeys.Ctrl, "T"), (PieMode.Actions, ModifierKeys.Alt, "A") })
        {
            result.Add(new TriggerGesture { Menu = mode, Kind = TriggerKind.Drag, Modifiers = modifier });
            result.Add(new TriggerGesture { Menu = mode, Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Key = key, Button = TriggerMouseButton.None });
        }
        return result;
    }

    public void Show(string id, IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        if (id == "cropImage")
        {
            windows.ShowCropOverlay(files, store.FindPreset("tool-crop")!.Clone());
            return;
        }
        if (id == "float")
        {
            _shelf ??= new ShelfWindow(loc);
            _shelf.Add(files);
            _shelf.Show();
            _shelf.Activate();
            return;
        }
        var tool = new ToolWindow(id, files, loc, RunAsync);
        tool.Show();
        tool.Activate();
    }

    public JobViewModel? StartConversion(string id, IReadOnlyList<string> files, ToolRequest? request = null)
    {
        var formats = ProfileSelector.DetectFormats(files);
        var category = formats.FirstOrDefault()?.Category;
        string? presetId = id switch
        {
            "splitPDF" => "pdf-split",
            "mergePDF" => "merge-pdf",
            "createPDF" => "to-pdf",
            "extractArchive" => "extract",
            _ => null,
        };
        Preset? preset = presetId is not null ? store.FindPreset(presetId)?.Clone() : null;
        if (id == "compress" && category is FormatCategory.Image or FormatCategory.Pdf or FormatCategory.Video or FormatCategory.Audio)
            preset = CompressionPreset(category.Value, request ?? new ToolRequest());
        if (id == "removeMetadata" && category is not (FormatCategory.Video or FormatCategory.Audio))
        {
            // PDF metadata uses a fresh PDF container in ToolEngine. Images use the normal engine/queue.
            if (id == "removeMetadata" && category == FormatCategory.Pdf) return null;
            preset = new Preset { TargetFormat = BuiltInData.SameAsSource };
            if (category == FormatCategory.Image)
            {
                preset.Image.KeepMetadata = false; preset.Image.KeepColorProfile = false;
            }
            else if (category == FormatCategory.Pdf) preset.Pdf.Compression = PdfCompression.Ebook;
            else throw new InvalidOperationException(loc["tool.error.unsupported"]);
        }
        if (preset is null) return null;
        preset.Name = loc["tool." + id];
        preset.Output.Location = OutputLocation.Subfolder;
        preset.Output.SubfolderName = "Dilim";
        preset.Output.Conflict = ConflictPolicy.Rename;
        return conversions.Start(files, preset);
    }

    internal static Preset CompressionPreset(FormatCategory category, ToolRequest request)
    {
        request.Validate("compress");
        var preset = new Preset { TargetFormat = BuiltInData.SameAsSource };
        switch (category)
        {
            case FormatCategory.Image:
                preset.TargetFormat = request.ImageFormat;
                preset.Image.Quality = request.Quality;
                preset.Image.Resize = request.ResizePercent == 100 ? ResizeMode.None : ResizeMode.Percent;
                preset.Image.ResizePercent = request.ResizePercent;
                break;
            case FormatCategory.Pdf:
                preset.Pdf.Compression = PdfCompression.Ebook;
                preset.Pdf.CompressionDpi = request.PdfDpi;
                preset.Pdf.CompressionQuality = request.PdfQuality;
                break;
            case FormatCategory.Video:
                preset.TargetFormat = "mp4";
                preset.Media.Quality = MediaQuality.Small;
                preset.Media.Crf = request.VideoCrf;
                preset.Media.MaxHeight = request.VideoHeight;
                preset.Media.FrameRate = request.VideoFrameRate;
                preset.Media.VideoCodec = request.VideoCodec;
                preset.Media.AudioBitrateKbps = request.AudioBitrateKbps;
                break;
            case FormatCategory.Audio:
                preset.TargetFormat = "m4a";
                preset.Media.Quality = MediaQuality.Small;
                preset.Media.AudioBitrateKbps = request.AudioBitrateKbps;
                break;
            default: throw new ArgumentException("tool.error.unsupported");
        }
        return preset;
    }

    private async Task<IReadOnlyList<string>> RunAsync(string id, IReadOnlyList<string> files, ToolRequest request,
        Window owner, IProgress<double> progress, CancellationToken ct)
    {
        if (files.Any(f => !File.Exists(f) && !Directory.Exists(f))) throw new FileNotFoundException(loc["tool.error.missing"]);
        if (DilimTools.Actions.Contains(id))
        {
            await FileActionAsync(id, files, owner, ct);
            return [];
        }
        if (StartConversion(id, files, request) is { } job)
        {
            using var registration = ct.Register(() => job.Job.Cancel());
            while (job.Job.State is Filee.Core.Conversion.JobState.Queued or Filee.Core.Conversion.JobState.Running)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(job.Job.Progress);
                await Task.Delay(100, ct);
            }
            if (job.Job.State != Filee.Core.Conversion.JobState.Completed) throw new InvalidOperationException(job.ErrorDetails ?? job.Status);
            return job.Job.Outputs.ToArray();
        }
        // A private, new folder avoids overwriting any existing output. Concurrent operations get distinct paths.
        var root = Path.Combine(Path.GetDirectoryName(files[0])!, "Dilim");
        var directory = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var outputs = await ToolEngine.RunAsync(id, files, request, directory, progress, ct);
            store.AddHistory(new Filee.Core.History.HistoryEntry
            {
                JobId = Guid.NewGuid().ToString("N")[..10],
                FinishedAt = DateTime.Now,
                PresetId = "dilim-" + id,
                PresetName = loc["tool." + id],
                State = Filee.Core.Conversion.JobState.Completed,
                Sources = [.. files],
                Outputs = [.. outputs],
            });
            return outputs;
        }
        catch
        {
            // Only an empty directory created for this operation can be removed.
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    private async Task FileActionAsync(string id, IReadOnlyList<string> files, Window owner, CancellationToken ct)
    {
        var clipboard = owner.Clipboard ?? throw new InvalidOperationException(loc["tool.error.clipboard"]);
        switch (id)
        {
            case "copyPath":
                var text = new DataTransfer(); text.Add(DataTransferItem.CreateText(string.Join(Environment.NewLine, files)));
                await clipboard.SetDataAsync(text); break;
            case "copy":
                var data = new DataTransfer();
                foreach (var path in files)
                {
                    IStorageItem? item = Directory.Exists(path) ? await owner.StorageProvider.TryGetFolderFromPathAsync(path)
                        : await owner.StorageProvider.TryGetFileFromPathAsync(path);
                    if (item is null) throw new FileNotFoundException(path);
                    data.Add(DataTransferItem.CreateFile(item));
                }
                await clipboard.SetDataAsync(data);
                break;
            case "move":
                var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = loc["tool.move.destination"], AllowMultiple = false });
                if (picked.Count == 0) throw new OperationCanceledException();
                var destination = picked[0].TryGetLocalPath() ?? throw new InvalidOperationException(loc["tool.error.local"]);
                await Task.Run(() => MoveFiles(files, destination, ct), ct);
                break;
            case "terminal":
                var folder = Directory.Exists(files[0]) ? files[0] : Path.GetDirectoryName(files[0])!;
                var terminal = new ProcessStartInfo(OperatingSystem.IsWindows() ? "wt.exe" : OperatingSystem.IsMacOS() ? "open" : "x-terminal-emulator") { UseShellExecute = true, WorkingDirectory = folder };
                foreach (var arg in OperatingSystem.IsWindows() ? new[] { "-d", folder } : OperatingSystem.IsMacOS() ? new[] { "-a", "Terminal", folder } : Array.Empty<string>()) terminal.ArgumentList.Add(arg);
                Process.Start(terminal);
                break;
            case "share":
                if (!OperatingSystem.IsWindows()) throw new InvalidOperationException(loc["tool.error.windows"]);
                if (files.Any(Directory.Exists)) throw new InvalidOperationException(loc["tool.hint.share"]);
                var helper = Path.Combine(AppContext.BaseDirectory, "share", "Filee.Share.exe");
                if (!File.Exists(helper)) throw new InvalidOperationException(loc["tool.error.share"]);
                var share = new ProcessStartInfo(helper) { UseShellExecute = false };
                foreach (var path in files) share.ArgumentList.Add(path);
                Process.Start(share);
                break;
            default: throw new ArgumentException(loc["tool.error.unsupported"]);
        }
    }

    internal static void MoveFiles(IReadOnlyList<string> sources, string destination, CancellationToken ct)
    {
        var root = Path.GetFullPath(destination);
        // Preflight the whole selection before moving even one item. Never merge/replace directories or files.
        var moves = sources.Select(source => (Source: Path.GetFullPath(source), Target: Path.Combine(root, Path.GetFileName(Path.TrimEndingDirectorySeparator(source))))).ToArray();
        if (moves.Select(m => m.Target).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != moves.Length) throw new IOException("tool.error.conflict");
        foreach (var (source, target) in moves)
        {
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("tool.error.conflict");
            if (Directory.Exists(source) && (root.Equals(source, StringComparison.OrdinalIgnoreCase) || root.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("tool.error.destination");
        }
        foreach (var (source, target) in moves)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(source)) Directory.Move(source, target);
            else File.Move(source, target, overwrite: false);
        }
    }

    public void ShowGuide()
    {
        var text = new TextBlock { Text = loc["pie.guide"], TextWrapping = TextWrapping.Wrap, FontSize = 16, LineHeight = 27 };
        var window = new Window
        {
            Title = loc["pie.help"],
            Width = 620,
            Height = 600,
            MinWidth = 400,
            MinHeight = 360,
            Content = new ScrollViewer { Content = text, Margin = new Avalonia.Thickness(28) },
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Show();
    }
}
