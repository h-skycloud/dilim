using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Filee.App.Tests;

public class DilimPieTests
{
    [AvaloniaFact]
    public void Three_modes_are_persisted_and_contextual_and_do_not_change_saved_profiles()
    {
        TestServices.EnsureInitialized("tr");
        var store = AppHost.Get<UserDataStore>();
        var radial = AppHost.Get<RadialViewModel>();
        var profileIds = store.Profiles.Select(p => string.Join(',', p.PresetIds)).ToArray();
        radial.Load(["test.png"], true, mode: PieMode.Tools);
        Assert.All(radial.Presets, p => Assert.True(p.IsTool));
        Assert.Contains(radial.Presets, p => p.ToolId == "annotateImage");
        radial.Load(["folder"], true, mode: PieMode.Actions);
        Assert.Equal(6, radial.Presets.Count);
        Assert.Contains(radial.Presets, p => p.ToolId == "move");
        radial.Load(["test.png"], true, mode: PieMode.Convert);
        Assert.Contains(radial.Presets, p => p.Id == "to-webp");
        Assert.Equal(profileIds, store.Profiles.Select(p => string.Join(',', p.PresetIds)));
        var testDir = Directory.CreateTempSubdirectory("filee-pie-gestures-").FullName;
        try
        {
            var data = new UserDataStore(testDir);
            DilimService.EnsureGestures(data);
            Assert.Equal(6, data.Settings.Triggers.Count);
            Assert.Equal(3, data.Settings.Triggers.Select(g => g.Menu).Distinct().Count());
            var restored = new UserDataStore(testDir); restored.Load();
            Assert.Equal(2, restored.Settings.Triggers.Count(g => g.Menu == PieMode.Actions));
            restored.Settings.Triggers[0].Key = "custom"; restored.SaveSettings();
            DilimService.EnsureGestures(restored);
            Assert.Equal("custom", restored.Settings.Triggers[0].Key);
        }
        finally { Directory.Delete(testDir, true); }
    }

    [AvaloniaFact]
    public void Catalogue_modes_and_language_keep_selection_and_tools_use_the_same_presets_as_pie()
    {
        TestServices.EnsureInitialized();
        using var vm = ActivatorUtilities.CreateInstance<CatalogPageViewModel>(AppHost.Services);
        var root = Directory.CreateTempSubdirectory("filee-pie-catalogue-").FullName;
        try
        {
            var source = Path.Combine(root, "test.png"); File.WriteAllBytes(source, [1]);
            vm.SelectFiles([source]); vm.SelectedMode = vm.MenuModes.Single(m => m.Value == PieMode.Tools);
            Assert.Contains(vm.Front, e => e.Preset.ToolId == "redactImage" || e.Preset.ToolId == "annotateImage");
            AppHost.Get<LocalizationService>().SetLanguage("tr"); vm.RefreshLanguage();
            Assert.Equal([source], vm.Files); Assert.Equal(PieMode.Tools, vm.SelectedMode!.Value);
            vm.SelectedMode = vm.MenuModes.Single(m => m.Value == PieMode.Actions);
            Assert.Equal(6, vm.Front.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Customized_gestures_are_not_replaced_on_upgrade()
    {
        var root = Directory.CreateTempSubdirectory("filee-pie-custom-").FullName;
        try
        {
            var store = new UserDataStore(root);
            store.Settings.Triggers[0].Modifiers = ModifierKeys.Meta;
            DilimService.EnsureGestures(store);
            Assert.Equal(2, store.Settings.Triggers.Count);
            Assert.Equal(ModifierKeys.Meta, store.Settings.Triggers[0].Modifiers);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Basic_tools_run_through_the_real_queue_for_images_pdf_and_archives()
    {
        TestServices.EnsureInitialized();
        var service = AppHost.Get<DilimService>();
        var root = Directory.CreateTempSubdirectory("filee-pie-basic-").FullName;
        try
        {
            var imageFile = Path.Combine(root, "source.png");
            using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Orange, 40, 20)) image.Write(imageFile);
            var pdfFile = Path.Combine(root, "source.pdf");
            using (var pdf = new PdfSharp.Pdf.PdfDocument()) { pdf.AddPage(); pdf.AddPage(); pdf.Save(pdfFile); }
            var zipFile = Path.Combine(root, "archive.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(zipFile, System.IO.Compression.ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("entry.txt").Open()); writer.Write("test"); }
            var imageHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(imageFile));
            foreach (var (id, files) in new (string, string[])[] { ("compress", [imageFile]), ("removeMetadata", [imageFile]), ("createPDF", [imageFile]), ("splitPDF", [pdfFile]), ("mergePDF", [pdfFile, pdfFile]), ("compress", [pdfFile]), ("extractArchive", [zipFile]) })
            {
                var job = Assert.IsType<JobViewModel>(service.StartConversion(id, files));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                while (job.Job.State is Filee.Core.Conversion.JobState.Queued or Filee.Core.Conversion.JobState.Running) await Task.Delay(30, timeout.Token);
                Assert.True(job.Job.State == Filee.Core.Conversion.JobState.Completed, id + ": " + job.ErrorDetails);
                Assert.NotEmpty(job.Job.Outputs);
                Assert.All(job.Job.Outputs, path => Assert.True(File.Exists(path) || Directory.Exists(path)));
                if (id == "splitPDF") Assert.Equal(2, job.Job.Outputs.Count());
                if (id == "mergePDF") { using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(job.Job.Outputs.Single()); Assert.Equal(4, pdf.PageCount); }
            }
            Assert.Equal(imageHash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(imageFile)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Move_preflights_conflicts_and_self_moves_without_overwriting_or_moving_other_sources()
    {
        var root = Directory.CreateTempSubdirectory("filee-pie-move-").FullName;
        try
        {
            var source = Path.Combine(root, "source"); var dest = Path.Combine(root, "dest"); Directory.CreateDirectory(source); Directory.CreateDirectory(dest);
            var a = Path.Combine(source, "a.txt"); var b = Path.Combine(source, "b.txt");
            File.WriteAllText(a, "a"); File.WriteAllText(b, "b"); File.WriteAllText(Path.Combine(dest, "b.txt"), "keep");
            Assert.Throws<IOException>(() => DilimService.MoveFiles([a, b], dest, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(a)); Assert.True(File.Exists(b)); Assert.Equal("keep", File.ReadAllText(Path.Combine(dest, "b.txt")));
            Assert.Throws<IOException>(() => DilimService.MoveFiles([source], source, TestContext.Current.CancellationToken));
            File.Delete(Path.Combine(dest, "b.txt")); DilimService.MoveFiles([a, b], dest, TestContext.Current.CancellationToken);
            Assert.False(File.Exists(a)); Assert.Equal("a", File.ReadAllText(Path.Combine(dest, "a.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
