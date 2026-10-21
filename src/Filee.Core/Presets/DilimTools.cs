using Filee.Core.Formats;

namespace Filee.Core.Presets;

/// <summary>Context-sensitive tools. These transient presets never replace the user's conversion profiles.</summary>
public static class DilimTools
{
    public static readonly string[] Actions = ["share", "float", "copy", "move", "copyPath", "terminal"];
    public static readonly string[] All = ["compress", "removeMetadata", "editImage", "annotateImage", "frameImage", "cropImage", "normalizeAudio", "muteVideo", "trimVideo", "splitPDF", "organizePDF", "extractArchive", "createPDF", "mergePDF", "cropVideo", "changeVideoSpeed", "joinVideos", "videoSnapshots", "splitVideo", "audioToVideo", "trimAudio", "audioChannels", "createCollage", "redactImage", "redactVideo", "redactAudio"];

    public static bool NeedsMediaEngine(string id, IReadOnlyList<FileFormat> formats) =>
        formats.Any(f => f.Category is FormatCategory.Video or FormatCategory.Audio) && !Actions.Contains(id);

    public static Preset Make(string id) => new()
    {
        Id = "dilim-" + id,
        ToolId = id,
        NameKey = "tool." + id,
        Kind = PresetKind.Tool,
        TargetFormat = SameTarget,
    };

    private const string SameTarget = BuiltInData.SameAsSource;

    public static IReadOnlyList<Preset> For(IReadOnlyList<FileFormat> formats, int count)
    {
        if (formats.Count == 0 || count == 0) return [];
        var categories = formats.Select(f => f.Category).Distinct().ToArray();
        if (categories.Length != 1) return [];
        string[] ids = categories[0] switch
        {
            FormatCategory.Image => count > 1
                ? ["compress", "removeMetadata", "cropImage", "createPDF", "createCollage"]
                : ["compress", "removeMetadata", "editImage", "annotateImage", "frameImage", "cropImage", "redactImage", "createPDF"],
            FormatCategory.Video => count > 1
                ? ["compress", "removeMetadata", "muteVideo", "trimVideo", "cropVideo", "changeVideoSpeed", "joinVideos"]
                : ["compress", "removeMetadata", "muteVideo", "trimVideo", "cropVideo", "changeVideoSpeed", "videoSnapshots", "splitVideo", "redactVideo"],
            FormatCategory.Audio => ["compress", "removeMetadata", "normalizeAudio", "audioToVideo", "trimAudio", "audioChannels", "redactAudio"],
            FormatCategory.Pdf => count > 1
                ? ["compress", "removeMetadata", "splitPDF", "mergePDF", "organizePDF"]
                : ["compress", "removeMetadata", "splitPDF", "organizePDF"],
            FormatCategory.Archive => ["extractArchive"],
            FormatCategory.Document or FormatCategory.Presentation or FormatCategory.Spreadsheet => ["createPDF"],
            _ => [],
        };
        return ids.Select(Make).ToArray();
    }
}
