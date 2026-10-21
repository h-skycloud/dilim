// Local-file picking and drops for the catalogue. Every invocation still goes through Filee's real pipeline.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Presets;

namespace Filee.App.Views.Pages;

public partial class CatalogPage : UserControl
{
    private void OnHelp(object? sender, RoutedEventArgs e) => AppHost.Get<DilimService>().ShowGuide();

    private async void OnOpenPie(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CatalogPageViewModel vm || !vm.HasFiles && !await ChooseFilesAsync()) return;
        var top = TopLevel.GetTopLevel(this)!;
        var at = top.PointToScreen(new Avalonia.Point(top.Bounds.Width / 2, top.Bounds.Height / 2));
        AppHost.Get<RadialController>().ShowForFiles(at.X, at.Y, vm.Files, mode: vm.SelectedMode?.Value ?? Filee.Core.Settings.PieMode.Convert);
    }
    public CatalogPage()
    {
        InitializeComponent();
        WireWheel(FrontWheel, back: false);
        WireWheel(BackWheel, back: true);
        DragDrop.AddDragEnterHandler(FileDropZone, (_, e) =>
        {
            FileDropZone.Classes.Add("over");
            e.DragEffects = e.DataTransfer.TryGetFiles() is not null ? DragDropEffects.Copy : DragDropEffects.None;
        });
        DragDrop.AddDragOverHandler(FileDropZone, (_, e) =>
            e.DragEffects = e.DataTransfer.TryGetFiles() is not null ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDragLeaveHandler(FileDropZone, (_, _) => FileDropZone.Classes.Remove("over"));
        DragDrop.AddDropHandler(FileDropZone, (_, e) =>
        {
            FileDropZone.Classes.Remove("over");
            if (DataContext is not CatalogPageViewModel vm)
                return;
            vm.SelectFiles(e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? []);
            e.DragEffects = vm.HasFiles ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
    }

    private void WireWheel(DonutMenu wheel, bool back)
    {
        wheel.SegmentInvoked += async (_, index) =>
        {
            if (DataContext is not CatalogPageViewModel vm)
                return;
            var entries = back ? vm.Back : vm.Front;
            if (index >= 0 && index < entries.Count)
                await InvokeAsync(entries[index].Preset, wheel);
        };
    }

    private async void OnChooseFiles(object? sender, RoutedEventArgs e) => await ChooseFilesAsync();

    private async Task<bool> ChooseFilesAsync()
    {
        if (DataContext is not CatalogPageViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return false;
        var chosen = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppHost.Get<Filee.Core.Localization.ILocalizer>()["catalog.choose_files"],
            AllowMultiple = true,
        });
        if (chosen.Count == 0)
            return false;
        vm.SelectFiles(chosen.Select(f => f.TryGetLocalPath()).OfType<string>());
        return vm.HasFiles;
    }

    private async Task InvokeAsync(Preset preset, Control? anchor = null)
    {
        if (DataContext is not CatalogPageViewModel vm)
            return;
        if (preset.IsGroup && anchor is not null)
        {
            var menu = new ContextMenu();
            var store = AppHost.Get<Filee.Core.Settings.UserDataStore>();
            var loc = AppHost.Get<Filee.Core.Localization.ILocalizer>();
            foreach (var child in preset.SubPresetIds.Select(store.FindPreset).OfType<Preset>().Where(p => !p.IsGroup))
            {
                var item = new MenuItem { Header = loc.DisplayName(child) };
                item.Click += async (_, _) => await InvokeAsync(child);
                menu.Items.Add(item);
            }
            menu.Open(anchor);
            return;
        }
        if (!vm.HasFiles && !await ChooseFilesAsync())
            return;
        vm.Invoke(preset);
        if (vm.CurrentJob is not null && !preset.IsTool)
            Dispatcher.UIThread.Post(() =>
            {
                JobResult.UpdateLayout();
                JobResult.BringIntoView();
            }, DispatcherPriority.Background);
    }

    private async void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CatalogEntry entry })
        {
            await InvokeAsync(entry.Preset, (Control)sender);
        }
    }
}
