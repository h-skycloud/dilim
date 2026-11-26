// Donuts for an app, named shelves, the user's own slices and exporting one donut.

using Dilim.Core.Presets;
using Dilim.Core.Profiles;
using Dilim.Core.Settings;
using Dilim.Core.Shelf;

namespace Dilim.Core.Tests;

public class FlexibilityTests
{
    [Fact]
    public void A_donut_for_an_app_wins_over_that_app_and_never_shows_elsewhere()
    {
        var general = new ToolbarProfile { Id = "images", Extensions = ["png", "jpg"] };
        var photoshop = new ToolbarProfile { Id = "ps", Extensions = ["png"], Apps = ["Photoshop"] };
        var mixed = new ToolbarProfile { Id = "mixed", IsFallback = true };
        var profiles = new List<ToolbarProfile> { photoshop, general, mixed };

        Assert.Equal("ps", ProfileSelector.Select(profiles, ["a.png"], "photoshop")!.Id);
        Assert.Equal("images", ProfileSelector.Select(profiles, ["a.png"], "explorer")!.Id);
        Assert.Equal("images", ProfileSelector.Select(profiles, ["a.png"])!.Id);
        Assert.Equal("images", ProfileSelector.Select(profiles, ["a.jpg"], "Photoshop")!.Id); // the app's donut lacks jpg
        Assert.Equal("mixed", ProfileSelector.Select(profiles, ["a.zip"], "Photoshop")!.Id);
        Assert.Equal(["Photoshop"], photoshop.Clone().Apps);
    }

    [Fact]
    public void Named_shelves_keep_their_own_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dilim-shelf-sets", Guid.NewGuid().ToString("N")[..8]);
        var a = Path.Combine(folder, "a.pdf");
        var b = Path.Combine(folder, "b.pdf");
        var shelf = new ShelfList(folder);
        shelf.Add([a]);
        shelf.Switch("Proje: A/sunum");
        Assert.Equal("Proje Asunum", shelf.Current);
        Assert.Empty(shelf.Paths);
        shelf.Add([b]);

        var reloaded = new ShelfList(folder);
        Assert.Equal([a], reloaded.Paths);
        Assert.Equal(["", "Proje Asunum"], reloaded.Sets);
        reloaded.Switch("Proje Asunum");
        Assert.Equal([b], reloaded.Paths);
        reloaded.DeleteSet("Proje Asunum");
        Assert.Equal("", reloaded.Current);
        Assert.Equal([a], reloaded.Paths);
        Assert.Equal([""], reloaded.Sets);
        Assert.Null(ShelfList.CleanSetName("  /:*  "));
    }

    [Fact]
    public void Custom_slices_fill_their_placeholders()
    {
        var files = new[] { Path.Combine("C:", "proje", "Vaziyet planı.pdf"), Path.Combine("C:", "proje", "Kesit.dwg") };
        Assert.Equal("https://www.google.com/search?q=Vaziyet%20plan%C4%B1",
            CustomAction.Expand("https://www.google.com/search?q={name}", files, url: true));
        Assert.Equal($"\"{files[0]}\" \"{files[1]}\" -o \"{Path.GetDirectoryName(files[0])}\"",
            CustomAction.Expand("{files} -o {folder}", files));
        Assert.Equal("CustomActions", nameof(AppSettings.CustomActions));

        Assert.Equal("tool.custom.error_name", new CustomAction { Target = "x" }.Problem());
        Assert.Equal("tool.custom.error_url", new CustomAction { Name = "Ara", Kind = CustomActionKind.OpenUrl, Target = "file:///c:/x" }.Problem());
        Assert.Null(new CustomAction { Name = "Ara", Kind = CustomActionKind.OpenUrl, Target = "https://example.com/?q={name}" }.Problem());

        var slices = DilimTools.ActionsWith([new CustomAction { Id = "abc", Name = "GIMP", Kind = CustomActionKind.OpenWith, Target = "gimp.exe" }]);
        Assert.Equal(DilimTools.Actions.Length + 1, slices.Count);
        Assert.Equal(("custom:abc", "GIMP", true), (slices[^1].ToolId, slices[^1].Name, slices[^1].IsTool));
    }

    [Fact]
    public void One_donut_exports_with_its_presets_and_imports_back()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dilim-pie-export", Guid.NewGuid().ToString("N")[..8]);
        var store = new UserDataStore(folder);
        store.Load();
        var pdf = store.Profiles.Single(p => p.Id == "pdf");
        var file = Path.Combine(folder, "pdf.dilim-pie.json");
        store.ExportProfile(pdf, file);

        var other = new UserDataStore(Path.Combine(folder, "other"));
        other.Load();
        other.Profiles.RemoveAll(p => p.Id == "pdf");
        other.Import(file);
        var imported = other.Profiles.Single(p => p.Id == "pdf");
        Assert.Equal(pdf.PresetIds, imported.PresetIds);
        Assert.All(imported.PresetIds, id => Assert.Contains(other.Presets, p => p.Id == id));
    }
}
