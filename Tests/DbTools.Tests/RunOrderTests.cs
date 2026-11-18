using System.Text;
using DbTools.Core;

namespace DbTools.Tests;

public class RunOrderTests
{
    private static List<string> Order(IEnumerable<SqlScriptFile> files) => RunOrder.AutoSort(files.ToList()).Files.Select(f => f.RelativePath).ToList();

    private static void Before(List<string> order, string first, string second) =>
        Assert.True(order.IndexOf(first) < order.IndexOf(second), $"{first} phải chạy trước {second}. Thứ tự: {string.Join(" → ", order)}");

    [Fact]
    public void Sample_folder_runs_dependencies_first()
    {
        using var folder = new TempFolder();
        SampleScripts.WriteTo(folder);

        var order = Order(SampleScripts.Load(folder));

        Assert.Equal(SampleScripts.Files.Count, order.Count);
        Before(order, @"SEQ\APP_ORDER_NO_SEQ.sql", @"OBJECT\APP_ORDER_ALL.sql");
        foreach (var dependent in new[]
                 {
                     @"OBJECT\APP_ORDER.sql", @"OBJECT\APP_ORDER_@S.sql", @"CHECK\APP_ORDER_ALL_CK01.sql",
                     @"COMMENT\APP_ORDER_ALL_comment.sql", @"CO_VT\APP_ORDER_ALL_TR_K.sql", @"HST_TR\APP_ORDER_ALL_TR_R.sql",
                 })
        {
            Before(order, @"OBJECT\APP_ORDER_ALL.sql", dependent);
        }
        Before(order, @"OBJECT\APP_ORDER.sql", @"COMMENT\APP_ORDER_comment.sql");
        // Dọn trước, ghi sau: DELETE dòng đăng ký cũ rồi mới INSERT dòng mới (cùng bảng app_history_table).
        Before(order, @"HST_TAB\deleteHistoryTab_APP1.sql", @"HST_TAB\APP_ORDER_ALL_R.sql");
        // Hàm trigger ghi vào bảng lịch sử _R.
        Before(order, @"HST_TAB\APP_ORDER_ALL_R.sql", @"HST_TR\APP_ORDER_ALL_TR_R.sql");
    }

    [Fact]
    public void Alphabetical_order_of_the_sample_would_be_wrong()
    {
        // Bảo đảm mẫu thử thật sự kiểm tra được việc sắp xếp: thứ tự vần ABC (như FOR /R) đặt CHECK trước bảng.
        var alphabetical = SampleScripts.Files.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(alphabetical.IndexOf(@"CHECK\APP_ORDER_ALL_CK01.sql") < alphabetical.IndexOf(@"OBJECT\APP_ORDER_ALL.sql"));
    }

    [Fact]
    public void Kind_rank_orders_independent_files()
    {
        var files = new[]
        {
            Parse("a_comment.sql", "COMMENT ON TABLE x IS 'a';"),
            Parse("b_view.sql", "CREATE VIEW v1 AS SELECT 1;"),
            Parse("c_table.sql", "CREATE TABLE t1 (a int);"),
            Parse("d_seq.sql", "CREATE SEQUENCE s1;"),
            Parse("e_data.sql", "INSERT INTO other VALUES (1);"),
            Parse("f_func.sql", "CREATE FUNCTION f1() RETURNS int AS 'select 1' LANGUAGE sql;"),
        };

        Assert.Equal(["d_seq.sql", "c_table.sql", "b_view.sql", "f_func.sql", "a_comment.sql", "e_data.sql"], Order(files));
    }

    [Fact]
    public void Leading_drop_does_not_decide_the_file_kind()
    {
        var file = Parse("t.sql", "DROP TABLE IF EXISTS t1; CREATE TABLE t1 (a int);");

        Assert.Equal(RunOrder.RankOf(StatementKind.CreateTable), file.Rank);
    }

    [Fact]
    public void Natural_order_for_same_kind()
    {
        var files = new[] { Parse("v10.sql", "CREATE TABLE a10 (x int);"), Parse("v2.sql", "CREATE TABLE a2 (x int);") };

        Assert.Equal(["v2.sql", "v10.sql"], Order(files));
    }

    [Fact]
    public void Cycle_is_broken_by_rank_and_noted()
    {
        var files = new[]
        {
            Parse("a.sql", "CREATE VIEW va AS SELECT * FROM vb;"),
            Parse("b.sql", "CREATE VIEW vb AS SELECT * FROM va;"),
        };

        var result = RunOrder.AutoSort(files);

        Assert.Equal(2, result.Files.Count);
        Assert.Single(result.Notes);
        Assert.Contains("Vòng phụ thuộc", result.Notes[0]);
    }

    [Fact]
    public void Saved_order_is_applied_and_new_files_go_last()
    {
        var files = new[]
        {
            Parse("a.sql", "CREATE TABLE a (x int);"),
            Parse("b.sql", "CREATE TABLE b (x int);"),
            Parse("c.sql", "CREATE TABLE c (x int);"),
        };

        var result = RunOrder.ApplySaved(files, ["C.SQL", "a.sql", "gone.sql"]);

        Assert.Equal(["c.sql", "a.sql", "b.sql"], result.Files.Select(f => f.RelativePath));
        Assert.Single(result.Notes);
        Assert.Contains("b.sql", result.Notes[0]);
    }

    private static SqlScriptFile Parse(string name, string sql) => SqlScriptFile.Parse(Encoding.UTF8.GetBytes(sql), name, name);
}
