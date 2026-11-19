using DbTools.Core;
using DbTools.Services;

namespace DbTools.Tests;

/// <summary>Phần không cần server của Sao lưu / Khôi phục: đọc phiên bản, dựng tham số pg_dump / pg_restore / psql, đọc file
/// dump, tên file, kế hoạch Sao lưu trước khi chạy, cài đặt mặc định.</summary>
public class BackupTests
{
    private static readonly PgTarget Target = new("192.168.1.5", 5432, "postgres", "app_db");

    [Theory]
    [InlineData("pg_dump (PostgreSQL) 17.2", 17, 2)]
    [InlineData("pg_dump (PostgreSQL) 12.9", 12, 9)]
    [InlineData("17.2 (Ubuntu 17.2-1.pgdg22.04+1)", 17, 2)]
    [InlineData("pg_dump (PostgreSQL) 18beta1", 18, 0)]
    public void Parses_versions(string text, int major, int minor)
    {
        Assert.Equal(new Version(major, minor), PgVersion.Parse(text));
    }

    [Fact]
    public void Unparsable_version_is_null()
    {
        Assert.Null(PgVersion.Parse(""));
        Assert.Null(PgVersion.Parse("pg_dump (PostgreSQL)"));
    }

    [Fact]
    public void Auto_choice_is_newest_client_and_older_client_is_flagged()
    {
        var v12 = new PgClient(@"C:\Program Files\PostgreSQL\12\bin", new Version(12, 9));
        var v17 = new PgClient(@"C:\Program Files\PostgreSQL\17\bin", new Version(17, 2));
        Assert.Same(v17, PgVersion.Choose([v12, v17]));
        Assert.Null(PgVersion.Choose([]));
        Assert.Null(PgVersion.Mismatch(v17, 12));
        Assert.Null(PgVersion.Mismatch(v17, 17));
        Assert.Contains("cũ hơn", PgVersion.Mismatch(v12, 17));
        Assert.Equal(@"C:\Program Files\PostgreSQL\17\bin\pg_restore.exe", v17.Exe("pg_restore"));
    }

    [Fact]
    public void Dump_args_never_prompt_and_carry_no_password()
    {
        var args = PgArgs.Dump(Target, new DumpOptions(DumpFormat.Custom), @"D:\DMP\a.dump");
        Assert.Equal(["-h", "192.168.1.5", "-p", "5432", "-U", "postgres", "-d", "app_db", "-w", "-v", "-F", "c", "-f", @"D:\DMP\a.dump"], args);
        Assert.DoesNotContain(args, a => a.Contains("password", StringComparison.OrdinalIgnoreCase));

        var plain = PgArgs.Dump(Target, new DumpOptions(DumpFormat.Plain, SchemaOnly: true, Schema: "Addon"), "x.sql");
        Assert.Contains("p", plain);
        Assert.Contains("-s", plain);
        Assert.Equal("\"Addon\"", plain[plain.IndexOf("-n") + 1]); // trong "..." = đúng hoa / thường, không hiểu là mẫu
    }

    [Fact]
    public void Patterns_quote_names_literally()
    {
        Assert.Equal("\"msta_x_$s\"", PgArgs.Pattern("msta_x_$s"));
        Assert.Equal("\"a\"\"b\"", PgArgs.Pattern("a\"b"));
        Assert.Equal("\"addon\".\"App_Order\"", PgArgs.Pattern(new QualifiedName("addon", "App_Order", true)));
        var args = PgArgs.DumpRelations(Target, [new QualifiedName("public", "t1", true), new QualifiedName("public", "t2", true)], schemaOnly: true, "o.dump");
        Assert.Equal(2, args.Count(a => a == "-t"));
        Assert.Contains("-s", args);
        Assert.Contains("\"public\".\"t2\"", args);
    }

    [Fact]
    public void Restore_and_psql_args_follow_options()
    {
        var safe = PgArgs.Restore(Target, new RestoreOptions(), "f.dump");
        Assert.Contains("--single-transaction", safe);
        Assert.Contains("--exit-on-error", safe);
        Assert.DoesNotContain("--clean", safe);
        Assert.Equal("f.dump", safe[^1]);

        var all = PgArgs.Restore(Target, new RestoreOptions(Clean: true, NoOwner: true, SingleTransaction: false), "f.dump");
        Assert.Contains("--clean", all);
        Assert.Contains("--if-exists", all);
        Assert.Contains("--no-owner", all);
        Assert.Contains("--no-privileges", all);
        Assert.DoesNotContain("--single-transaction", all);

        var psql = PgArgs.Psql(Target, singleTransaction: true, "f.sql");
        Assert.Contains("ON_ERROR_STOP=1", psql);
        Assert.Contains("--single-transaction", psql);
        Assert.Contains("-w", psql);
        Assert.Equal(["-f", "f.sql"], psql[^2..]);
    }

