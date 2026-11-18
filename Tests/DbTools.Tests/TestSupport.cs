using System.Runtime.CompilerServices;
using System.Text;
using DbTools.Core;

namespace DbTools.Tests;

internal static class TestSetup
{
    /// <summary>Giống App(): Npgsql gửi nguyên văn từng câu - đặt trước mọi lần dùng Npgsql trong test.</summary>
    [ModuleInitializer]
    internal static void Init() => AppContext.SetSwitch("Npgsql.EnableSqlRewriting", false);
}

/// <summary>Thư mục tạm, tự xoá khi xong.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AppSuite", "DbTools.Tests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Write(string relative, string content, Encoding? encoding = null)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, encoding ?? new UTF8Encoding(false));
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // file còn bị giữ - bỏ qua
        }
    }
}

/// <summary>
/// Thư mục script mẫu cùng kiểu với thư mục add-on mcframe (đặt tên chung chung, không phải dữ liệu khách): mỗi loại 1 thư
/// mục con, đầu file \encoding UTF-8, view tên có $s, hàm trigger $BODY$, bảng lịch sử + dòng đăng ký (DELETE trước INSERT).
/// Chạy theo vần ABC sẽ lỗi (CHECK / COMMENT trước bảng, view trước bảng).
/// </summary>
public static class SampleScripts
{
    public static readonly Dictionary<string, string> Files = new()
    {
        [@"CHECK\APP_ORDER_ALL_CK01.sql"] = """
            \encoding UTF-8

            ALTER TABLE app_order_all
             ADD CONSTRAINT app_order_all_ck01 CHECK (del_flg IN ('0', '1'));
            """,
        [@"COMMENT\APP_ORDER_ALL_comment.sql"] = """
            \encoding UTF-8

            COMMENT ON TABLE app_order_all IS '受注データ // đơn hàng';
            COMMENT ON COLUMN app_order_all.order_no IS '受注番号';
            """,
        [@"COMMENT\APP_ORDER_comment.sql"] = """
            \encoding UTF-8

            COMMENT ON VIEW app_order IS '受注データ (view)';
            """,
        [@"CO_VT\APP_ORDER_ALL_TR_K.sql"] = """
            \encoding UTF-8

            CREATE OR REPLACE FUNCTION app_order_all_tr_k_body() RETURNS trigger AS $BODY$
            BEGIN
                IF NEW.co_cd IS NULL THEN
                    NEW.co_cd := 'C01';
                END IF;
                RETURN NEW;
            END;
            $BODY$ LANGUAGE plpgsql;

            CREATE TRIGGER app_order_all_tr_k
            BEFORE INSERT ON app_order_all
            FOR EACH ROW
            EXECUTE FUNCTION app_order_all_tr_k_body();
            """,
        [@"HST_TAB\APP_ORDER_ALL_R.sql"] = """
            \encoding UTF-8

            CREATE TABLE app_order_all_r
            (
            hst_id numeric(24,0) DEFAULT 0 NOT NULL,
            hst_type varchar(2) NOT NULL,
            order_no varchar(15) NULL
            );

            INSERT INTO app_history_table (history_table_name, table_name, trigger_name)
            VALUES ('app_order_all_r', 'app_order_all', 'app_order_all_tr_r');
            """,
        [@"HST_TAB\deleteHistoryTab_APP1.sql"] = """
            \encoding UTF-8

            DELETE FROM app_history_table WHERE
            table_name = 'app_order_all'
            	;
            """,
        [@"HST_TR\APP_ORDER_ALL_TR_R.sql"] = """
            \encoding UTF-8

            CREATE OR REPLACE FUNCTION app_order_all_tr_r_body() RETURNS trigger AS $BODY$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    INSERT INTO app_order_all_r (hst_type, order_no) VALUES ('D', OLD.order_no);
                    RETURN OLD;
                END IF;
                INSERT INTO app_order_all_r (hst_type, order_no) VALUES ('U', NEW.order_no);
                RETURN NEW;
            END;
            $BODY$ LANGUAGE plpgsql;

            CREATE TRIGGER app_order_all_tr_r
            AFTER INSERT OR DELETE OR UPDATE ON app_order_all
            FOR EACH ROW
            EXECUTE FUNCTION app_order_all_tr_r_body();
            ALTER TABLE app_order_all DISABLE TRIGGER app_order_all_tr_r;
            """,
        [@"OBJECT\APP_ORDER.sql"] = """
            \encoding UTF-8

            CREATE OR REPLACE VIEW app_order(
            	order_no,
            	del_flg
            )
            AS
            SELECT
            	app_order_org.order_no,
            	app_order_org.del_flg
            FROM
            	app_order_all app_order_org
            WHERE
            	app_order_org.co_cd = (SELECT co_cd FROM app_context WHERE pid = pg_backend_pid())
            ;
            """,
        [@"OBJECT\APP_ORDER_@S.sql"] = """
            \encoding UTF-8

            CREATE OR REPLACE VIEW app_order_$s(
            	order_no
            )
            AS
            SELECT app_order_$s_org.order_no
            FROM app_order_all app_order_$s_org
            ;
            """,
        [@"OBJECT\APP_ORDER_ALL.sql"] = """
            \encoding UTF-8

            CREATE TABLE app_order_all
            (order_no varchar (15) NOT NULL,
            co_cd varchar (10) NULL,
            del_flg char (1) DEFAULT '0' NOT NULL,
            amount numeric (15,2) DEFAULT nextval('app_order_no_seq') NULL
            );

            ALTER TABLE app_order_all
            ADD CONSTRAINT app_order_all_pk PRIMARY KEY(order_no);

            CREATE INDEX app_order_all_ie01
            ON app_order_all USING BTREE (co_cd, order_no);
            """,
        [@"SEQ\APP_ORDER_NO_SEQ.sql"] = """
            \encoding UTF-8

            CREATE SEQUENCE app_order_no_seq
            	INCREMENT 1
            	START 0
            	MAXVALUE 999999999999999
            	MINVALUE 0
            	NO CYCLE;
            """,
    };

    /// <summary>Đối tượng "nền" mà script mẫu dùng nhưng không tạo (như bảng / hàm có sẵn của mcframe).</summary>
    public const string BaseObjects = """
        CREATE TABLE app_context (pid int, co_cd varchar(10));
        CREATE TABLE app_history_table (history_table_name varchar(30), table_name varchar(30), trigger_name varchar(30));
        """;

    public static void WriteTo(TempFolder folder)
    {
        foreach (var (path, content) in Files)
        {
            folder.Write(path, content.Replace("\r\n", "\n").Replace("\n", "\r\n")); // file thật dùng CRLF
        }
    }

    public static List<SqlScriptFile> Load(TempFolder folder) => Directory
        .EnumerateFiles(folder.Path, "*.sql", SearchOption.AllDirectories)
        .Select(p => SqlScriptFile.Load(p, folder.Path))
        .ToList();
}
