// Main (settings) window: left navigation + current page.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.App.Services.Triggers;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Filee.App.ViewModels;

/// <summary>Navigation entry. <see cref="Title"/> follows the UI language.</summary>
public sealed partial class NavItem(string key, string titleKey, string iconKey, Type pageType) : ObservableObject
{
    public string Key { get; } = key;
    public string TitleKey { get; } = titleKey;
    public Geometry? Icon { get; } = Application.Current?.FindResource(iconKey) as Geometry;
    public Type PageType { get; } = pageType;

    [ObservableProperty] private string _title = "";

    public void Refresh(ILocalizer loc) => Title = loc[TitleKey];
}

/// <summary>
/// One per main window (<see cref="WindowService.ShowMain"/>); disposed with it, which drops its subscriptions on the
/// long-lived services and the current page.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly TriggerService _triggers;
    private bool _disposed;

    /// <summary>The repository, for the GitHub button above the version.</summary>
    public string RepositoryUrl => UpdateService.RepositoryUrl;

    public MainWindowViewModel(IServiceProvider services, ILocalizer loc, UpdateService updates)
    {
        _services = services;
        _loc = loc;
        _store = services.GetRequiredService<UserDataStore>();
        _triggers = services.GetRequiredService<TriggerService>();
        _store.SettingsChanged += OnStatusChanged;
        _triggers.StatusChanged += OnStatusChanged;
        Updates = updates;
        updates.PropertyChanged += OnUpdatesChanged;
        RefreshVersionTexts();
        Items =
        [
            new("catalog", "nav.catalog", "Icon.Shapes", typeof(CatalogPageViewModel)),
            new("home", "nav.home", "Icon.Home", typeof(HomePageViewModel)),
            new("general", "nav.general", "Icon.Settings", typeof(GeneralPageViewModel)),
            new("triggers", "nav.triggers", "Icon.Keyboard", typeof(TriggersPageViewModel)),
            new("toolbar", "nav.toolbar", "Icon.Donut", typeof(ToolbarPageViewModel)),
            new("presets", "nav.presets", "Icon.Tune", typeof(PresetsPageViewModel)),
            new("watch", "nav.watch", "Icon.Folder", typeof(WatchFoldersPageViewModel)),
            new("theme", "nav.theme", "Icon.Palette", typeof(ThemePageViewModel)),
            new("engines", "nav.engines", "Icon.Engine", typeof(EnginesPageViewModel)),
            new("about", "nav.about", "Icon.Info", typeof(AboutPageViewModel)),
        ];
        foreach (var item in Items)
            item.Refresh(loc);
        loc.LanguageChanged += OnLanguageChanged;
        _selectedItem = Items[0];
        _currentPage = CreatePage(Items[0]);
    }

    public IReadOnlyList<NavItem> Items { get; }

    /// <summary>Quick language controls mirror the persisted setting, including changes from General.</summary>
    public bool IsEnglish => _loc.Language == "en";
    public bool IsTurkish => _loc.Language == "tr";

    /// <summary>The real global-hook state, rather than the prototype's decorative tray badge.</summary>
    public string CaptureStatus => _store.Settings.Paused ? _loc["triggers.paused"]
        : _triggers.IsRunning ? _loc["shell.capture_ready"] : _loc["shell.capture_unavailable"];

    [RelayCommand]
    private void ChangeLanguage(string code)
    {
        if (code is not ("en" or "tr"))
            return;
        _store.Settings.Language = code;
        _store.SaveSettings();
        // App normally applies SettingsChanged. Explicitly updating also keeps a standalone/test window usable.
        if (_loc is LocalizationService language && language.NeedsUpdate(code))
            language.SetLanguage(code);
    }

    private void OnStatusChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CaptureStatus));

    /// <summary>Update state for the bottom of the navigation (version, "update available" button).</summary>
    public UpdateService Updates { get; }

    [ObservableProperty] private NavItem _selectedItem;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageScroll))]
    private ObservableObject _currentPage;

    /// <summary>
    /// Pages scroll as a whole, except pages that fit the window height and scroll their own parts
    /// (<see cref="IFitsWindowHeight"/>, e.g. the presets list and editor).
    /// </summary>
    public ScrollBarVisibility PageScroll => CurrentPage is IFitsWindowHeight ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    [ObservableProperty] private string _versionText = "";
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private string _updateTip = "";

    /// <summary>Downloads and runs the new installer (or opens the download page in a portable copy).</summary>
    [RelayCommand]
    private Task DownloadUpdate() => Updates.UpdateAsync();

    private void OnUpdatesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (UpdateTexts.Affects(e.PropertyName))
            RefreshVersionTexts();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Items)
            item.Refresh(_loc);
        RefreshVersionTexts();
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsTurkish));
        OnPropertyChanged(nameof(CaptureStatus));
        if (CurrentPage is CatalogPageViewModel catalog)
            catalog.RefreshLanguage();
        else
            CurrentPage = CreatePage(SelectedItem); // rebuild so code-generated labels update too
    }

    private void RefreshVersionTexts()
    {
        VersionText = _loc.Format("about.version", UpdateService.CurrentVersion);
        UpdateText = UpdateTexts.Short(Updates, _loc);
        var changes = UpdateTexts.Changes(Updates, _loc);
        UpdateTip = UpdateTexts.Status(Updates, _loc)
                    ?? $"{_loc[$"update.level.{Updates.Level}"]}{Environment.NewLine}{(changes.Length > 0 ? changes + Environment.NewLine : "")}{_loc[UpdateTexts.HowKey]}";
    }

    public void Navigate(string key)
    {
        var item = Items.FirstOrDefault(i => i.Key == key);
        if (item is not null)
            SelectedItem = item;
    }

    // Pages are recreated on every visit so they always reflect the latest settings. A navigation list that loses its
    // selection keeps the open page (Choices.Restore).
    partial void OnSelectedItemChanged(NavItem? oldValue, NavItem newValue)
    {
        if (!Choices.Restore(oldValue, newValue, v => SelectedItem = v))
            CurrentPage = CreatePage(newValue);
    }

    // Old pages may hold event subscriptions on long-lived services.
    partial void OnCurrentPageChanged(ObservableObject? oldValue, ObservableObject newValue) => (oldValue as IDisposable)?.Dispose();

    private ObservableObject CreatePage(NavItem item) =>
        (ObservableObject)ActivatorUtilities.CreateInstance(_services, item.PageType);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Updates.PropertyChanged -= OnUpdatesChanged;
        _store.SettingsChanged -= OnStatusChanged;
        _triggers.StatusChanged -= OnStatusChanged;
        _loc.LanguageChanged -= OnLanguageChanged;
        (CurrentPage as IDisposable)?.Dispose();
    }
}
