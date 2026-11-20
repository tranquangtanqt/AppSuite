using DbTools.Core;

namespace DbTools.Tests;

public class SqlSplitterTests
{
    [Fact]
    public void Splits_on_semicolons_and_keeps_start_lines()
    {
        var result = SqlSplitter.Split("CREATE TABLE a (id int);\r\n\r\nCREATE TABLE b\r\n(id int);\r\n");

        Assert.Equal(2, result.Statements.Count);
        Assert.Equal("CREATE TABLE a (id int)", result.Statements[0].Text);
        Assert.Equal(1, result.Statements[0].StartLine);
        Assert.Equal(3, result.Statements[1].StartLine);
    }

    [Fact]
    public void Ignores_semicolons_in_strings_quoted_names_and_comments()
    {
        var sql = """
            INSERT INTO t VALUES ('a;b', 'it''s;', E'x\';y');
            SELECT "we;ird" FROM t; -- chú thích ; ở đây
            /* khối ; /* lồng ; */ vẫn chú thích ; */ SELECT 1;
            """;

        var result = SqlSplitter.Split(sql);

        Assert.Equal(3, result.Statements.Count);
        Assert.EndsWith("E'x\\';y')", result.Statements[0].Text);
        Assert.Equal("SELECT \"we;ird\" FROM t", result.Statements[1].Text);
        Assert.Equal("SELECT 1", result.Statements[2].Text);
    }

    [Fact]
    public void Dollar_quoted_function_body_is_one_statement()
    {
        var sql = """
            CREATE OR REPLACE FUNCTION f() RETURNS trigger AS $BODY$
            BEGIN
                NEW.a := 1;
                RETURN NEW;
            END;
            $BODY$ LANGUAGE plpgsql;

            CREATE TRIGGER t BEFORE INSERT ON x FOR EACH ROW EXECUTE FUNCTION f();
            DO $$ BEGIN PERFORM 1; END $$;
            """;

        var result = SqlSplitter.Split(sql);

        Assert.Equal(3, result.Statements.Count);
        Assert.EndsWith("LANGUAGE plpgsql", result.Statements[0].Text);
        Assert.Equal(8, result.Statements[1].StartLine);
    }

    [Fact]
    public void Sql_standard_function_body_begin_atomic_is_one_statement()
    {
        // PostgreSQL 14+: thân hàm SQL chuẩn có ; bên trong - như psql, ; chỉ kết thúc câu khi BEGIN / CASE ... END đã đóng hết
        var sql = """
            CREATE FUNCTION a.f_count() RETURNS bigint LANGUAGE sql
            BEGIN ATOMIC
                SELECT count(*) FROM a.cust;
            END;
            CREATE OR REPLACE PROCEDURE a.p_fix(x int) LANGUAGE sql
            BEGIN ATOMIC
                UPDATE a.cust SET kind = CASE WHEN x > 0 THEN 'a;b' ELSE 'c' END;
                INSERT INTO a.cust VALUES (x, (CASE WHEN x > 0 THEN 1 END));
                DELETE FROM a.cust WHERE id = x;
            END;
            create or replace function a.f2() returns int language sql begin atomic; select 1; end;
            BEGIN;
            SELECT CASE WHEN true THEN 1 END;
            COMMIT;
            CREATE TABLE t (id int DEFAULT (CASE WHEN true THEN 1 END));
            CREATE FUNCTION a.f3() RETURNS int AS $$ BEGIN RETURN 1; END $$ LANGUAGE plpgsql;
            SELECT 2;
            """;

        var s = SqlSplitter.Split(sql).Statements;

        Assert.Equal(9, s.Count);
        Assert.StartsWith("CREATE FUNCTION a.f_count()", s[0].Text);
        Assert.EndsWith("END", s[0].Text);
        Assert.StartsWith("CREATE OR REPLACE PROCEDURE a.p_fix", s[1].Text);
        Assert.Contains("DELETE FROM a.cust WHERE id = x;", s[1].Text);
        Assert.Equal(5, s[1].StartLine);
        Assert.EndsWith("end", s[2].Text);
        Assert.Equal(["BEGIN", "SELECT CASE WHEN true THEN 1 END", "COMMIT"], s.Skip(3).Take(3).Select(x => x.Text).ToArray()); // BEGIN giao dịch không phải thân hàm
        Assert.StartsWith("CREATE TABLE t", s[6].Text);
        Assert.EndsWith("LANGUAGE plpgsql", s[7].Text);
        Assert.Equal("SELECT 2", s[8].Text);
    }

