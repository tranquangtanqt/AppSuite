using FileTools.Core;

namespace FileTools.Tests;

public class HelpContentTests
{
    /// <summary>Thư mục Views của module, tính từ bin của project test (…\Tests\FileTools.Tests\bin\…).</summary>
    private static string ViewsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Modules", "FileTools", "Views")))
        {
            dir = dir.Parent;
        }
        return dir is null ? string.Empty : Path.Combine(dir.FullName, "Modules", "FileTools", "Views");
    }

    [Fact]
    public void Every_page_has_exactly_one_help_section()
    {
        var folder = ViewsFolder();
        // Chạy từ bản publish trong Sandbox thì không có mã nguồn - bỏ qua kiểm tra này.
        Assert.SkipWhen(folder.Length == 0, "Không tìm thấy mã nguồn Views (chạy từ bản publish).");
        var pages = Directory.GetFiles(folder, "*Page.xaml").Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.NotEmpty(pages);
        foreach (var page in pages)
        {
            Assert.Single(HelpContent.Sections, s => s.PageTag == page);
        }
        Assert.All(HelpContent.Sections.Where(s => s.PageTag is not null), s => Assert.Contains(s.PageTag, pages));
    }

    [Fact]
    public void Sections_are_filled_in()
    {
        Assert.All(HelpContent.Sections, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Summary));
            Assert.NotEmpty(s.Items);
            Assert.All(s.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
        });
    }

    [Fact]
    public void For_page_falls_back_to_first_section()
    {
        Assert.Equal("CsvSplitPage", HelpContent.ForPage("CsvSplitPage").PageTag);
        Assert.Same(HelpContent.Sections[0], HelpContent.ForPage("KhongCo"));
        Assert.Same(HelpContent.Sections[0], HelpContent.ForPage(null));
    }

    [Theory]
    [InlineData("tach cot", "CSV: Tách theo cột")]
    [InlineData("SHIFT-JIS", "Đổi encoding / xuống dòng")]
    [InlineData("healthcheck", "Tìm / Lọc dòng")]
    [InlineData("tsv", "CSV: Chọn cột / đổi dấu phân cách")]
    [InlineData("thung rac", "Tìm file trùng")]
    [InlineData("tail -f", "Theo dõi log")]
    public void Search_ignores_case_and_diacritics(string query, string expectedSection)
    {
        var results = HelpContent.Search(query);

        Assert.Contains(results, r => r.Section.Title == expectedSection);
    }

    [Fact]
    public void Search_requires_every_word()
    {
        Assert.Empty(HelpContent.Search("tach cot xyzkhongco"));
        Assert.Empty(HelpContent.Search("   "));
    }
}
