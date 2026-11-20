using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Thông tin DB: tìm chữ trong mã nguồn, dò hàm nhắc tới bảng, tóm tắt ảnh hưởng khi xoá / sửa bảng.</summary>
public class DependencyTests
{
    private const string Source = """
        CREATE OR REPLACE FUNCTION addon.f_sum(p int)
         RETURNS numeric
        AS $function$
        DECLARE v numeric;
        BEGIN
          -- app_order dùng để tính tổng (chú thích: không tính)
          SELECT sum(o.amount) INTO v FROM addon.app_order o WHERE o.cust = p;
          UPDATE app_order SET flag = 1;            /* app_order trong chú thích khối */
          SELECT app_order_all.x FROM app_order_all; -- tên dài hơn: không tính
          SELECT t.app_order FROM other t;          -- cột cùng tên: không tính
          PERFORM app_order(1);                      -- hàm cùng tên: không tính
          SELECT * FROM other_schema.app_order;      -- bảng ở schema khác: không tính
          EXECUTE 'DELETE FROM addon.app_order WHERE id = ' || p;
          RETURN v;
        END $function$
        """;

    [Fact]
    public void Finds_table_in_function_body_skipping_comments_columns_and_other_schemas()
    {
        var matches = SourceSearch.FindTable(Source, "addon", "app_order");

        Assert.Equal([7, 8, 13], matches.Select(m => m.Line).ToArray());
        Assert.Equal([true, false, true], matches.Select(m => m.Qualified).ToArray()); // dòng 13: trong chuỗi EXECUTE
        Assert.Equal("addon.app_order", Source.Substring(matches[0].Index, matches[0].Length));
        Assert.StartsWith("SELECT sum(o.amount)", matches[0].LineText);
        Assert.Empty(SourceSearch.FindTable(Source, "addon", "f_sum")); // tên ngay sau FUNCTION không phải dùng bảng
    }

    [Fact]
    public void Finds_quoted_table_names_exactly()
    {
        var sql = "SELECT * FROM addon.\"Order\" o JOIN \"Order\" p ON true JOIN \"order\" q ON true; SELECT * FROM order_x";
        var matches = SourceSearch.FindTable(sql, "addon", "Order");
        Assert.Equal(2, matches.Count);
        Assert.Equal("addon.\"Order\"", sql.Substring(matches[0].Index, matches[0].Length));
        Assert.Single(SourceSearch.FindTable(sql, "addon", "order")); // "order" thường (đặt trong nháy)
    }

    [Fact]
    public void Text_search_options()
    {
        const string text = "select ORDER_NO from app_order\n-- order\nselect order_detail";
        Assert.Equal(4, SourceSearch.Find(text, "order", wholeWord: false, caseSensitive: false).Count);
        Assert.Single(SourceSearch.Find(text, "order", wholeWord: true, caseSensitive: false)); // chỉ "-- order"
        Assert.Equal(3, SourceSearch.Find(text, "order", wholeWord: false, caseSensitive: true).Count);
        var m = SourceSearch.Find(text, "order_detail", true, true).Single();
        Assert.Equal((3, "select order_detail"), (m.Line, m.LineText));
        Assert.Equal(text.IndexOf("order_detail", StringComparison.Ordinal), m.Index);
        Assert.Empty(SourceSearch.Find(text, "", false, false));
        Assert.Equal("1 chỗ · dòng 3: select order_detail", SourceSearch.Summary([m]));
    }

