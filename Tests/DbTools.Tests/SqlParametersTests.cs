using DbTools.Core;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Nhật ký server - Chép SQL đã thay tham số: tách "parameters: $1 = '...'" và thay $n vào câu (2026-11-26).</summary>
public class SqlParametersTests
{
    private const string UserDetail = "DETAIL: parameters: $1 = '20261126091603000', $2 = '20261126113201000'";

    [Fact]
    public void Parses_parameters_line()
    {
        var p = SqlParameters.Parse(UserDetail);
        Assert.Equal(2, p.Count);
        Assert.Equal("'20261126091603000'", p[1]);
        Assert.Equal("'20261126113201000'", p[2]);
    }

    [Fact]
    public void Parses_quotes_null_commas_newlines_and_error_context()
    {
        var p = SqlParameters.Parse("DETAIL: parameters: $1 = 'O''Brien, Jr.', $2 = NULL, $3 = 'dòng 1\ndòng 2', $10 = '債権'");
        Assert.Equal("'O''Brien, Jr.'", p[1]);
        Assert.Equal("NULL", p[2]);
        Assert.Equal("'dòng 1\ndòng 2'", p[3]);
        Assert.Equal("'債権'", p[10]);

        var ctx = SqlParameters.Parse("ERROR: invalid input\nCONTEXT: unnamed portal with parameters: $1 = 'x'");
        Assert.Equal("'x'", ctx[1]);
        // PostgreSQL 16+ viết hoa
        Assert.Equal("'O''Brien'", SqlParameters.Parse("DETAIL: Parameters: $1 = 'O''Brien', $2 = '2026'")[1]);

        Assert.Empty(SqlParameters.Parse("DETAIL: Key (id)=(1) already exists."));
        // log bị cắt giữa giá trị: giữ các giá trị đủ
        var cut = SqlParameters.Parse("DETAIL: parameters: $1 = 'a', $2 = 'chưa đóng");
        Assert.Equal(new[] { "'a'" }, cut.Values);
    }

    [Fact]
    public void Inlines_user_sample_with_end_of_line_comments()
    {
        var sql = "-- MBBBLA5020\nWITH X AS (\n    SELECT 1\n    WHERE\n"
            + "        AND TO_TIMESTAMP($1, 'YYYYMMDDHH24MISSMS') < A.UPD_TS\n"
            + "        AND A.UPD_TS <= TO_TIMESTAMP($2 , 'YYYYMMDDHH24MISSMS')\n)";
        var r = SqlParameters.Inline(sql, SqlParameters.Parse(UserDetail));
        Assert.Equal(2, r.Replaced);
        Assert.Empty(r.Missing);
        Assert.Equal("-- MBBBLA5020\nWITH X AS (\n    SELECT 1\n    WHERE\n"
            + "        AND TO_TIMESTAMP('20261126091603000', 'YYYYMMDDHH24MISSMS') < A.UPD_TS  -- $1 = '20261126091603000'\n"
            + "        AND A.UPD_TS <= TO_TIMESTAMP('20261126113201000' , 'YYYYMMDDHH24MISSMS')  -- $2 = '20261126113201000'\n)", r.Sql);
    }

    [Fact]
    public void Leaves_strings_comments_identifiers_and_dollar_bodies_alone()
    {
        var values = new Dictionary<int, string> { [1] = "'a'", [2] = "NULL", [10] = "'mười'" };
        var sql = "SELECT $1, '$1', \"$1\", E'it\\'s $1', a$1, $10, $2 /* $1 /* lồng $1 */ $1 */ -- $1\n"
            + "DO $$ BEGIN PERFORM $1; END $$; SELECT $fn$ $1 $fn$, $1, $3";
        var r = SqlParameters.Inline(sql, values);
        Assert.Equal("SELECT 'a', '$1', \"$1\", E'it\\'s $1', a$1, 'mười', NULL /* $1 /* lồng $1 */ $1 */ -- $1  -- $1 = 'a', $10 = 'mười', $2 = NULL\n"
            + "DO $$ BEGIN PERFORM $1; END $$; SELECT $fn$ $1 $fn$, 'a', $3  -- $1 = 'a'", r.Sql);
        Assert.Equal(4, r.Replaced);
        Assert.Equal([3], r.Missing);
    }

    [Fact]
    public void No_comment_when_line_ends_inside_a_string_and_long_values_are_shortened_in_comment()
    {
        var values = new Dictionary<int, string> { [1] = "'x'", [2] = "'" + new string('9', 200) + "'" };
        var r = SqlParameters.Inline("SELECT $1, 'nhiều\r\ndòng' AS c\nWHERE v = $2", values);
        Assert.Equal("SELECT 'x', 'nhiều\ndòng' AS c\nWHERE v = '" + new string('9', 200) + "'  -- $2 = '" + new string('9', 77) + "…'", r.Sql);
    }
}

/// <summary>Câu đã thay tham số chạy trên PostgreSQL thật cho cùng kết quả với câu gửi kèm tham số (cần DBTOOLS_TEST_PG).</summary>
public class SqlParametersIntegrationTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");

    [Fact]
    public async Task Inlined_sql_returns_the_same_as_parameterized()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var ct = TestContext.Current.CancellationToken;
        await using var c = new NpgsqlConnection(Server);
        await c.OpenAsync(ct);
        const string sql = "SELECT TO_TIMESTAMP($1, 'YYYYMMDDHH24MISSMS')::text, $2::text, coalesce($3::text, 'null'), length($4) -- '$1'\n, $2 || '$2'";
        // đúng dạng server ghi log: nháy đơn nhân đôi, NULL không nháy
        var detail = "DETAIL:  Parameters: $1 = '20261126091603000', $2 = 'O''Brien 債権', $3 = NULL, $4 = 'dòng 1\ndòng 2'";
        var inlined = SqlParameters.Inline(sql, SqlParameters.Parse(detail));
        Assert.Equal(5, inlined.Replaced);

        async Task<string> Run(NpgsqlCommand cmd)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString()));
        }
        await using var withParams = new NpgsqlCommand(sql, c)
        {
            Parameters =
            {
                new() { Value = "20261126091603000" }, new() { Value = "O'Brien 債権" }, new() { Value = DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text },
                new() { Value = "dòng 1\ndòng 2" },
            },
        };
        await using var plain = new NpgsqlCommand(inlined.Sql, c);
        Assert.Equal(await Run(withParams), await Run(plain));
    }
}
