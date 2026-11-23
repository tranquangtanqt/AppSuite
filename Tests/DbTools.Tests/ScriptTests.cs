using System.Text;
using DbTools.Core;

namespace DbTools.Tests;

public class DropScriptTests
{
    private static SqlScriptFile Parse(string name, string sql) => SqlScriptFile.Parse(Encoding.UTF8.GetBytes(sql), name, name);

    [Fact]
    public void Sample_drop_script_reverses_run_order()
    {
        using var folder = new TempFolder();
        SampleScripts.WriteTo(folder);
        var ordered = RunOrder.AutoSort(SampleScripts.Load(folder)).Files;

        var drop = DropScript.Build(ordered);
        var sql = drop.Statements.Select(s => s.Sql).ToList();

        // Ràng buộc / index trên bảng do thư mục tạo: xoá bảng là mất theo - không có câu riêng.
        Assert.DoesNotContain(sql, s => s.Contains("DROP CONSTRAINT") || s.StartsWith("DROP INDEX"));
        // Trigger thì xoá riêng, trước hàm trigger (hàm còn trigger dùng thì không DROP được).
        Assert.True(sql.IndexOf("DROP TRIGGER IF EXISTS app_order_all_tr_r ON app_order_all") < sql.IndexOf("DROP FUNCTION IF EXISTS app_order_all_tr_r_body()"));
        Assert.Contains("DROP FUNCTION IF EXISTS app_order_all_tr_r_body()", sql);
        Assert.Contains("DROP VIEW IF EXISTS app_order_$s", sql);
        Assert.Contains("DROP SEQUENCE IF EXISTS app_order_no_seq", sql);
        // View xoá trước bảng nó dùng; bảng xoá trước sequence.
        Assert.True(sql.IndexOf("DROP VIEW IF EXISTS app_order") < sql.IndexOf("DROP TABLE IF EXISTS app_order_all"));
        Assert.True(sql.IndexOf("DROP TABLE IF EXISTS app_order_all") < sql.IndexOf("DROP SEQUENCE IF EXISTS app_order_no_seq"));
        Assert.Equal(["app_order_all_r", "app_order_all"], drop.Tables.Select(t => t.Key).Order().Reverse());
        // DELETE / INSERT dữ liệu không đảo ngược được → liệt kê để xem tay.
        Assert.Equal(2, drop.NotReversible.Count);
        Assert.DoesNotContain(sql, s => s.Contains("CASCADE"));
    }

    [Fact]
    public void Objects_on_existing_tables_get_their_own_drop()
    {
        var files = new[]
        {
            Parse("a.sql", """
                CREATE OR REPLACE FUNCTION f(a int DEFAULT 1) RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER tr1 BEFORE INSERT ON base_table FOR EACH ROW EXECUTE FUNCTION f();
                ALTER TABLE base_table ADD CONSTRAINT base_ck CHECK (x > 0);
                CREATE INDEX base_ix ON base_table (x);
                """),
        };

        var sql = DropScript.Build(files).Statements.Select(s => s.Sql).ToList();

        Assert.Equal(
            [
                "DROP INDEX IF EXISTS base_ix",
                "ALTER TABLE IF EXISTS base_table DROP CONSTRAINT IF EXISTS base_ck",
                "DROP TRIGGER IF EXISTS tr1 ON base_table",
                "DROP FUNCTION IF EXISTS f(a int)",
            ], sql);
    }

    [Fact]
    public void Chosen_schema_qualifies_own_objects_only()
    {
        var files = new[]
        {
            Parse("a.sql", """
                CREATE TABLE t1 (a int);
                CREATE TABLE other.t2 (a int);
                CREATE VIEW v1 AS SELECT * FROM t1;
                CREATE OR REPLACE FUNCTION f() RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER tr1 BEFORE INSERT ON t1 FOR EACH ROW EXECUTE FUNCTION f();
                CREATE TRIGGER tr2 BEFORE INSERT ON base_table FOR EACH ROW EXECUTE FUNCTION f();
                CREATE INDEX base_ix ON base_table (x);
                CREATE SEQUENCE s1;
                """),
        };

        var drop = DropScript.Build(files, "Addon");
        var sql = drop.Statements.Select(s => s.Sql).ToList();

        Assert.Equal(
            [
                "DROP SEQUENCE IF EXISTS \"Addon\".s1",
                "DROP INDEX IF EXISTS base_ix",                          // index trên bảng có sẵn: tìm theo search_path như lúc tạo
                "DROP TRIGGER IF EXISTS tr2 ON base_table",              // bảng có sẵn: giữ nguyên
                "DROP TRIGGER IF EXISTS tr1 ON \"Addon\".t1",            // bảng do thư mục tạo: ghi rõ schema
                "DROP FUNCTION IF EXISTS \"Addon\".f()",
                "DROP VIEW IF EXISTS \"Addon\".v1",
                "DROP TABLE IF EXISTS other.t2",                         // file đã ghi schema → theo file
                "DROP TABLE IF EXISTS \"Addon\".t1",
            ], sql);
        Assert.Equal(["\"Addon\".t1", "other.t2"], drop.Tables.Select(t => t.Sql).Order());
        Assert.DoesNotContain(DropScript.Build(files).Statements, s => s.Sql.Contains("Addon"));
    }

