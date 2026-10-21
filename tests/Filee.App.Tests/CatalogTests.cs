// Exercise the new catalogue against real files, persisted language settings, and missing engines.

using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.ViewModels.Pages;
using Filee.App.Views;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Settings;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;

namespace Filee.App.Tests;

public class CatalogTests
{
    [AvaloniaFact]
    public async Task Imported_preset_outside_the_pie_can_run_from_the_complete_library()
    {
        TestServices.EnsureInitialized("tr");
        var folder = Directory.CreateTempSubdirectory("dilim-import-library-").FullName;
        var store = AppHost.Get<UserDataStore>();
        var preset = new Filee.Core.Presets.Preset
        {
            Id = "fc-test-library",
            Name = "Imported WebP",
            Folder = "FileConverter/Photos",
            TargetFormat = "webp",
            SourceExtensions = ["png"],
            Output = { Location = Filee.Core.Presets.OutputLocation.SameFolder },
        };
        store.Presets.Add(preset);
        try
        {
            var source = Path.Combine(folder, "sample.png");
            using (var image = new MagickImage(MagickColors.Orange, 48, 32)) image.Write(source);
            using var vm = ActivatorUtilities.CreateInstance<CatalogPageViewModel>(AppHost.Services);
            vm.SelectFiles([source]);
            Assert.DoesNotContain(vm.Front.Concat(vm.Back), p => p.Preset.Id == preset.Id);
            vm.SelectedLibraryPreset = Assert.Single(vm.LibraryPresets, p => p.Value.Id == preset.Id);
            Assert.Contains(preset.Folder, vm.SelectedLibraryPreset.Label);
            vm.RunLibraryPresetCommand.Execute(null);
            var job = Assert.IsType<JobViewModel>(vm.CurrentJob);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (job.Job.State is JobState.Queued or JobState.Running) await Task.Delay(25, timeout.Token);
            Assert.Equal(JobState.Completed, job.Job.State);
            using var result = new MagickImage(Assert.Single(job.Job.Outputs));
            Assert.Equal(MagickFormat.WebP, result.Format);
            Assert.Equal(48u, result.Width);
        }
        finally
        {
            store.Presets.Remove(preset);
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void Quick_language_switch_is_persisted_and_keeps_selected_files()
    {
        TestServices.EnsureInitialized();
        using var vm = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        vm.Navigate("catalog");
        var page = Assert.IsType<CatalogPageViewModel>(vm.CurrentPage);
        var directory = Directory.CreateTempSubdirectory("filee-catalog-language-").FullName;
        try
        {
            var path = Path.Combine(directory, "ışık örneği.png");
            using (var image = new MagickImage(MagickColors.Orange, 36, 24))
                image.Write(path);
            page.SelectFiles([path]);
            vm.ChangeLanguageCommand.Execute("tr");
            Assert.Same(page, vm.CurrentPage);
            Assert.Equal([path], page.Files);
            Assert.Equal("Görseller", page.Title);
            Assert.True(vm.IsTurkish);
            var restored = new UserDataStore(TestServices.DataDirectory);
            restored.Load();
            Assert.Equal("tr", restored.Settings.Language);
            vm.ChangeLanguageCommand.Execute("en");
            Assert.Same(page, vm.CurrentPage);
            Assert.Equal([path], page.Files);
            Assert.Equal("Images", page.Title);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Catalogue_runs_a_real_image_conversion_without_changing_the_original_or_replacing_an_output()
    {
        TestServices.EnsureInitialized("tr");
        using var vm = ActivatorUtilities.CreateInstance<CatalogPageViewModel>(AppHost.Services);
        var directory = Directory.CreateTempSubdirectory("filee-catalog-convert-").FullName;
        try
        {
            var source = Path.Combine(directory, "örnek resim.png");
            using (var image = new MagickImage(MagickColors.Orange, 72, 48))
                image.Write(source);
            var original = SHA256.HashData(File.ReadAllBytes(source));
            vm.SelectFiles([source]);
            var preset = vm.Front.Single(e => e.Preset.Id == "to-webp").Preset.Clone();
            // Keep the test independent of other tests' default-output settings.
            preset.Output.Location = Filee.Core.Presets.OutputLocation.SameFolder;
            preset.Output.Conflict = Filee.Core.Presets.ConflictPolicy.Rename;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                vm.Invoke(preset);
                var job = Assert.IsType<JobViewModel>(vm.CurrentJob);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (job.Job.State is JobState.Queued or JobState.Running)
                    await Task.Delay(25, timeout.Token);
                Assert.Equal(JobState.Completed, job.Job.State);
                var output = Assert.Single(job.Job.Outputs);
                using var result = new MagickImage(output);
                Assert.Equal(MagickFormat.WebP, result.Format);
                Assert.Equal(72u, result.Width);
                Assert.Equal(48u, result.Height);
            }
            Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(source)));
            Assert.Equal(2, Directory.GetFiles(directory, "*.webp").Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void Removed_files_and_unavailable_engines_do_not_start_jobs()
    {
        TestServices.EnsureInitialized();
        using var vm = ActivatorUtilities.CreateInstance<CatalogPageViewModel>(AppHost.Services);
        var directory = Directory.CreateTempSubdirectory("filee-catalog-missing-").FullName;
        try
        {
            var source = Path.Combine(directory, "example.png");
            File.WriteAllBytes(source, [0]);
            vm.SelectFiles([source]);
            var preset = vm.Front.Single(e => e.Preset.Id == "to-webp").Preset;
            File.Delete(source);
            vm.Invoke(preset);
            Assert.Null(vm.CurrentJob);
            Assert.True(vm.HasMessage);

            File.WriteAllBytes(source, [0]);
            var catalog = new ConverterCatalog([]);
            var availability = new PresetAvailability(catalog, AppHost.Get<ILocalizer>(), AppHost.Get<EngineDownloadService>());
            using var unavailable = new CatalogPageViewModel(AppHost.Get<UserDataStore>(), AppHost.Get<ILocalizer>(), availability,
                catalog, AppHost.Get<ConversionService>(), AppHost.Get<WindowService>());
            unavailable.SelectFiles([source]);
            var entry = unavailable.Front.Single(e => e.Preset.Id == "to-webp");
            Assert.False(entry.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(entry.Detail));
            unavailable.Invoke(entry.Preset);
            Assert.Null(unavailable.CurrentJob);
            Assert.True(unavailable.HasMessage);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData("en", 1080)]
    [InlineData("tr", 1080)]
    [InlineData("tr", 820)]
    public void Catalogue_renders_at_normal_and_minimum_window_width(string language, int width)
    {
        TestServices.EnsureInitialized(language);
        using var model = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        var window = new MainWindow { DataContext = model, Width = width, Height = 820 };
        try
        {
            window.Show();
            RenderTests.Pump();
            Assert.IsType<CatalogPageViewModel>(model.CurrentPage);
            Assert.NotNull(window.FindControl<Button>("TurkishButton"));
            RenderTests.Save(window, $"catalog-{language}-{width}.png");
        }
        finally
        {
            window.Close();
            RenderTests.Pump();
        }
    }
}
