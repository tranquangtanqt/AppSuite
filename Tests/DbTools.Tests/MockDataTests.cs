using System.Text;
using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Sinh dữ liệu mẫu: kiểu cột, đề xuất cách sinh, bộ sinh (kiểm tra độ dài / numeric / không trùng), ghi CSV / INSERT, đọc CSV.</summary>
public class MockDataTests
{
    [Theory]
    [InlineData("integer", MockKind.Integer, null, null, null)]
    [InlineData("numeric(15,2)", MockKind.Decimal, null, 15, 2)]
    [InlineData("character varying(200)", MockKind.Text, 200, null, null)]
    [InlineData("character(1)", MockKind.Text, 1, null, null)]
    [InlineData("timestamp without time zone", MockKind.Timestamp, null, null, null)]
    [InlineData("date", MockKind.Date, null, null, null)]
    [InlineData("boolean", MockKind.Boolean, null, null, null)]
    [InlineData("jsonb", MockKind.Other, null, null, null)]
    public void Parses_postgres_types(string text, MockKind kind, int? length, int? precision, int? scale)
    {
        var t = MockType.Parse(text);
        Assert.Equal(kind, t.Kind);
        Assert.Equal(length, t.Length);
        Assert.Equal(precision, t.Precision);
        Assert.Equal(scale, t.Scale);
    }

    [Fact]
    public void Infers_types_from_csv_values()
    {
        Assert.Equal(MockKind.Integer, MockType.Infer(["1", "20", null]).Kind);
        Assert.Equal(MockKind.Text, MockType.Infer(["001", "002"]).Kind); // số 0 đầu = mã, giữ dạng chuỗi
        var d = MockType.Infer(["1.5", "20.25"]);
        Assert.Equal((MockKind.Decimal, 2), (d.Kind, d.Scale));
        Assert.Equal(MockKind.Date, MockType.Infer(["2026-01-05", "2026/02/01"]).Kind);
        Assert.Equal(MockKind.Timestamp, MockType.Infer(["2026-01-05 10:00:00"]).Kind);
        Assert.Equal(MockKind.Boolean, MockType.Infer(["true", "f"]).Kind);
        Assert.Equal(MockKind.Text, MockType.Infer(["khách", "1"]).Kind);
    }

    [Fact]
    public void Suggests_rules_from_keys_and_samples()
    {
        var i = MockType.Parse("integer");
        Assert.Equal((MockRule.Sequence, "501"), MockSuggest.For("id", i, true, true, false, ["1", "2"], "500", false));
        Assert.Equal((MockRule.Sequence, "ORD000124"), MockSuggest.For("code", MockType.Parse("varchar(10)"), true, true, false, ["ORD000101"], "ORD000123", false));
        Assert.Equal(MockRule.Skip, MockSuggest.For("id", i, true, true, true, ["1"], null, false).Rule);
        Assert.Equal(MockRule.ForeignKey, MockSuggest.For("cust_id", i, true, false, false, ["3"], null, true).Rule);
        Assert.Equal((MockRule.Pick, "0|1"), MockSuggest.For("del_flg", MockType.Parse("character(1)"), true, false, false, ["0", "1", "0"], null, false));
        Assert.Equal((MockRule.Number, "100..250.5"), MockSuggest.For("amount", MockType.Parse("numeric(10,2)"), false, false, false, ["100", "250.5", "180"], null, false));
        Assert.Equal((MockRule.DateRange, "2026-01-02..2026-03-03"), MockSuggest.For("d", MockType.Parse("date"), false, false, false, ["2026-02-01", "2026-02-01"], null, false));
        Assert.Equal((MockRule.Sequence, "khách 10"), MockSuggest.For("name", MockType.Parse("text"), true, false, false, ["khách 7", "khách 9"], null, false));
        Assert.Equal((MockRule.Pattern, "Nguyễn_{n}"), MockSuggest.For("name", MockType.Parse("text"), true, false, false, ["Nguyễn"], null, false));
        Assert.Equal(MockRule.Null, MockSuggest.For("memo", MockType.Parse("text"), false, false, false, [null], null, false).Rule);
        Assert.Equal("ab{n}", MockSuggest.PatternOf("abcdef", 8)); // varchar(8): chừa 6 chữ số cho {n}
        // File CSV (không biết khoá): mã A001, A002 → tăng dần giữ số chữ số; id 1, 2, 3 → tăng dần
        Assert.Equal((MockRule.Sequence, "A003"), MockSuggest.For("code", MockType.Parse("text"), true, false, false, ["A001", "A002"], null, false));
        Assert.Equal((MockRule.Sequence, "4"), MockSuggest.For("id", i, true, false, false, ["2", "1", "3"], null, false));
        Assert.Equal(MockRule.Number, MockSuggest.For("qty", i, true, false, false, ["5", "9"], null, false).Rule);
    }

