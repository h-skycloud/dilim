using Avalonia.Controls;
using Dilim.Core.Localization;
using Dilim.Engines.Tools;

namespace Dilim.App.Views.Tools;

/// <summary>"Merge PDFs" and "Join videos": the files in a list to put in order, then one result.</summary>
public sealed class MergeTool : ToolShell
{
    private readonly string _id;
    private readonly OrderList _list;
    private readonly Func<string, IReadOnlyList<string>, ToolRequest, Window, IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> _run;

    public MergeTool(string id, IReadOnlyList<string> files, ILocalizer loc,
        Func<string, IReadOnlyList<string>, ToolRequest, Window, IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> run)
        : base("tool." + id, loc, 640, Math.Clamp(330 + files.Count * 82, 420, 720))
    {
        _id = id;
        _run = run;
        var video = id == "joinVideos";
        _list = new OrderList(files,
            video ? (path, ct) => ToolPreviews.VideoFrameAsync(path, 0.5, 160, ct) : (path, ct) => ToolPreviews.PdfPageAsync(path, 0, 120, ct),
            Geometry("Icon.FileOutline"),
            loc["tool.order.up"], loc["tool.order.down"], portrait: !video);
        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(Label(loc[video ? "tool.order.playback" : "tool.order.documents"]));
        body.Children.Add(_list);
        body.Children.Add(Note(loc[video ? "tool.joinVideos.note" : "tool.mergePDF.note"]));
        Body = body;
        PrimaryText = loc["tool." + id + ".run"];
        PrimaryEnabled = files.Count >= 2;
        if (files.Count < 2) Status = loc["tool.order.need_two"];
    }

    protected override Task<IReadOnlyList<string>> RunAsync(IProgress<double> progress, CancellationToken ct) =>
        _run(_id, _list.Paths, new ToolRequest(), this, progress, ct);
}
