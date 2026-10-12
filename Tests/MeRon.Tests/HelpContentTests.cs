using MeRon.Core;

namespace MeRon.Tests;

public class HelpContentTests
{
    [Fact]
    public void Every_menu_page_has_exactly_one_help_section()
    {
        foreach (var page in HelpContent.Pages)
        {
            Assert.Single(HelpContent.Sections, s => s.PageTag == page);
        }
        Assert.All(HelpContent.Sections.Where(s => s.PageTag is not null), s => Assert.Contains(s.PageTag, HelpContent.Pages));
    }

    [Fact]
    public void Every_page_listed_in_help_exists_as_a_view()
    {
        var views = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Modules", "MeRon", "Views");
        foreach (var page in HelpContent.Pages)
        {
            Assert.True(File.Exists(Path.Combine(views, page + ".xaml")), $"Thiếu Views\\{page}.xaml");
        }
    }

    [Fact]
    public void Sections_have_content_and_unique_titles()
    {
        Assert.Equal(HelpContent.Sections.Count, HelpContent.Sections.Select(s => s.Title).Distinct().Count());
        Assert.All(HelpContent.Sections, s => Assert.NotEmpty(s.Items));
        Assert.Equal("Khách hàng", HelpContent.ForPage("CustomersPage").Title);
        Assert.Equal(HelpContent.Sections[0], HelpContent.ForPage("KhongCo"));
    }
}
