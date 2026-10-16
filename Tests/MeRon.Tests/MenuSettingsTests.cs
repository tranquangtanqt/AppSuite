using MeRon.Core;

namespace MeRon.Tests;

public class MenuSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MeRonMenuSettings_" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, MenuSettings.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_shows_everything()
    {
        var settings = MenuSettings.Load(FilePath);
        Assert.Empty(settings.Hidden);
        Assert.True(settings.IsVisible("AddressesPage"));
    }

    [Fact]
    public void Hidden_pages_survive_save_and_load()
    {
        var settings = new MenuSettings();
        settings.SetVisible("AddressesPage", false);
        settings.SetVisible("CategoriesPage", false);
        settings.SetVisible("CategoriesPage", true);
        settings.Save(FilePath);

        var loaded = MenuSettings.Load(FilePath);
        Assert.False(loaded.IsVisible("AddressesPage"));
        Assert.True(loaded.IsVisible("CategoriesPage"));
        Assert.Equal(["AddressesPage"], loaded.Hidden);
    }

    [Fact]
    public void Orders_page_cannot_be_hidden_even_from_file()
    {
        var settings = new MenuSettings();
        settings.SetVisible("OrdersPage", false);
        Assert.True(settings.IsVisible("OrdersPage"));

        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "HiddenMenu": ["OrdersPage", "ImportPage"] }""");
        var loaded = MenuSettings.Load(FilePath);
        Assert.True(loaded.IsVisible("OrdersPage"));
        Assert.False(loaded.IsVisible("ImportPage"));
    }

    [Fact]
    public void Broken_file_shows_everything_and_show_all_clears()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ không phải json");
        Assert.Empty(MenuSettings.Load(FilePath).Hidden);

        var settings = new MenuSettings();
        settings.SetVisible("ExportPage", false);
        settings.ShowAll();
        Assert.True(settings.IsVisible("ExportPage"));
    }
}
