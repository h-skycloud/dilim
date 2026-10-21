using Filee.Core.Presets;

namespace Filee.Core.Conversion;

/// <summary>Opt-in source management after a completely successful local job.</summary>
internal static class SourceDisposition
{
    public static void Apply(ConversionJob job)
    {
        var rule = job.Preset.Output;
        if (rule.AfterConversion == SourceAction.Keep || job.State != JobState.Completed || job.Fetcher is not null || job.CancellationToken.IsCancellationRequested)
            return;
        var outputs = job.Outputs.Select(Path.GetFullPath).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (outputs.Count == 0 || outputs.Any(p => !File.Exists(p) && !Directory.Exists(p))) return;
        var sources = job.Files.Where(f => f.State == FileState.Done && File.Exists(f.SourcePath))
            .Select(f => Path.GetFullPath(f.SourcePath)).Distinct(outputs.Comparer).ToArray();
        if (sources.Any(outputs.Contains))
        {
            foreach (var file in job.Files) file.NoticeKey = "notice.source_action_skipped";
            return;
        }
        if (rule.AfterConversion == SourceAction.Delete)
        {
            foreach (var path in sources) File.Delete(path);
            return;
        }
        if (rule.AfterConversion != SourceAction.Archive) return;
        var folder = rule.ArchiveFolderName.Trim();
        if (folder.Length == 0 || folder is "." or ".." || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder.Contains('/') || folder.Contains('\\'))
            throw new IOException("Invalid original archive folder name.");
        var planned = new List<(string Source, string Target)>();
        var reserved = new HashSet<string>(outputs.Comparer);
        foreach (var source in sources)
        {
            var parent = Path.Combine(Path.GetDirectoryName(source)!, folder);
            if (File.Exists(parent)) throw new IOException("Original archive location is a file.");
            var target = Path.Combine(parent, Path.GetFileName(source));
            for (var n = 2; File.Exists(target) || Directory.Exists(target) || reserved.Contains(target); n++)
                target = Path.Combine(parent, $"{Path.GetFileNameWithoutExtension(source)} ({n}){Path.GetExtension(source)}");
            reserved.Add(target);
            planned.Add((source, target));
        }
        foreach (var (source, target) in planned)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target);
        }
    }
}
