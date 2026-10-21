#:project ../../src/Filee.Engines/Filee.Engines.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Security.Cryptography;
using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines;
using Filee.Engines.Infrastructure;
using Filee.Engines.Media;
using ImageMagick;

if (args.Length < 2) throw new ArgumentException("settings.xml output-directory [import-data-directory]");
var imported = FileConverterImport.Read(args[0]);
var root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
var png = Path.Combine(root, "sample.png");
using (var image = new MagickImage("gradient:#F35B28-#124A66", 320, 240)) image.Write(png);
var engineRoot = Path.GetFullPath("engines/ffmpeg");
EngineEnvironment.OwnCopies = new Dictionary<string, string> { ["ffmpeg"] = engineRoot };
var ffmpeg = EngineEnvironment.OwnProgram("ffmpeg", "ffmpeg.exe") ?? throw new InvalidOperationException("Run from the repository after fetching FFmpeg.");
var mp4 = Path.Combine(root, "sample.mp4");
var wav = Path.Combine(root, "sample.wav");
foreach (var (output, input, codec) in new[] { (mp4, "testsrc2=s=320x240:r=24:d=1", "libx264"), (wav, "sine=frequency=440:duration=1", "pcm_s16le") })
{
    var made = await ProcessRunner.RunAsync(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", input, "-c", codec, output], TimeSpan.FromSeconds(30), default);
    if (made.ExitCode != 0) throw new IOException(made.StandardError);
}
var converters = EngineRegistry.CreateAll(new EngineEnvironment(Path.Combine(root, "data")));
var catalog = new ConverterCatalog(converters) { Priority = new AppSettings().EnginePriority };
await using var queue = new JobQueue(catalog, EngineRegistry.FindPdfMerger(converters), combiner: EngineRegistry.FindFileCombiner(converters), tiffMerger: EngineRegistry.FindTiffMerger(converters));
var results = new List<object>();
var failed = 0;
var pdfPreset = new Preset { TargetFormat = "pdf", Pdf = { MergeIntoSingle = true }, Output = { Location = OutputLocation.CustomFolder, CustomFolder = Path.Combine(root, "pdf-source") } };
var pdfJob = new ConversionJob([png, png], pdfPreset, "Generated PDF");
await queue.RunAsync(pdfJob);
if (pdfJob.State != JobState.Completed) throw new IOException("Cannot create the generated PDF sample.");
var pdf = pdfJob.Outputs.Single();
foreach (var original in imported.Presets)
{
    // Physical disc extraction is outside the owner's stated migration scope.
    if (original.Name.Contains("Extract DVD", StringComparison.OrdinalIgnoreCase) || original.Name.Contains("Extract CDA", StringComparison.OrdinalIgnoreCase))
    {
        results.Add(new { original.Id, original.Folder, original.Name, state = "hardware-not-tested" });
        continue;
    }
    var candidates = new[] { "aac", "flac", "wav", "mp3", "ogg" }.Contains(original.TargetFormat) ? new[] { wav, mp4 }
        : new[] { "mp4", "mkv", "avi", "webm", "ogv", "gif" }.Contains(original.TargetFormat) ? new[] { mp4, png } : new[] { png, pdf, mp4, wav };
    var source = candidates.FirstOrDefault(p => original.SourceExtensions.Contains(Path.GetExtension(p)[1..], StringComparer.OrdinalIgnoreCase));
    if (source is null) { results.Add(new { original.Id, original.Name, state = "no-generated-input" }); failed++; continue; }
    var hash = SHA256.HashData(File.ReadAllBytes(source));
    var preset = original.Clone();
    // Keep validation outputs and originals inside the scratch area, even for Music/Videos templates.
    preset.Output.Location = OutputLocation.CustomFolder;
    preset.Output.CustomFolder = Path.Combine(root, original.Id);
    preset.Output.AfterConversion = SourceAction.Keep;
    var job = new ConversionJob([source], preset, original.Name);
    await queue.RunAsync(job);
    var passed = job.State == JobState.Completed && job.Outputs.Any() && job.Outputs.All(File.Exists) && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)));
    if (!passed) failed++;
    results.Add(new { original.Id, original.Folder, original.Name, original.TargetFormat, state = passed ? "passed" : job.State.ToString(), outputs = job.Outputs.ToArray(), errors = job.Files.Where(f => f.ErrorDetail is not null).Select(f => f.ErrorDetail).ToArray() });
    Console.WriteLine(original.Folder + "/" + original.Name + ": " + (passed ? "passed" : "FAILED"));
}
if (args.Length > 2 && failed == 0)
{
    var store = new UserDataStore(Path.GetFullPath(args[2]));
    store.Load();
    foreach (var preset in imported.Presets.Where(p => store.Presets.All(existing => existing.Id != p.Id))) store.Presets.Add(preset);
    store.SaveLibrary();
    File.WriteAllLines(Path.Combine(store.Directory, "fileconverter-import-report.txt"), imported.Warnings);
}
File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { imported = imported.Presets.Count, imported.Warnings, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Imported: {imported.Presets.Count}, warnings: {imported.Warnings.Count}, failed: {failed}");
return failed == 0 ? 0 : 1;
