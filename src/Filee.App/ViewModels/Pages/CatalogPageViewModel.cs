// The working catalogue: the user's real toolbar profiles, availability and conversion jobs, in one place.

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;

namespace Filee.App.ViewModels.Pages;

/// <summary>One real toolbar profile in the category strip.</summary>
public sealed record CatalogCategory(string Id, string Title, Geometry? Icon);

/// <summary>A preset and its current availability, also exposed as a keyboard-accessible button below the wheels.</summary>
public sealed record CatalogEntry(Preset Preset, string Title, string Detail, bool IsEnabled);

/// <summary>Browse a profile, choose local files, then invoke a real preset on either face of its donut.</summary>
public sealed partial class CatalogPageViewModel : ObservableObject, IDisposable
{
    private readonly UserDataStore _store;
    private readonly ILocalizer _loc;
    private readonly PresetAvailability _availability;
    private readonly ConverterCatalog _catalog;
    private readonly ConversionService _conversions;
    private readonly WindowService _windows;
    private bool _refreshing;
    private bool _disposed;

    public CatalogPageViewModel(UserDataStore store, ILocalizer loc, PresetAvailability availability,
        ConverterCatalog catalog, ConversionService conversions, WindowService windows)
    {
        _store = store;
        _loc = loc;
        _availability = availability;
        _catalog = catalog;
        _conversions = conversions;
        _windows = windows;
        RefreshLanguage();
        store.LibraryChanged += OnCatalogChanged;
        store.SettingsChanged += OnCatalogChanged;
        catalog.Changed += OnCatalogChanged;
    }

    public ObservableCollection<CatalogCategory> Categories { get; } = [];
    public IReadOnlyList<Choice<PieMode>> MenuModes { get; private set; } = [];
    [ObservableProperty] private Choice<PieMode>? _selectedMode;
    partial void OnSelectedModeChanged(Choice<PieMode>? value) { if (!_refreshing) Rebuild(); }
    public IReadOnlyList<string> Files { get; private set; } = [];

    [ObservableProperty] private CatalogCategory? _selectedCategory;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _selectionText = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _centerLabel = "";
    [ObservableProperty] private string _gestureHint = "";
    [ObservableProperty] private string _frontLabel = "";
    [ObservableProperty] private string _backLabel = "";
    public int FrontSpan => HasBack ? 1 : 2;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private JobViewModel? _currentJob;
    [ObservableProperty] private IReadOnlyList<CatalogEntry> _front = [];
    [ObservableProperty] private IReadOnlyList<CatalogEntry> _back = [];
    [ObservableProperty] private IReadOnlyList<DonutItem> _frontItems = [];
    [ObservableProperty] private IReadOnlyList<DonutItem> _backItems = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasBack => Back.Count > 0;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    [ObservableProperty] private IReadOnlyList<Choice<Preset>> _libraryPresets = [];
    [ObservableProperty] private Choice<Preset>? _selectedLibraryPreset;
    [RelayCommand]
    private void RunLibraryPreset()
    {
        if (SelectedLibraryPreset is { } choice) Invoke(choice.Value);
    }

    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnSelectedCategoryChanged(CatalogCategory? value)
    {
        if (_refreshing || value is null)
            return;
        Files = [];
        Message = null;
        Rebuild();
    }

