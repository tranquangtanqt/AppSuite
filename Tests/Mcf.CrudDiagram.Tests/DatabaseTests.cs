using Mcf.CrudDiagram.HtmlGenerator.Models;
using Mcf.CrudDiagram.HtmlGenerator.Services;

namespace Mcf.CrudDiagram.Tests;

/// <summary>SQLite (Data\Database\02_CRUD図.db cạnh exe test) + trang index tìm kiếm.</summary>
public class DatabaseTests
{
    private static CrudRecord Record(string code, string name, string search) => new()
    {
        ScreenCode = code,
        ScreenName = name,
        ModuleId = "MSB",
        ModuleName = "販売管理",
        SubModuleId = "BB",
        SubModuleName = "受注",
        DocNumber = "M7-DV-0221",
        Version = "1.0",
        Revision = "02",
        SourceFile = @"C:\src\a.xlsx",
        SearchText = search,
        HtmlFileName = code + ".html",
        BlockCount = 3,
    };

    [Fact]
    public async Task Replace_all_then_read_back_sorted_by_code()
    {
        var db = new CrudDatabase();
        await db.ReplaceAllAsync([Record("MSBBB0030", "受注変更", "MAM_BP"), Record("MSBBB0020", "受注登録", "TSB_ORDER")]);
        await db.ReplaceAllAsync([Record("MSBBB0030", "受注変更", "MAM_BP"), Record("MSBBB0020", "受注登録", "TSB_ORDER")]);

        var all = db.GetAllLogics();

        // Ghi lần 2 thay thế toàn bộ, không cộng dồn.
        Assert.Equal(["MSBBB0020", "MSBBB0030"], all.Select(l => l.ScreenCode));
        var first = all[0];
        Assert.Equal(("受注登録", "販売管理", "M7-DV-0221", "TSB_ORDER", "MSBBB0020.html", 3),
            (first.ScreenName, first.ModuleName, first.DocNumber, first.SearchText, first.HtmlFileName, first.BlockCount));

        using var temp = new TempDir();
        string indexPath = new HtmlIndexGenerator().Generate(db, temp.Path);

        Assert.Equal("02_CRUD図.html", Path.GetFileName(indexPath));
        string html = File.ReadAllText(indexPath);
        Assert.Contains("\"code\":\"MSBBB0020\"", html);
        Assert.Contains("\"name\":\"受注登録\"", html); // tiếng Nhật giữ nguyên, không bị \uXXXX
        Assert.True(File.Exists(Path.Combine(temp.Path, "manifest.json")));
    }
}