    [Fact]
    public void Object_created_twice_is_dropped_once()
    {
        var files = new[] { Parse("a.sql", "CREATE OR REPLACE VIEW v AS SELECT 1;"), Parse("b.sql", "CREATE OR REPLACE VIEW v AS SELECT 2;") };

        Assert.Single(DropScript.Build(files).Statements);
    }

    [Fact]
    public void Materialized_view_and_quoted_names()
    {
        var files = new[] { Parse("a.sql", "CREATE TABLE \"Big Table\" (a int); CREATE MATERIALIZED VIEW mv AS SELECT 1;") };

        var sql = DropScript.Build(files).Statements.Select(s => s.Sql).ToList();

        Assert.Equal(["DROP MATERIALIZED VIEW IF EXISTS mv", "DROP TABLE IF EXISTS \"Big Table\""], sql);
    }
}

public class PrecheckTests
{
    private static SqlScriptFile Parse(string sql) => SqlScriptFile.Parse(Encoding.UTF8.GetBytes(sql), "a.sql", "a.sql");

    [Fact]
    public void Existing_object_with_plain_create_is_a_warning()
    {
        var file = Parse("CREATE TABLE t1 (a int);\nCREATE OR REPLACE VIEW v1 AS SELECT 1;\nCREATE TABLE t2 (a int);");
        var existing = new[] { new ExistingObject("table", "t1", ""), new ExistingObject("view", "v1", "") };

        var issues = Precheck.Compare(file, existing, dropFirst: false);

        Assert.Equal(2, issues.Count);
        Assert.Equal(IssueLevel.Warning, issues[0].Level);
        Assert.Equal(1, issues[0].Line);
        Assert.Contains("sẽ lỗi", issues[0].Message);
        Assert.Equal(IssueLevel.Info, issues[1].Level); // OR REPLACE
    }

    [Fact]
    public void Drop_first_turns_warnings_into_info()
    {
        var file = Parse("CREATE TABLE t1 (a int);");

        var issues = Precheck.Compare(file, [new ExistingObject("table", "t1", "")], dropFirst: true);

        Assert.Equal(IssueLevel.Info, Assert.Single(issues).Level);
    }

    [Fact]
    public void Trigger_on_own_table_counts_only_in_current_schema()
    {
        var table = Parse("CREATE TABLE t1 (a int);");
        var trigger = Parse("CREATE TRIGGER tr BEFORE INSERT ON t1 FOR EACH ROW EXECUTE FUNCTION f();\nCREATE TRIGGER tb BEFORE INSERT ON base FOR EACH ROW EXECUTE FUNCTION f();");
        var created = Precheck.CreatedRelations([table, trigger]);
        // Đã chọn schema addon: trigger cùng tên có trên public.t1 / public.base (search_path = addon, public).
        var existing = new[] { new ExistingObject("trigger", "tr", "t1", InCurrentSchema: false), new ExistingObject("trigger", "tb", "base", InCurrentSchema: false) };

        var issues = Precheck.Compare(trigger, existing, dropFirst: false, created);

        // t1 do thư mục tạo (sẽ ở addon) → trigger trên public.t1 không liên quan; base là bảng có sẵn ở public → đúng là sẽ lỗi.
        Assert.Equal(2, Assert.Single(issues).Line);
        Assert.Equal(2, Precheck.Compare(trigger, existing, dropFirst: false).Count); // không truyền danh sách: như cũ
    }

    [Fact]
    public void Kinds_and_tables_must_match()
    {
        var file = Parse("CREATE TRIGGER tr BEFORE INSERT ON t1 FOR EACH ROW EXECUTE FUNCTION f();\nCREATE SEQUENCE s1;\nCREATE FUNCTION t9() RETURNS int AS 'select 1' LANGUAGE sql;");
        var existing = new[]
        {
            new ExistingObject("trigger", "tr", "other_table"), // trigger cùng tên trên bảng khác - không đụng
            new ExistingObject("index", "s1", ""),              // bảng / sequence / index chung không gian tên
            new ExistingObject("table", "t9", ""),              // hàm và bảng khác không gian tên
        };

        var issues = Precheck.Compare(file, existing, dropFirst: false);

        Assert.Equal(2, Assert.Single(issues).Line);
    }
}