    [Fact]
    public void Dollar_after_identifier_is_part_of_the_name_not_a_quote()
    {
        // Tên view kiểu mcframe: msta_x_$s, msta_x_$u; alias cũng có $.
        var sql = "CREATE OR REPLACE VIEW v_$s(a) AS SELECT v_$s_org.a FROM t v_$s_org;\nCOMMENT ON VIEW v_$s IS 'x';";

        var result = SqlSplitter.Split(sql);

        Assert.Equal(2, result.Statements.Count);
        Assert.StartsWith("COMMENT ON VIEW v_$s", result.Statements[1].Text);
    }

    [Fact]
    public void Psql_meta_commands_are_separated()
    {
        var result = SqlSplitter.Split("\\encoding UTF-8\r\n\r\nSELECT 1;\n\\i other.sql\nSELECT 2");

        Assert.Equal(2, result.MetaCommands.Count);
        Assert.Equal("encoding", result.MetaCommands[0].Name);
        Assert.Equal("UTF-8", result.MetaCommands[0].Argument);
        Assert.Equal(1, result.MetaCommands[0].Line);
        Assert.Equal("i", result.MetaCommands[1].Name);
        Assert.Equal(4, result.MetaCommands[1].Line);
        Assert.Equal(["SELECT 1", "SELECT 2"], result.Statements.Select(s => s.Text));
    }

    [Fact]
    public void Lone_semicolons_and_comments_only_make_no_statement()
    {
        var result = SqlSplitter.Split("-- chỉ có chú thích\n;\n  ;\n/* x */");

        Assert.Empty(result.Statements);
    }
}

public class SqlAnalyzerTests
{
    private static StatementInfo A(string sql) => SqlAnalyzer.Analyze(new SqlStatement(sql, 1, 0));

    [Theory]
    [InlineData("CREATE TABLE app_order_all (id int)", StatementKind.CreateTable, "app_order_all", false)]
    [InlineData("create table if not exists s1.T1 (id int)", StatementKind.CreateTable, "t1", true)]
    [InlineData("CREATE UNLOGGED TABLE x (id int)", StatementKind.CreateTable, "x", false)]
    [InlineData("CREATE SEQUENCE seq_no INCREMENT 1", StatementKind.CreateSequence, "seq_no", false)]
    [InlineData("CREATE OR REPLACE VIEW v_$s(a) AS SELECT 1", StatementKind.CreateView, "v_$s", true)]
    [InlineData("CREATE VIEW v AS SELECT 1", StatementKind.CreateView, "v", false)]
    [InlineData("CREATE MATERIALIZED VIEW mv AS SELECT 1", StatementKind.CreateView, "mv", false)]
    [InlineData("CREATE OR REPLACE FUNCTION f$func() RETURNS trigger AS $$ BEGIN RETURN NEW; END $$ LANGUAGE plpgsql", StatementKind.CreateFunction, "f$func", true)]
    [InlineData("CREATE PROCEDURE p(a int) LANGUAGE sql AS 'select 1'", StatementKind.CreateProcedure, "p", false)]
    [InlineData("CREATE UNIQUE INDEX ix1 ON t USING btree (a)", StatementKind.CreateIndex, "ix1", false)]
    [InlineData("CREATE SCHEMA IF NOT EXISTS app", StatementKind.CreateSchema, "app", true)]
    [InlineData("CREATE TYPE mood AS ENUM ('a')", StatementKind.CreateType, "mood", false)]
    [InlineData("CREATE TABLE \"MixedCase\" (id int)", StatementKind.CreateTable, "MixedCase", false)]
    public void Recognizes_create_statements(string sql, StatementKind kind, string name, bool rerunnable)
    {
        var info = A(sql);

        Assert.Equal(kind, info.Kind);
        Assert.Equal(name, info.Object?.Key);
        Assert.Equal(rerunnable, info.Rerunnable);
        Assert.True(info.CreatesObject);
    }

    [Fact]
    public void Trigger_knows_its_table()
    {
        var info = A("CREATE TRIGGER t_r\nAFTER INSERT OR DELETE OR UPDATE ON app_order_all\nFOR EACH ROW\nEXECUTE FUNCTION f()");

        Assert.Equal(StatementKind.CreateTrigger, info.Kind);
        Assert.Equal("t_r", info.Object!.Key);
        Assert.Equal("app_order_all", info.Table!.Key);
    }