    [Fact]
    public void Impact_summary_for_drop_and_column_changes()
    {
        var none = DependencyImpact.Describe("a.t", []);
        Assert.StartsWith("Không thấy đối tượng nào dùng a.t", Assert.Single(none));

        var items = new List<DependentItem>
        {
            new(DependentKind.View, "a.v1", "view", ["amount", "id"], ""),
            new(DependentKind.View, "a.v2", "view · gián tiếp qua a.v1", [], "", Depth: 2),
            new(DependentKind.ForeignKey, "a.line (fk)", "", ["id"], ""),
            new(DependentKind.Trigger, "tr", "", [], ""),
            new(DependentKind.Sequence, "a.t_id_seq", "", ["id"], ""),
            new(DependentKind.Function, "a.f()", "", [], ""),
            new(DependentKind.IndirectTrigger, "tr2 trên a.x", "", [], ""),
        };
        var lines = DependencyImpact.Describe("a.t", items);
        Assert.Equal("⚠ DROP TABLE a.t: báo lỗi vì có 2 view, 1 khoá ngoại từ bảng khác phụ thuộc. DROP ... CASCADE sẽ xoá 2 view (kể cả view dựng trên view đó), "
            + "bỏ 1 khoá ngoại (dữ liệu bảng kia giữ nguyên).", lines[0]);
        Assert.Equal("Xoá theo bảng: 1 trigger, 1 sequence.", lines[1]);
        Assert.StartsWith("⚠ 1 hàm / procedure nhắc tên bảng trong mã nguồn (1 trigger trên bảng khác gọi các hàm này)", lines[2]);
        Assert.Equal("Đổi kiểu / xoá cột đang dùng trong view sẽ báo lỗi: amount (a.v1); id (a.v1).", lines[3]);

        Assert.StartsWith("DROP TABLE a.t: không bị chặn", DependencyImpact.Describe("a.t", [new(DependentKind.Trigger, "tr", "", [], "")])[0]);
    }
}

/// <summary>Trang Chạy - cảnh báo phụ thuộc: nhận câu xoá / sửa bảng trong script, chọn đối tượng bị ảnh hưởng.</summary>
public class TableChangeTests
{
    private static SqlScriptFile Script(string sql, string name = "a.sql") =>
        SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes(sql), name, name);

    [Fact]
    public void Finds_drop_and_alter_table_changes()
    {
        var file = Script("""
            DROP TABLE IF EXISTS a.t1, t2 CASCADE;
            ALTER TABLE ONLY a.t3 DROP COLUMN IF EXISTS c1, ALTER COLUMN c2 TYPE bigint, ALTER c3 SET DATA TYPE text, RENAME COLUMN c4 TO c5;
            ALTER TABLE t4 RENAME TO t4_old;
            ALTER TABLE t5 SET SCHEMA b;
            ALTER TABLE t6 DROP CONSTRAINT k, ALTER COLUMN c DROP DEFAULT, ADD COLUMN d int, RENAME CONSTRAINT x TO y;
            ALTER TABLE t7 DROP c9, ADD CONSTRAINT ck CHECK (a IN (1, 2));
            DROP VIEW v1;
            """);

        var changes = TableChanges.Find([file]);

        Assert.Equal([
            "DROP TABLE a.t1 CASCADE (a.sql dòng 1)", "DROP TABLE t2 CASCADE (a.sql dòng 1)",
            "xoá cột a.t3.c1 (a.sql dòng 2)", "đổi kiểu cột a.t3.c2 (a.sql dòng 2)", "đổi kiểu cột a.t3.c3 (a.sql dòng 2)", "đổi tên cột a.t3.c4 (a.sql dòng 2)",
            "đổi tên / chuyển schema bảng t4 (a.sql dòng 3)", "đổi tên / chuyển schema bảng t5 (a.sql dòng 4)",
            "xoá cột t7.c9 (a.sql dòng 6)",
        ], changes.Select(c => $"{c.Text} ({c.Where})").ToArray());
    }

    private static readonly DependentItem[] Deps =
    [
        new(DependentKind.View, "a.v1", "", ["id", "name"], ""),
        new(DependentKind.View, "a.v_new", "", ["id"], ""),
        new(DependentKind.ForeignKey, "a.line (fk)", "", ["id"], ""),
        new(DependentKind.Trigger, "tr", "", [], ""),
        new(DependentKind.Function, "a.f_name()", "", [], "SELECT name FROM a.t"),
        new(DependentKind.Function, "a.f_all()", "", [], "SELECT count(*) FROM a.t"),
    ];

    private static List<string> Describe(TableChangeKind kind, string? column = null, bool cascade = false) =>
        TableChanges.Describe(new TableChange(kind, new QualifiedName("a", "t", false), column, "x.sql", 3, cascade), Deps,
            new HashSet<string>(["v_new"])); // v_new: thư mục tự tạo lại → bỏ qua

    [Fact]
    public void Describes_only_what_each_change_breaks()
    {
        Assert.Equal([
            "DROP TABLE a.t (x.sql dòng 3): sẽ LỖI (rollback cả lô) vì đang được dùng bởi: a.v1, a.line (fk)",
            "DROP TABLE a.t (x.sql dòng 3): hàm nhắc tên bảng sẽ lỗi lúc chạy: a.f_name(), a.f_all()",
        ], Describe(TableChangeKind.DropTable));
        Assert.StartsWith("DROP TABLE a.t CASCADE (x.sql dòng 3): CASCADE sẽ xoá / bỏ kèm: a.v1, a.line (fk)", Describe(TableChangeKind.DropTable, cascade: true)[0]);
        Assert.Equal(["xoá cột a.t.name (x.sql dòng 3): sẽ LỖI (rollback cả lô) vì cột đang được dùng bởi: a.v1",
            "xoá cột a.t.name (x.sql dòng 3): hàm nhắc tên cột sẽ lỗi lúc chạy: a.f_name()"], Describe(TableChangeKind.DropColumn, "name"));
        Assert.Equal(["xoá cột a.t.id (x.sql dòng 3): sẽ LỖI (rollback cả lô) vì cột đang được dùng bởi: a.v1, a.line (fk)"], Describe(TableChangeKind.DropColumn, "id"));
        Assert.Empty(Describe(TableChangeKind.DropColumn, "memo")); // cột không ai dùng
        Assert.StartsWith("đổi kiểu cột a.t.name (x.sql dòng 3): sẽ LỖI (rollback cả lô): PostgreSQL không cho đổi kiểu cột view đang dùng - a.v1",
            Describe(TableChangeKind.AlterColumnType, "name")[0]);
        Assert.Equal(["đổi tên cột a.t.name (x.sql dòng 3): hàm nhắc tên cột sẽ lỗi lúc chạy: a.f_name()"], Describe(TableChangeKind.RenameColumn, "name"));
        Assert.Equal(["đổi tên / chuyển schema bảng a.t (x.sql dòng 3): hàm gọi bảng theo tên cũ sẽ lỗi lúc chạy (view / khoá ngoại tự theo tên mới): a.f_name(), a.f_all()"],
            Describe(TableChangeKind.RenameTable));
    }
}