public class SqlScriptFileTests
{
    [Fact]
    public void Utf8_with_encoding_meta_is_clean()
    {
        var file = SqlScriptFile.Parse(Encoding.UTF8.GetBytes("\\encoding UTF-8\r\n\r\nCOMMENT ON TABLE t IS '受注';"), "a.sql", "a.sql");

        Assert.Equal("UTF-8", file.EncodingName);
        Assert.Empty(file.Issues);
        Assert.Contains("受注", file.Statements[0].Statement.Text);
    }

    [Fact]
    public void Bom_is_removed()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("SELECT 1;")).ToArray();

        var file = SqlScriptFile.Parse(bytes, "a.sql", "a.sql");

        Assert.Equal("UTF-8 (BOM)", file.EncodingName);
        Assert.Equal("SELECT 1", file.Statements[0].Statement.Text);
    }

    [Fact]
    public void Clean_shift_jis_is_read_without_warning_only_broken_bytes_warn()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932);

        // Shift-JIS sạch (2026-11-23: không cảnh báo nữa - DbTools tự chuyển khi chạy).
        var plain = SqlScriptFile.Parse(sjis.GetBytes("COMMENT ON TABLE t IS '受注明細 ｶﾅ 木型マスタ';"), "a.sql", "a.sql");
        var declared = SqlScriptFile.Parse(sjis.GetBytes("\\encoding SJIS\nCOMMENT ON TABLE t IS '受注';"), "b.sql", "b.sql");
        Assert.Equal("Shift-JIS", plain.EncodingName);
        Assert.Contains("受注明細 ｶﾅ 木型マスタ", plain.Statements[0].Statement.Text);
        Assert.Empty(plain.Issues);
        Assert.Empty(declared.Issues);

        // Byte không hợp lệ theo Shift-JIS (0x81 0x20: lead byte thiếu trail) → vẫn cảnh báo.
        var broken = sjis.GetBytes("COMMENT ON TABLE t IS '受注").Concat(new byte[] { 0x81, 0x20 }).Concat(sjis.GetBytes("';")).ToArray();
        var b = SqlScriptFile.Parse(broken, "c.sql", "c.sql");
        Assert.Equal("Shift-JIS", b.EncodingName);
        Assert.Contains("byte không hợp lệ", Assert.Single(b.Issues).Message);

        // Gần như toàn UTF-8, chỉ 1 byte hỏng → đọc UTF-8 (chữ Nhật không thành ký tự rác) + cảnh báo.
        var utf8 = Encoding.UTF8.GetBytes("COMMENT ON TABLE t IS '受注明細 木型マスタ';").Concat(new byte[] { 0xFF }).ToArray();
        var u = SqlScriptFile.Parse(utf8, "d.sql", "d.sql");
        Assert.Equal("UTF-8 (có byte lỗi)", u.EncodingName);
        Assert.Contains("受注明細 木型マスタ", u.Statements[0].Statement.Text);
        Assert.Contains("1 chỗ byte không hợp lệ", Assert.Single(u.Issues).Message);
    }

    [Fact]
    public void Unsupported_psql_commands_and_transactions_are_errors()
    {
        var file = SqlScriptFile.Parse(Encoding.UTF8.GetBytes("\\i other.sql\nBEGIN;\nCREATE TABLE t (a int);\nCOMMIT;\nVACUUM t;"), "a.sql", "a.sql");

        Assert.True(file.HasErrors);
        Assert.Equal([1, 2, 4, 5], file.Issues.Where(i => i.Level == IssueLevel.Error).Select(i => i.Line));
    }

    [Fact]
    public void Data_changes_are_warnings_and_summarized()
    {
        var file = SqlScriptFile.Parse(Encoding.UTF8.GetBytes("DELETE FROM a;\nDELETE FROM b;\nDROP VIEW v;\nINSERT INTO a VALUES (1);"), "a.sql", "a.sql");

        Assert.False(file.HasErrors);
        Assert.Equal(3, file.Issues.Count(i => i.Level == IssueLevel.Warning));
        Assert.Equal("DELETE ×2, DROP", file.DangerSummary);
        Assert.Equal("Xoá dữ liệu, Xoá đối tượng, Thêm dữ liệu", file.KindSummary);
    }

    [Fact]
    public void Empty_file_is_a_warning()
    {
        var file = SqlScriptFile.Parse(Encoding.UTF8.GetBytes("\\encoding UTF-8\n-- trống\n"), "a.sql", "a.sql");

        Assert.Equal("(trống)", file.KindSummary);
        Assert.Equal(IssueLevel.Warning, Assert.Single(file.Issues).Level);
    }
}
