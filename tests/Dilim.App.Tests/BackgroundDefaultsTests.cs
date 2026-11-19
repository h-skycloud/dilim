using Dilim.App.Services;
using Dilim.Core.Settings;

namespace Dilim.App.Tests;

public class BackgroundDefaultsTests
{
    private static UserDataStore Store(Action<AppSettings>? before = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dilim-background-tests", Guid.NewGuid().ToString("N")[..8]);
        var store = new UserDataStore(dir);
        store.Load();
        before?.Invoke(store.Settings);
        return store;
    }

    [Fact]
    public void Settings_with_the_old_defaults_start_with_windows_and_stay_in_the_tray()
    {
        var store = Store(s => { s.StartWithSystem = false; s.CloseAction = CloseAction.Ask; });
        DilimService.EnsureBackgroundDefaults(store);
        Assert.True(store.Settings.StartWithSystem);
        Assert.Equal(CloseAction.Tray, store.Settings.CloseAction);
        Assert.True(store.Settings.BackgroundDefaultsApplied);
    }

    [Fact]
    public void It_happens_once_and_later_choices_are_kept()
    {
        var store = Store(s => s.CloseAction = CloseAction.Quit);
        DilimService.EnsureBackgroundDefaults(store);
        Assert.Equal(CloseAction.Quit, store.Settings.CloseAction);
        store.Settings.StartWithSystem = false;
        DilimService.EnsureBackgroundDefaults(store);
        Assert.False(store.Settings.StartWithSystem);
    }

    [Fact]
    public void New_settings_start_with_windows_and_close_to_the_tray()
    {
        var settings = new AppSettings();
        Assert.True(settings.StartWithSystem);
        Assert.Equal(CloseAction.Tray, settings.CloseAction);
    }
}