/// <summary>Phụ thuộc + mã nguồn đọc từ catalog thật (cần DBTOOLS_TEST_PG).</summary>
public sealed class DependencyIntegrationTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
    private readonly string _db = "dbtools_dep_" + Guid.NewGuid().ToString("N")[..10];
    private ConnectionProfile _admin = new();

    private const string Schema = """
        CREATE SCHEMA addon;
        CREATE TABLE addon.cust (id serial PRIMARY KEY, name text, amount numeric);
        CREATE TABLE addon.line (id int PRIMARY KEY, cust_id int REFERENCES addon.cust(id));
        CREATE VIEW addon.v_cust AS SELECT id, name FROM addon.cust;
        CREATE VIEW addon.v_cust2 AS SELECT id FROM addon.v_cust;
        CREATE FUNCTION addon.f_total() RETURNS numeric AS $$ BEGIN RETURN (SELECT sum(amount) FROM addon.cust); END $$ LANGUAGE plpgsql;
        CREATE FUNCTION addon.f_other() RETURNS int AS $$ BEGIN RETURN 1; END $$ LANGUAGE plpgsql;
        CREATE FUNCTION addon.f_atomic() RETURNS bigint LANGUAGE sql BEGIN ATOMIC SELECT count(*) FROM addon.cust; END;
        CREATE FUNCTION addon.tr_line() RETURNS trigger AS $$ BEGIN UPDATE cust SET amount = 0 WHERE id = NEW.cust_id; RETURN NEW; END $$ LANGUAGE plpgsql;
        CREATE TRIGGER line_tr AFTER INSERT ON addon.line FOR EACH ROW EXECUTE FUNCTION addon.tr_line();
        CREATE FUNCTION addon.tr_cust() RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql;
        CREATE TRIGGER cust_tr BEFORE UPDATE ON addon.cust FOR EACH ROW EXECUTE FUNCTION addon.tr_cust();
        """;

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server);
        _admin = new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
        await PgAdmin.CreateDatabaseAsync(_admin, _db, CancellationToken.None);
        await using var c = await PgInfo.OpenAsync(_admin, _db, CancellationToken.None);
        // Tách bằng SqlSplitter như trang Chạy - có hàm BEGIN ATOMIC ...; END (; bên trong thân hàm)
        var statements = SqlSplitter.Split(Schema).Statements;
        Assert.Equal(12, statements.Count);
        foreach (var s in statements)
        {
            await using var cmd = new NpgsqlCommand(s.Text, c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        await using var c = await Pg.OpenAsync(_admin, CancellationToken.None);
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_db} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Lists_everything_that_uses_a_table()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await using var c = await PgInfo.OpenAsync(_admin, _db, CancellationToken.None);
        var sources = await PgInfo.SourcesAsync(c, CancellationToken.None);
        Assert.Contains(sources, s => s.Kind == "view" && s.Name == "v_cust" && s.Definition.StartsWith("CREATE OR REPLACE VIEW addon.v_cust AS"));
        Assert.Contains(sources, s => s.Kind == "function" && s.Name == "f_total" && s.Definition.Contains("sum(amount) FROM addon.cust"));

        var oid = await ScalarAsync<uint>(c, "SELECT 'addon.cust'::regclass::oid");
        var deps = await PgInfo.DependentsAsync(c, oid, "addon", "cust", sources, CancellationToken.None);
        string Kinds(DependentKind k) => string.Join(",", deps.Where(d => d.Kind == k).Select(d => d.Name).Order());

        Assert.Equal("addon.v_cust,addon.v_cust2", Kinds(DependentKind.View));
        var v1 = deps.Single(d => d.Name == "addon.v_cust");
        Assert.Equal(["id", "name"], v1.Columns); // amount không dùng
        Assert.Equal(2, deps.Single(d => d.Name == "addon.v_cust2").Depth);
        Assert.Contains("gián tiếp qua addon.v_cust", deps.Single(d => d.Name == "addon.v_cust2").Relation);
        Assert.Equal("addon.line (line_cust_id_fkey)", Kinds(DependentKind.ForeignKey));
        Assert.Equal("cust_tr", Kinds(DependentKind.Trigger));
        Assert.Equal("addon.cust_id_seq", Kinds(DependentKind.Sequence));
        Assert.Equal("addon.f_atomic()", Kinds(DependentKind.TrackedFunction));
        Assert.Equal("addon.f_total(),addon.tr_line()", Kinds(DependentKind.Function)); // f_other, tr_cust không nhắc tới bảng
        Assert.Contains("theo tên", deps.Single(d => d.Name == "addon.tr_line()").Relation); // UPDATE cust không ghi schema
        Assert.Equal("line_tr trên addon.line", Kinds(DependentKind.IndirectTrigger));

        var impact = DependencyImpact.Describe("addon.cust", deps);
        Assert.StartsWith("⚠ DROP TABLE addon.cust: báo lỗi vì có 2 view, 1 khoá ngoại từ bảng khác, 1 hàm SQL chuẩn phụ thuộc", impact[0]);

        // Bảng không ai dùng
        var lineOid = await ScalarAsync<uint>(c, "SELECT 'addon.line'::regclass::oid");
        var lineDeps = await PgInfo.DependentsAsync(c, lineOid, "addon", "line", sources, CancellationToken.None);
        Assert.Equal("line_tr", string.Join(",", lineDeps.Select(d => d.Name)));
    }

    [Fact]
    public async Task Source_search_line_numbers_match_the_definition_box()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        await using var c = await PgInfo.OpenAsync(_admin, _db, CancellationToken.None);
        var sources = await PgInfo.SourcesAsync(c, CancellationToken.None);
        var hits = sources.Select(s => (s, m: SourceSearch.Find(s.Definition, "AMOUNT", wholeWord: true, caseSensitive: false))).Where(x => x.m.Count > 0).ToList();
        Assert.Equal("f_total,tr_line", string.Join(",", hits.Select(h => h.s.Name).Order()));
        var objects = await PgInfo.ObjectsAsync(c, CancellationToken.None);
        var total = hits.Single(h => h.s.Name == "f_total");
        // Định nghĩa đọc cả lô = định nghĩa đọc khi chọn 1 dòng ở tab Hàm → số dòng khớp đúng chỗ
        Assert.Equal(await PgInfo.DefinitionAsync(c, objects.Single(o => o.Name == "f_total"), CancellationToken.None), total.s.Definition);
        Assert.Contains("amount", total.m[0].LineText);
    }

    [Fact]
    public async Task Predicted_breaks_match_what_postgresql_does()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var file = SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes("""
            ALTER TABLE addon.cust DROP COLUMN name;
            ALTER TABLE addon.cust DROP COLUMN amount;
            ALTER TABLE addon.cust ALTER COLUMN name TYPE varchar(10);
            DROP TABLE addon.cust;
            DROP TABLE addon.not_there;
            """), "x.sql", "x.sql");
        await using var c = await PgInfo.OpenAsync(_admin, _db, CancellationToken.None);
        var sources = await PgInfo.SourcesAsync(c, CancellationToken.None);
        var warnings = new List<string>();
        foreach (var change in TableChanges.Find([file]))
        {
            if (await PgInfo.FindTableAsync(c, change.Table.Sql, CancellationToken.None) is { } t)
            {
                var deps = await PgInfo.DependentsAsync(c, t.Oid, t.Schema, t.Name, sources, CancellationToken.None);
                warnings.AddRange(TableChanges.Describe(change, deps, new HashSet<string>()));
            }
        }
        Assert.Null(await PgInfo.FindTableAsync(c, "addon.not_there", CancellationToken.None));
        Assert.Contains(warnings, w => w.StartsWith("xoá cột addon.cust.name (x.sql dòng 1): sẽ LỖI") && w.Contains("addon.v_cust"));
        Assert.Contains(warnings, w => w.StartsWith("xoá cột addon.cust.amount (x.sql dòng 2): hàm nhắc tên cột sẽ lỗi lúc chạy: addon.f_total()"));
        Assert.DoesNotContain(warnings, w => w.StartsWith("xoá cột addon.cust.amount") && w.Contains("sẽ LỖI")); // không view nào dùng amount
        Assert.Contains(warnings, w => w.StartsWith("đổi kiểu cột addon.cust.name (x.sql dòng 3): sẽ LỖI"));
        Assert.Contains(warnings, w => w.StartsWith("DROP TABLE addon.cust (x.sql dòng 4): sẽ LỖI") && w.Contains("addon.line (line_cust_id_fkey)"));

        // PostgreSQL thật: câu dự đoán LỖI thì lỗi, câu chỉ cảnh báo hàm thì chạy được (trong transaction, rollback)
        async Task<bool> FailsAsync(string sql)
        {
            await using var tx = await c.BeginTransactionAsync();
            try
            {
                await using var cmd = new NpgsqlCommand(sql, c, tx);
                await cmd.ExecuteNonQueryAsync();
                return false;
            }
            catch (PostgresException)
            {
                return true;
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }
        Assert.True(await FailsAsync("ALTER TABLE addon.cust DROP COLUMN name"));
        Assert.False(await FailsAsync("ALTER TABLE addon.cust DROP COLUMN amount"));
        Assert.True(await FailsAsync("ALTER TABLE addon.cust ALTER COLUMN name TYPE varchar(10)"));
        Assert.True(await FailsAsync("DROP TABLE addon.cust"));
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