    private static MockColumnSpec Col(string name, string type, MockRule rule, string param = "", bool notNull = false, IReadOnlyList<string>? values = null) =>
        new(name, MockType.Parse(type), notNull, rule, param, values);

    [Fact]
    public void Generates_deterministic_valid_rows()
    {
        MockColumnSpec[] cols =
        [
            Col("id", "integer", MockRule.Skip),
            Col("code", "varchar(10)", MockRule.Sequence, "ORD000998"),
            Col("no", "integer", MockRule.Sequence, "1001"),
            Col("amount", "numeric(8,2)", MockRule.Number, "10..99"),
            Col("qty", "integer", MockRule.Number, "1..3"),
            Col("d", "date", MockRule.DateRange, "2026-01-01..2026-01-31"),
            Col("at", "timestamp", MockRule.DateRange, "2026-01-01..2026-01-01"),
            Col("flag", "char(1)", MockRule.Pick, "0|1"),
            Col("name", "text", MockRule.Pattern, "khách {n}"),
            Col("memo", "text", MockRule.Null),
            Col("c", "text", MockRule.Constant, " x "),
            Col("ok", "boolean", MockRule.Boolean),
            Col("u", "uuid", MockRule.Uuid),
            Col("cust", "integer", MockRule.ForeignKey, values: ["7", "8"]),
        ];
        var rows = MockGenerator.Generate(cols, 50, seed: 42, [["code"], ["no"]]);
        Assert.Equal(50, rows.Count);
        Assert.Equal(13, rows[0].Length); // id Bỏ qua
        Assert.Equal(["ORD000998", "1001"], rows[0][..2]);
        Assert.Equal(["ORD001000", "1003"], rows[2][..2]); // giữ số chữ số, sang 4 chữ số tự nhiên
        Assert.All(rows, r =>
        {
            var amount = decimal.Parse(r[2]!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(amount, 10m, 99m);
            Assert.True(r[2]!.Split('.').ElementAtOrDefault(1)?.Length is null or <= 2);
            Assert.InRange(int.Parse(r[3]!), 1, 3);
            Assert.StartsWith("2026-01-", r[4]);
            Assert.StartsWith("2026-01-01 ", r[5]);
            Assert.Contains(r[6], new[] { "0", "1" });
            Assert.Null(r[8]);
            Assert.Equal(" x ", r[9]);
            Assert.True(Guid.TryParse(r[11], out _));
            Assert.Contains(r[12], new[] { "7", "8" });
        });
        Assert.Equal("khách 50", rows[49][7]);
        // Cùng seed → cùng dữ liệu (xem trước = đầu file); 20 dòng đầu của 50 = 20 dòng sinh riêng
        var again = MockGenerator.Generate(cols, 20, seed: 42, [["code"], ["no"]]);
        Assert.Equal(rows.Take(20).Select(r => string.Join(",", r)), again.Select(r => string.Join(",", r)));
    }

    [Fact]
    public void Reports_violations()
    {
        Assert.Contains("dài", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("c", "varchar(5)", MockRule.Pattern, "abcde{n}")], 1, 1)).Message);
        Assert.Contains("numeric(4,2)", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("n", "numeric(4,2)", MockRule.Number, "100..200")], 1, 1)).Message);
        Assert.Contains("không trùng", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("k", "integer", MockRule.Pick, "1|2")], 10, 1, [["k"]])).Message);
        Assert.Contains("NOT NULL", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("k", "integer", MockRule.Null, notNull: true)], 1, 1)).Message);
        Assert.Contains("{n}", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("k", "text", MockRule.Pattern, "abc")], 1, 1)).Message);
        Assert.Contains("min..max", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("k", "integer", MockRule.Number, "5")], 1, 1)).Message);
        Assert.Contains("chưa có dòng", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("k", "integer", MockRule.ForeignKey, values: [])], 1, 1)).Message);
        Assert.Contains("Bỏ qua", Assert.Throws<MockError>(() => MockGenerator.Generate([Col("k", "integer", MockRule.Skip)], 1, 1)).Message);
    }

    [Fact]
    public void Writes_csv_and_insert()
    {
        var rows = new List<string?[]> { new[] { "1", "a,b", null }, new[] { "2", "say \"hi\"", "" } };
        var csv = new StringWriter();
        MockOutput.WriteCsv(csv, ["id", "name", "memo"], rows, new CsvOptions());
        Assert.Equal("id,name,memo\r\n1,\"a,b\",\r\n2,\"say \"\"hi\"\"\",\"\"\r\n", csv.ToString());
        var nulls = new StringWriter();
        MockOutput.WriteCsv(nulls, ["id"], [["1", null]], new CsvOptions(Header: false, NullAsText: true));
        Assert.Equal("1,NULL\r\n", nulls.ToString());

        MockColumnSpec[] cols = [Col("id", "integer", MockRule.Sequence, "1"), Col("Name", "text", MockRule.Pattern, "x{n}"), Col("d", "date", MockRule.DateRange, "2026-01-01..2026-01-01")];
        var sql = new StringWriter();
        MockOutput.WriteInsert(sql, "public.t", cols, MockGenerator.Generate(cols, 3, 1).Append(["4", "o'k", null]), "test", batch: 2);
        var text = sql.ToString();
        Assert.Contains("INSERT INTO public.t (id, \"Name\", d) VALUES\n    (1, 'x1', '2026-01-01'),\n    (2, 'x2', '2026-01-01');", text);
        Assert.Contains("(4, 'o''k', NULL);", text);
        Assert.Equal(2, text.Split("INSERT INTO").Length - 1);
        Assert.DoesNotContain("BEGIN;", text);
    }

    [Fact]
    public void Reads_csv_samples_in_utf8_and_shift_jis()
    {
        var csv = "id,name,memo\r\n1,\"a,b\",\r\n2,\"line1\nline2\",NULL\r\n";
        var (header, rows) = MockOutput.ReadCsv(new UTF8Encoding(true).GetBytes(csv));
        Assert.Equal(["id", "name", "memo"], header);
        Assert.Equal(2, rows.Count);
        Assert.Equal("a,b", rows[0][1]);
        Assert.Null(rows[0][2]);
        Assert.Equal("line1\nline2", rows[1][1]);
        Assert.Null(rows[1][2]);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var (h2, r2) = MockOutput.ReadCsv(Encoding.GetEncoding(932).GetBytes("コード\t名前\r\nA01\t受注\r\n"));
        Assert.Equal(["コード", "名前"], h2);
        Assert.Equal("受注", r2[0][1]);
    }

    [Fact]
    public void Csv_source_infers_columns()
    {
        using var folder = new TempFolder();
        var path = folder.Write("Order Items.csv", "id,code,price,ordered\r\n1,A001,12.50,2026-01-02\r\n2,A002,8,2026-01-05\r\n");
        var src = MockSource.FromCsv(path, string.Empty);
        Assert.Equal("\"order items\"", src.TableSql);
        Assert.Equal([MockKind.Integer, MockKind.Text, MockKind.Decimal, MockKind.Date], src.Columns.Select(c => c.Type.Kind));
        Assert.All(src.Columns, c => Assert.True(c.NotNull));
        Assert.Equal("public.oi", MockSource.FromCsv(path, "public.oi").TableSql);
    }
}