    [Fact]
    public void Add_constraint_knows_table_and_name()
    {
        var info = A("ALTER TABLE app_order_all\n ADD CONSTRAINT app_order_all_ck01 CHECK (del_flg IN ('0', '1'))");

        Assert.Equal(StatementKind.AddConstraint, info.Kind);
        Assert.Equal("app_order_all_ck01", info.Object!.Key);
        Assert.Equal("app_order_all", info.Table!.Key);
    }

    [Fact]
    public void Alter_with_several_actions_is_alter_other()
    {
        Assert.Equal(StatementKind.AlterOther, A("ALTER TABLE t ADD CONSTRAINT c CHECK (a > 0), ADD COLUMN b int").Kind);
        Assert.Equal(StatementKind.AlterOther, A("ALTER TABLE t DISABLE TRIGGER t_r").Kind);
        Assert.True(A("ALTER TABLE t DROP COLUMN b").ChangesData);
    }

    [Theory]
    [InlineData("INSERT INTO hist_table (a) VALUES (1)", StatementKind.Insert, "hist_table")]
    [InlineData("DELETE FROM hist_table WHERE table_name = 'x'", StatementKind.Delete, "hist_table")]
    [InlineData("UPDATE ONLY s.t SET a = 1", StatementKind.Update, "t")]
    [InlineData("TRUNCATE TABLE t", StatementKind.Truncate, "t")]
    public void Recognizes_data_statements(string sql, StatementKind kind, string table)
    {
        var info = A(sql);

        Assert.Equal(kind, info.Kind);
        Assert.Equal(table, info.Table!.Key);
        Assert.Equal(kind != StatementKind.Insert, info.ChangesData);
    }

    [Theory]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("rollback")]
    [InlineData("START TRANSACTION")]
    [InlineData("END")]
    public void Transaction_control_is_flagged(string sql) => Assert.Equal(StatementKind.TransactionControl, A(sql).Kind);

    [Theory]
    [InlineData("CREATE DATABASE x")]
    [InlineData("DROP DATABASE x")]
    [InlineData("VACUUM ANALYZE t")]
    [InlineData("CREATE INDEX CONCURRENTLY ix ON t (a)")]
    [InlineData("ALTER SYSTEM SET work_mem = '1MB'")]
    public void Statements_that_cannot_run_in_a_transaction_are_flagged(string sql) =>
        Assert.Equal(StatementKind.NotInTransaction, A(sql).Kind);

    [Fact]
    public void Names_include_identifiers_inside_function_bodies_but_not_strings()
    {
        var info = A("CREATE FUNCTION f() RETURNS trigger AS $BODY$ BEGIN INSERT INTO app_order_all_r VALUES (NEW.a); RETURN NEW; END; $BODY$ LANGUAGE plpgsql");
        var insert = A("INSERT INTO hist (t) VALUES ('app_order_all')");

        Assert.Contains("app_order_all_r", info.Names);
        Assert.DoesNotContain("app_order_all", insert.Names);
    }

    [Fact]
    public void Function_args_drop_defaults()
    {
        var info = A("CREATE OR REPLACE FUNCTION s.calc(a integer DEFAULT 0, b varchar(10) = 'x', OUT c numeric) RETURNS numeric AS $$ SELECT 1 $$ LANGUAGE sql");

        Assert.Equal("s.calc", info.Object!.Sql);
        Assert.Equal("a integer, b varchar(10), out c numeric", info.FunctionArgs);
        Assert.Equal(string.Empty, A("CREATE FUNCTION f() RETURNS int AS 'select 1' LANGUAGE sql").FunctionArgs);
    }

    [Fact]
    public void Verb_is_short_and_upper_case()
    {
        Assert.Equal("CREATE TABLE", A("create table x (a int)").Verb);
        Assert.Equal("CREATE VIEW", A("CREATE OR REPLACE VIEW v AS SELECT 1").Verb);
        Assert.Equal("CREATE INDEX", A("CREATE UNIQUE INDEX i ON t (a)").Verb);
        Assert.Equal("DELETE", A("DELETE FROM t").Verb);
    }

    [Fact]
    public void Quoted_names_are_requoted_for_sql()
    {
        Assert.Equal("\"MixedCase\"", A("CREATE TABLE \"MixedCase\" (id int)").Object!.Sql);
        Assert.Equal("v_$s", A("CREATE VIEW v_$s AS SELECT 1").Object!.Sql);
    }
}