    [Fact]
    public void Dump_file_names_are_safe_and_unique()
    {
        var time = new DateTime(2026, 11, 19, 9, 5, 0);
        Assert.Equal("app_db_20261119_0905.dump", PgArgs.DumpFileName("app_db", DumpFormat.Custom, time));
        Assert.Equal("app_db_addon_20261119_0905.sql", PgArgs.DumpFileName("app_db", DumpFormat.Plain, time, "addon"));
        Assert.Equal("a_b_20261119_0905.dump", PgArgs.DumpFileName("a/b", DumpFormat.Custom, time));
        using var folder = new TempFolder();
        folder.Write("x.dump", "1");
        folder.Write("x_2.dump", "2");
        Assert.Equal(Path.Combine(folder.Path, "x_3.dump"), PgArgs.UniquePath(folder.Path, "x.dump"));
        Assert.Equal(Path.Combine(folder.Path, "y.dump"), PgArgs.UniquePath(folder.Path, "y.dump"));
    }

    private const string ListOutput = """
        ;
        ; Archive created at 2026-11-19 09:30:12 JST
        ;     dbname: mcf_dev
        ;     TOC Entries: 12
        ;     Compression: gzip
        ;     Dump Version: 1.16-0
        ;     Format: CUSTOM
        ;     Integer: 4 bytes
        ;     Offset: 8 bytes
        ;     Dumped from database version: 17.2
        ;     Dumped by pg_dump version: 17.2
        ;
        ;
        ; Selected TOC Entries:
        ;
        5; 2615 16384 SCHEMA - addon mcf_owner
        215; 1259 16390 TABLE public app_order_all mcf_owner
        216; 1259 16395 TABLE addon app_history_table mcf_owner
        217; 1259 16400 VIEW public app_order mcf_owner
        218; 1255 16410 FUNCTION public app_order_all_tr_k_body() mcf_owner
        219; 1259 16388 SEQUENCE public app_order_no_seq mcf_owner
        3340; 0 16390 TABLE DATA public app_order_all mcf_owner
        3341; 0 16395 TABLE DATA addon app_history_table mcf_owner
        3342; 0 0 SEQUENCE SET public app_order_no_seq mcf_owner
        3200; 2606 16420 CONSTRAINT public app_order_all app_order_all_pk mcf_owner
        3201; 1259 16421 INDEX public app_order_all_ie01 mcf_owner
        3202; 2620 16430 TRIGGER public app_order_all app_order_all_tr_k mcf_owner
        3400; 0 0 COMMENT public TABLE app_order_all mcf_owner
        """;

    [Fact]
    public void Reads_pg_restore_list()
    {
        var info = DumpArchiveInfo.ParseList(ListOutput.Replace("\n", "\r\n"));
        Assert.Equal("mcf_dev", info.DbName);
        Assert.Equal("CUSTOM", info.Format);
        Assert.Equal("17.2", info.ServerVersion);
        Assert.Equal("2026-11-19 09:30:12 JST", info.CreatedAt);
        Assert.Equal(12, info.TocEntries);
        Assert.Equal(2, info.Count("TABLE"));
        Assert.Equal(2, info.Count("TABLE DATA"));
        Assert.Equal(1, info.Count("SEQUENCE SET"));
        Assert.Equal(1, info.Count("SEQUENCE"));
        Assert.Equal(1, info.Count("VIEW"));
        Assert.Equal(1, info.Count("FUNCTION"));
        Assert.Equal(["addon", "public"], info.Schemas);
        Assert.Equal(["mcf_owner"], info.Owners);
        var summary = info.Summary();
        Assert.Contains("DB gốc: mcf_dev", summary);
        Assert.Contains("2 bảng", summary);
        Assert.Contains("2 bảng có dữ liệu", summary);
        Assert.Contains("1 hàm", summary);
        Assert.Contains("1 trigger", summary);
    }

    [Fact]
    public void Recognizes_archive_and_plain_sql()
    {
        Assert.True(DumpArchiveInfo.IsArchive("PGDMP\u0001\u000e"u8));
        Assert.False(DumpArchiveInfo.IsArchive("--\n-- PostgreSQL database dump\n"u8));
        var tar = new byte[512];
        "ustar"u8.CopyTo(tar.AsSpan(257));
        Assert.True(DumpArchiveInfo.IsArchive(tar));

        var plain = DumpArchiveInfo.ParsePlainHeader("""
            --
            -- PostgreSQL database dump
            --

            -- Dumped from database version 12.9
            -- Dumped by pg_dump version 17.2

            SET statement_timeout = 0;
            """);
        Assert.True(plain.IsPlainSql);
        Assert.Equal("12.9", plain.ServerVersion);
        Assert.Equal("17.2", plain.DumpVersion);
        Assert.Contains("psql", plain.Summary());
    }