    /// <summary>Rebuild translated labels without losing a file selection or the selected category.</summary>
    public void RefreshLanguage()
    {
        _refreshing = true;
        var mode = SelectedMode?.Value ?? PieMode.Convert;
        MenuModes = Enum.GetValues<PieMode>().Select(m => new Choice<PieMode>(m, _loc["pie." + m])).ToArray();
        SelectedMode = MenuModes.First(m => m.Value == mode);
        OnPropertyChanged(nameof(MenuModes));
        var id = HasFiles ? ProfileSelector.Select(_store.Profiles, Files)?.Id : SelectedCategory?.Id;
        Categories.Clear();
        string[] order = ["images", "video", "audio", "pdf", "office", "presentations", "spreadsheets", "ebooks", "subtitles", "archives"];
        foreach (var profile in _store.Profiles.Where(p => !p.IsFallback)
                     .OrderBy(p => Array.IndexOf(order, p.Id) is var index && index >= 0 ? index : order.Length))
        {
            var icon = profile.Id switch
            {
                "images" => "Icon.Image",
                "video" => "Icon.Video",
                "audio" => "Icon.Audio",
                "archives" => "Icon.Folder",
                _ => "Icon.File",
            };
            Categories.Add(new CatalogCategory(profile.Id, _loc.DisplayName(profile), Application.Current?.FindResource(icon) as Geometry));
        }
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == id) ?? (HasFiles ? null : Categories.FirstOrDefault());
        _refreshing = false;
        Rebuild();
    }

    /// <summary>Accept existing local files only; browser downloads continue to use Filee's existing Home drop zone.</summary>
    public void SelectFiles(IEnumerable<string> paths)
    {
        Files = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        if (Files.Count == 0)
        {
            Message = _loc["catalog.local_files_only"];
            Rebuild();
            return;
        }
        var profile = ProfileSelector.Select(_store.Profiles, Files);
        _refreshing = true;
        // Mixed-selection profiles need not appear among the normal category tabs.
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == profile?.Id);
        _refreshing = false;
        Message = null;
        Rebuild();
    }

    [RelayCommand]
    private void ClearFiles()
    {
        Files = [];
        SelectedCategory ??= Categories.FirstOrDefault();
        Message = null;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_disposed)
            return;
        var profile = HasFiles
            ? ProfileSelector.Select(_store.Profiles, Files)
            : _store.Profiles.FirstOrDefault(p => p.Id == SelectedCategory?.Id);
        var sample = profile?.Extensions.Select(FormatRegistry.FindByExtension).OfType<FileFormat>().FirstOrDefault();
        var formats = HasFiles ? ProfileSelector.DetectFormats(Files) : sample is null ? [] : new[] { sample };
        Title = profile is null ? _loc["catalog.title"] : _loc.DisplayName(profile);
        if (SelectedMode?.Value is PieMode.Tools or PieMode.Actions) Title = _loc["pie." + SelectedMode.Value];
        FrontLabel = SelectedMode?.Value == PieMode.Convert ? _loc["catalog.front"] : _loc["pie." + (SelectedMode?.Value ?? PieMode.Tools)];
        BackLabel = SelectedMode?.Value == PieMode.Tools ? _loc["pie.more_tools"] : _loc["catalog.back"];
        SelectionText = HasFiles
            ? Files.Count == 1 ? Path.GetFileName(Files[0]) : _loc.Format("home.files", Files.Count)
            : _loc["catalog.no_files"];
        SourceText = HasFiles ? string.Join(" · ", formats.Select(f => f.DisplayName))
            : _loc.Format("catalog.preview_source", sample?.DisplayName ?? "—");
        CenterLabel = formats.Count == 1 ? formats[0].DisplayName : _loc.Format("home.files", Files.Count);
        var drag = _store.Settings.Triggers.FirstOrDefault(t => t.Enabled && t.Kind == TriggerKind.Drag && t.Menu == (SelectedMode?.Value ?? PieMode.Convert));
        var flipKey = _store.Settings.Donut.EffectiveFlipKey(_store.Settings.Triggers);
        var flipHint = flipKey is not null ? _loc.Format("catalog.flip_key", KeyNames.Display(flipKey))
            : _store.Settings.Donut.FlipWithWheel ? _loc["catalog.flip_wheel"] : "";
        GestureHint = drag is null ? _loc["catalog.no_gesture"]
            : $"{_loc.Format("catalog.gesture", GestureText.Modifiers(drag.Modifiers))} {flipHint}".Trim();
        Front = Entries(profile?.PresetIds ?? [], formats);
        Back = Entries(profile?.BackPresetIds ?? [], formats);
        if (SelectedMode?.Value is PieMode.Tools or PieMode.Actions)
        {
            var presets = SelectedMode.Value == PieMode.Actions ? DilimTools.Actions.Select(DilimTools.Make).ToArray()
                : DilimTools.For(formats, HasFiles ? Files.Count : 1);
            Front = EntriesOf(presets.Take(6), formats);
            Back = EntriesOf(presets.Skip(6), formats);
        }
        FrontItems = Items(Front);
        BackItems = Items(Back);
        var selectedId = SelectedLibraryPreset?.Value.Id;
        LibraryPresets = _store.Presets.Where(p => !p.IsGroup && !p.IsTool && _availability.Check(p, formats) is null)
            .OrderBy(p => p.Folder).ThenBy(p => _loc.DisplayName(p))
            .Select(p => new Choice<Preset>(p, string.IsNullOrWhiteSpace(p.Folder) ? _loc.DisplayName(p) : p.Folder + " / " + _loc.DisplayName(p))).ToArray();
        SelectedLibraryPreset = LibraryPresets.FirstOrDefault(p => p.Value.Id == selectedId) ?? LibraryPresets.FirstOrDefault();
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasBack));
        OnPropertyChanged(nameof(FrontSpan));
    }

    private IReadOnlyList<CatalogEntry> Entries(IEnumerable<string> ids, IReadOnlyList<FileFormat> formats) =>
        EntriesOf(ids.Select(_store.FindPreset).OfType<Preset>(), formats);

    private IReadOnlyList<CatalogEntry> EntriesOf(IEnumerable<Preset> presets, IReadOnlyList<FileFormat> formats) =>
        presets.Take(ToolbarProfile.MaxSlices).Select(p =>
        {
            var reason = p.IsGroup ? null : _availability.Check(p, formats);
            var children = p.SubPresetIds.Select(_store.FindPreset).OfType<Preset>().Where(c => !c.IsGroup).ToArray();
            if (p.IsGroup && !children.Any(c => _availability.Check(c, formats) is null))
                reason = _loc["error.no_route"];
            return new CatalogEntry(p, _loc.DisplayName(p), reason ?? (p.IsGroup ? _loc.Format("donut.group_caption", children.Length)
                : p.IsTool ? _loc["catalog.interactive"] : _loc["catalog.ready"]), reason is null);
        }).ToArray();

    private IReadOnlyList<DonutItem> Items(IReadOnlyList<CatalogEntry> entries) => entries.Select(entry => new DonutItem
    {
        Id = entry.Preset.Id,
        Label = _loc.RingName(entry.Preset),
        IsEnabled = entry.IsEnabled,
        DisabledReason = entry.IsEnabled ? null : entry.Detail,
        // The catalogue opens groups as accessible flyouts. No outside ring is reserved, so both previews stay
        // the same size. The global donut retains its existing expanding group rings.
        Caption = entry.Preset.IsGroup ? "▾" : null,
    }).ToArray();

    /// <summary>Invokes only a current, available leaf preset after a local file selection.</summary>
    public void Invoke(Preset preset)
    {
        if (!HasFiles)
            return;
        // Re-check at invocation: a selected file can disappear, or an engine can have changed since rendering.
        if (Files.Any(path => !File.Exists(path) && !Directory.Exists(path)))
        {
            Message = _loc["catalog.files_missing"];
            return;
        }
        if (_availability.Check(preset, ProfileSelector.DetectFormats(Files)) is { } reason)
        {
            Message = reason;
            return;
        }
        Message = null;
        if (preset.IsTool)
        {
            if (preset.ToolId is { } tool) AppHost.Get<DilimService>().Show(tool, Files);
            else _windows.ShowCropOverlay(Files, preset.Clone());
        }
        else if (!preset.IsGroup)
            CurrentJob = _conversions.Start(Files, preset.Clone());
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
            RefreshLanguage();
    });

    public void Dispose()
    {
        _disposed = true;
        _store.LibraryChanged -= OnCatalogChanged;
        _store.SettingsChanged -= OnCatalogChanged;
        _catalog.Changed -= OnCatalogChanged;
    }
}
