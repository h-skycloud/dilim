using Avalonia.Headless.XUnit;
using Dilim.App.Services;
using Dilim.App.Views;
using Dilim.Core.Settings;
using Dilim.Core.Shelf;

namespace Dilim.App.Tests;

public class ShelfTests
{
    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dilim-shelf-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Touch(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void References_survive_a_restart_in_order_without_duplicates()
    {
        var data = TempFolder();
        var files = TempFolder();
        var a = Touch(files, "a.pdf");
        var b = Touch(files, "b.jpg");
        var shelf = new ShelfList(data);
        Assert.Equal(2, shelf.Add([b, a, b, "", "relative.txt"]));
        Assert.Equal(0, shelf.Add([a]));

        var again = new ShelfList(data);
        Assert.Equal([b, a], again.Paths);
    }

    [Fact]
    public void Removing_and_clearing_never_touch_the_files()
    {
        var files = TempFolder();
        var a = Touch(files, "a.pdf");
        var b = Touch(files, "b.jpg");
        var shelf = new ShelfList(TempFolder());
        shelf.Add([a, b]);
        Assert.True(shelf.Remove(a));
        shelf.Clear();
        Assert.Empty(shelf.Paths);
        Assert.True(File.Exists(a) && File.Exists(b));
    }

    [Fact]
    public void Files_moved_away_leave_the_shelf()
    {
        var files = TempFolder();
        var a = Touch(files, "a.pdf");
        var b = Touch(files, "b.jpg");
        var shelf = new ShelfList(null);
        shelf.Add([a, b]);
        File.Delete(a);
        Assert.Equal(1, shelf.RemoveMissing());
        Assert.Equal([b], shelf.Paths);
    }

    [Fact]
    public void Move_reorders()
    {
        var shelf = new ShelfList(null);
        var root = Path.GetTempPath();
        string P(string n) => Path.Combine(root, n);
        shelf.Add([P("1"), P("2"), P("3")]);
        shelf.Move(0, 2);
        Assert.Equal([P("2"), P("3"), P("1")], shelf.Paths);
        shelf.Move(5, 0);
        Assert.Equal([P("2"), P("3"), P("1")], shelf.Paths);
    }

    [AvaloniaTheory]
    [InlineData("tr", false, true)]
    [InlineData("tr", true, true)]
    [InlineData("en", false, false)]
    public void The_shelf_renders(string language, bool dark, bool withFiles)
    {
        TestServices.EnsureInitialized(language);
        var theme = AppHost.Get<ThemeService>();
        try
        {
            if (dark) theme.Apply(new ThemeSettings { Mode = ThemeMode.Dark, CornerRadius = 16 }, language);
            var files = TempFolder();
            var shelf = new ShelfList(TempFolder());
            var window = new ShelfWindow(AppHost.Get<Dilim.Core.Localization.ILocalizer>(), shelf);
            if (withFiles)
            {
                var sub = Path.Combine(files, "Proje çizimleri");
                Directory.CreateDirectory(sub);
                window.Add([Touch(files, "Vaziyet planı.pdf"), Touch(files, "Cephe render.jpg"), sub, Touch(files, "Sunum.mp4"), Path.Combine(files, "silinen.docx")]);
            }
            window.Show();
            RenderTests.Pump();
            RenderTests.Save(window, $"shelf-{language}{(dark ? "-dark" : "")}{(withFiles ? "" : "-empty")}.png");
            Assert.Equal(withFiles ? 5 : 0, shelf.Count);
            window.Hide();
        }
        finally
        {
            if (dark) theme.Apply(new ThemeSettings(), language);
        }
    }
}