    [Fact]
    public void Backup_plan_of_sample_folder()
    {
        using var folder = new TempFolder();
        SampleScripts.WriteTo(folder);
        var files = SampleScripts.Load(folder);

        var plan = BackupPlan.Build(files, dropFirst: false);
        // DELETE + INSERT vào bảng đăng ký lịch sử → dữ liệu
        Assert.Contains(plan.DataRelations, n => n.Name == "app_history_table");
        // Bảng được tạo / thêm trigger, index, ràng buộc; view bị thay → chỉ cấu trúc
        Assert.Contains(plan.StructureRelations, n => n.Name == "app_order_all");
        Assert.Contains(plan.StructureRelations, n => n.Name == "app_order");
        Assert.Contains(plan.StructureRelations, n => n.Name == "app_order_no_seq");
        Assert.DoesNotContain(plan.StructureRelations, n => n.Name == "app_history_table");
        Assert.Contains(plan.Functions, n => n.Name == "app_order_all_tr_k_body");
        Assert.Contains(plan.Triggers, n => n.Name == "app_order_all_tr_r");
        Assert.False(plan.IsEmpty);

        // Xoá rồi tạo lại: DROP TABLE mất dữ liệu → bảng do thư mục tạo chuyển sang nhóm dữ liệu
        var drop = BackupPlan.Build(files, dropFirst: true);
        Assert.Contains(drop.DataRelations, n => n.Name == "app_order_all");
        Assert.Contains(drop.DataRelations, n => n.Name == "app_order_all_r");
        Assert.DoesNotContain(drop.StructureRelations, n => n.Name == "app_order_all");
    }

    [Fact]
    public void Backup_plan_of_drop_and_alter_drop_keeps_data()
    {
        var file = SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes("""
            DROP TABLE IF EXISTS old_tab;
            ALTER TABLE cust DROP COLUMN memo;
            ALTER TABLE cust2 ADD COLUMN memo text;
            DROP FUNCTION IF EXISTS calc_x(int);
            """), @"C:\x\a.sql", "a.sql");
        var plan = BackupPlan.Build([file], dropFirst: false);
        Assert.Contains(plan.DataRelations, n => n.Name == "old_tab");
        Assert.Contains(plan.DataRelations, n => n.Name == "cust");
        Assert.Contains(plan.StructureRelations, n => n.Name == "cust2");
        Assert.Contains(plan.Functions, n => n.Name == "calc_x");
        Assert.DoesNotContain(plan.DataRelations, n => n.Name is "drop" or "table" or "if" or "exists");
    }

    [Fact]
    public void Only_new_objects_mean_empty_plan()
    {
        var file = SqlScriptFile.Parse(System.Text.Encoding.UTF8.GetBytes("COMMENT ON TABLE x IS 'a';"), @"C:\x\c.sql", "c.sql");
        Assert.True(BackupPlan.Build([file], dropFirst: false).IsEmpty);
    }

    [Fact]
    public void Settings_defaults_and_round_trip()
    {
        var fresh = new AppSettings();
        Assert.Equal(@"D:\DMP", fresh.DumpFolder);
        Assert.True(fresh.BackupBeforeRun);
        Assert.Equal(string.Empty, fresh.PgBinFolder);

        using var folder = new TempFolder();
        var store = new SettingsStore(Path.Combine(folder.Path, "settings.json"));
        store.Save(new AppSettings { DumpFolder = @"E:\Backup", BackupBeforeRun = false, PgBinFolder = @"C:\pg\bin" });
        var loaded = store.Load();
        Assert.Equal(@"E:\Backup", loaded.DumpFolder);
        Assert.False(loaded.BackupBeforeRun);
        Assert.Equal(@"C:\pg\bin", loaded.PgBinFolder);

        // File cài đặt cũ (trước đợt 2) không có các khoá mới → mặc định
        File.WriteAllText(store.FilePath, """{ "LastFolder": "C:\\x" }""");
        var old = store.Load();
        Assert.Equal(@"D:\DMP", old.DumpFolder);
        Assert.True(old.BackupBeforeRun);
    }

    [Fact]
    public async Task Probing_a_folder_without_pg_dump_gives_null()
    {
        using var folder = new TempFolder();
        Assert.Null(await PgTools.ProbeAsync(folder.Path));
        Assert.Empty(await PgTools.FindAsync([folder.Path, Path.Combine(folder.Path, "khong-co")]));
    }
}