/// <summary>Đọc mẫu từ bảng thật, sinh theo đề xuất rồi chạy INSERT vào DB: không lỗi khoá chính / duy nhất / khoá ngoại / độ dài.</summary>
public sealed class MockDataIntegrationTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
    private readonly string _db = "dbtools_mock_" + Guid.NewGuid().ToString("N")[..10];
    private ConnectionProfile _admin = new();

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server);
        _admin = new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
        await PgAdmin.CreateDatabaseAsync(_admin, _db, CancellationToken.None);
        await ExecAsync("""
            CREATE TABLE cust (id int PRIMARY KEY, name varchar(20) NOT NULL);
            INSERT INTO cust SELECT g, 'khách ' || g FROM generate_series(1, 40) g;
            CREATE TABLE app_order (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                code varchar(12) NOT NULL UNIQUE,
                cust_id int NOT NULL REFERENCES cust(id),
                amount numeric(8,2) NOT NULL,
                del_flg char(1) NOT NULL DEFAULT '0',
                ordered date NOT NULL,
                memo text
            );
            INSERT INTO app_order (code, cust_id, amount, del_flg, ordered, memo) VALUES
                ('ORD000001', 1, 1200.50, '0', '2026-01-10', 'giao sáng'),
                ('ORD000002', 2, 89.00, '1', '2026-01-12', NULL),
                ('ORD000003', 3, 450.00, '0', '2026-02-01', 'giao chiều');
            """);
    }

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(Server))
        {
            await using var c = await Pg.OpenAsync(_admin, CancellationToken.None);
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_db} WITH (FORCE)", c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task ExecAsync(string sql)
    {
        await using var c = await PgInfo.OpenAsync(_admin, _db, CancellationToken.None);
        foreach (var s in SqlSplitter.Split(sql).Statements)
        {
            await using var cmd = new NpgsqlCommand(s.Text, c);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Suggested_rules_produce_insertable_rows()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var src = await MockSource.FromTableAsync(_admin, _db, "public.app_order", 5, CancellationToken.None);
        Assert.Equal(3, src.Samples.Count);
        Assert.True(src.Columns.Single(c => c.Name == "id").DbGenerated);
        Assert.Equal("ORD000003", src.Columns.Single(c => c.Name == "code").ExistingMax);
        Assert.Equal(40, src.Columns.Single(c => c.Name == "cust_id").ForeignValues!.Count);
        Assert.Equal("cust(id)", src.Columns.Single(c => c.Name == "cust_id").ForeignRef);

        var specs = src.Columns.Select((c, i) =>
        {
            var (rule, param) = MockSuggest.For(c.Name, c.Type, c.NotNull, c.Unique, c.DbGenerated, src.Samples.Select(r => r[i]).ToList(),
                c.ExistingMax, c.ForeignValues is { Count: > 0 });
            return new MockColumnSpec(c.Name, c.Type, c.NotNull, rule, param, c.ForeignValues);
        }).ToList();
        Assert.Equal(
            [MockRule.Skip, MockRule.Sequence, MockRule.ForeignKey, MockRule.Number, MockRule.Pick, MockRule.DateRange, MockRule.Pattern],
            specs.Select(s => s.Rule));
        Assert.Equal("ORD000004", specs[1].Param);

        var sql = new StringWriter();
        MockOutput.WriteInsert(sql, src.TableSql, specs, MockGenerator.Stream(specs, 2000, 7, src.UniqueSets), "test");
        await ExecAsync(sql.ToString());

        await using var c = await PgInfo.OpenAsync(_admin, _db, CancellationToken.None);
        await using var cmd = new NpgsqlCommand("SELECT count(*), count(DISTINCT code), max(code), min(amount) >= 89, max(amount) <= 1200.5 FROM app_order", c);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        Assert.Equal(2003L, r.GetInt64(0));
        Assert.Equal(2003L, r.GetInt64(1));
        Assert.Equal("ORD002003", r.GetString(2));
        Assert.True(r.GetBoolean(3));
        Assert.True(r.GetBoolean(4));
    }
}
