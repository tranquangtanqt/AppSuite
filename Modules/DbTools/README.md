# DbTools

Chạy cả thư mục script `.sql` lên PostgreSQL theo đúng thứ tự, trong **1 transaction**: lỗi ở câu nào là huỷ (rollback)
hết, DB không bị dở dang. Thay cho các file bat kiểu `FOR /R %%F IN (*.sql) DO psql -f %%F` (chạy theo vần ABC, lỗi giữa
chừng thì DB đã áp một nửa).

Là 1 exe WinUI 3 **độc lập** như mọi module khác: chỉ reference `Common` + `SharedUI`, không reference `MainLauncher`.
Chạy riêng: `dotnet run --project Modules\DbTools\DbTools.csproj` hoặc mở csproj trong VS rồi F5.

Đợt 2 (2026-11-19): Sao lưu / Khôi phục (Dump / Restore / Copy DB bằng pg_dump / pg_restore / psql của máy), Phiên kết nối
(xem / ngắt phiên), Sao lưu trước khi chạy thư mục script.

## Trang

| Trang | Việc |
|---|---|
| **Chạy thư mục SQL** | Đọc thư mục (gồm thư mục con) → danh sách file theo thứ tự chạy → Kiểm tra / Chạy thử / Chạy |
| **Sao lưu / Khôi phục** | Dump (pg_dump), Restore (pg_restore / psql), Copy DB (TEMPLATE cùng server / dump-restore khác server) |
| **Thông tin DB** | Tra cứu cấu trúc (thay DBeaver, chỉ đọc): tổng quan, bảng (cột, index, ràng buộc, trigger, DDL), view / hàm / trigger / sequence / extension + định nghĩa |
| **Chạy SQL** | Ô soạn câu SQL → bảng kết quả; mặc định chỉ đọc (BEGIN READ ONLY ... ROLLBACK), công tắc Cho phép ghi (hỏi lại, Production gõ tên DB); giới hạn dòng, Huỷ, chép TSV, Xuất CSV đủ dòng (CsvEditor), lịch sử 200 câu |
| **Sinh dữ liệu mẫu** | Từ vài dòng mẫu (bảng trong DB / file CSV) sinh N dòng giả → CSV và / hoặc INSERT (không ghi vào DB) |
| **Phiên kết nối** | Ai đang kết nối vào DB nào (pg_stat_activity), ngắt phiên đã chọn / ngắt hết |
| **Cài đặt** (cuối menu) | Thời gian chờ kết nối (5 - 120 giây, mặc định 15, mọi hồ sơ / mọi trang), thư mục dump, thư mục PostgreSQL client, Sao lưu trước khi chạy, thư mục dữ liệu của app |
| **Nhật ký server** | Đọc file log của server qua SQL (chỉ đọc): cấu hình log bằng lời, danh sách file, bảng giờ / user / DB / máy / ứng dụng / loại câu / ms / SQL, lọc, chi tiết, xuất CSV / TXT; Theo dõi (mỗi 2 giây đọc phần mới), cảnh báo dung lượng log; Đổi cấu hình log (ALTER SYSTEM + reload, Về mặc định) |
| **Lịch sử chạy** | Mọi lần Chạy / Chạy thử / Dump / Restore / Copy DB: lúc nào, database nào, thư mục / file nào + bản svn / git, kết quả, file log |
| **Kết nối** | Hồ sơ kết nối PostgreSQL (host, port, database, user, mật khẩu) xếp theo **cây thư mục** + ô tìm (xem mục Cây hồ sơ), Thử kết nối (nút Huỷ khi đang chờ - `WaitAsync(token)` thôi chờ ngay, đổi hồ sơ cũng huỷ); ô **Quan trọng (Production)** (xem mục Hồ sơ Production) |

F1 / nút ? / mục Hướng dẫn: cửa sổ hướng dẫn dùng chung `SharedUI.Help.HelpWindow`, nội dung ở `Core\HelpContent.cs`.

## Chạy thư mục SQL

1. **Đọc thư mục**: mọi `*.sql` (bỏ `.svn`, `.git`), mỗi file được tách thành từng câu (`Core\SqlSplitter`: hiểu chuỗi
   `'...'`, `E'...'`, tên `"..."`, thân hàm `$tag$...$tag$`, chú thích `--` / `/* */` lồng nhau; `$` dính sau tên như
   `msta_x_$s` là 1 phần của tên; thân hàm SQL chuẩn `CREATE FUNCTION / PROCEDURE ... BEGIN ATOMIC ...; END` là 1 câu - như
   psql, `BEGIN` / `CASE` ... `END` trong câu CREATE FUNCTION / PROCEDURE chưa đóng thì `;` không kết thúc câu) rồi nhận loại lệnh (`Core\SqlAnalyzer`: CREATE TABLE / VIEW / FUNCTION / TRIGGER / INDEX,
   ALTER TABLE ADD CONSTRAINT, COMMENT, INSERT / UPDATE / DELETE...).
2. **Thứ tự tự động** (`Core\RunOrder`):
   - File A nhắc tới đối tượng do file B tạo (kể cả trong thân hàm) → B chạy trước A.
   - File DELETE / TRUNCATE bảng X chạy trước file INSERT vào X ("dọn trước, ghi sau" - vd `deleteHistoryTab_*.sql`
     trước `*_R.sql` cùng ghi `fcm_history_table`).
   - Còn lại theo loại: schema / kiểu / sequence → bảng → view → hàm → trigger → index / ràng buộc → comment → dữ liệu;
     cùng loại theo đường dẫn (hiểu số). Vòng phụ thuộc → cắt theo loại và ghi chú trong nhật ký.
   - Chỉnh tay: kéo dòng, ▲ / ▼ (Alt+↑ / Alt+↓), Bỏ khỏi danh sách. Thứ tự + file đã bỏ nhớ theo thư mục
     (`Data\Config\settings.json`); Sắp lại tự động = quên thứ tự đã nhớ.
3. **Kiểm tra** (chỉ đọc catalog): CREATE không có OR REPLACE / IF NOT EXISTS mà đối tượng đã có → cảnh báo "sẽ lỗi";
   lệnh psql khác `\encoding`, BEGIN / COMMIT trong file, lệnh không chạy được trong transaction (CREATE DATABASE, VACUUM,
   CREATE INDEX CONCURRENTLY...) → lỗi; DELETE / UPDATE / TRUNCATE / DROP → cảnh báo. Encoding (`SqlScriptFile.Decode`,
   2026-11-23): file không phải UTF-8 đọc theo Shift-JIS, cảnh báo chỉ khi có byte không hợp lệ theo Shift-JIS (giải mã
   `DecoderFallback.ExceptionFallback`); gần như toàn UTF-8 (ký tự UTF-8 hợp lệ ≥ 4 × byte hỏng) → đọc UTF-8 + cảnh báo (đọc
   Shift-JIS thì chữ UTF-8 thành rác). Chữ đã giải mã gửi lên bằng UTF-8 (Npgsql) - test chạy file Shift-JIS lên PostgreSQL thật:
   chữ Nhật vào bảng / COMMENT đúng. Nhật ký ghi 1 dòng "N file không phải UTF-8".
   **Chuyển UTF-8** (`Core\Utf8Convert`): bản UTF-8 không BOM (psql coi BOM là ký tự lạ) của file không phải UTF-8, `\encoding
   SJIS` → `\encoding UTF8`; đích: `%TEMP%\AppSuite\DbTools\UTF8\<thư mục>_<giờ>` / `<thư mục dump>\UTF8\<thư mục>_<giờ>` (chép
   kèm file UTF-8, giữ thư mục con) hoặc ghi đè file gốc (chỉ file không phải UTF-8, ghi file `.utf8tmp` rồi thay; xong đọc lại
   danh sách giữ thứ tự). Lựa chọn nhớ ở `AppSettings.Utf8Target`; hộp kết quả có nút Mở thư mục.
   **Cảnh báo phụ thuộc** (2026-11-20, cả lúc Chạy thử / Chạy): `Core\TableChanges.Find` nhận DROP TABLE (nhiều bảng,
   CASCADE), ALTER TABLE ... DROP COLUMN / ALTER COLUMN TYPE / SET DATA TYPE / RENAME COLUMN / RENAME TO / SET SCHEMA (tách
   các thao tác theo dấu phẩy ngoài ngoặc) + bảng của "Xoá đối tượng cũ rồi tạo lại"; bảng đang có (`PgInfo.FindTableAsync`,
   `to_regclass` theo search_path của hồ sơ) → `PgInfo.DependentsAsync` (như tab Phụ thuộc) → `TableChanges.Describe` chọn cái
   bị ảnh hưởng thật (xoá bảng: view / khoá ngoại / hàm SQL chuẩn = sẽ lỗi, hàm nhắc tên = lỗi lúc chạy; cột: view / khoá
   ngoại dùng đúng cột đó, hàm nhắc tên cột; đổi tên: chỉ hàm), bỏ đối tượng thư mục tạo lại. Cảnh báo gắn vào dòng file (cột
   Kết quả), nhật ký và hộp hỏi lại (tối đa 10 dòng). Không tra được (quyền...) thì ghi nhật ký, không chặn.
4. **Chạy thử**: chạy hết rồi ROLLBACK - bắt mọi lỗi thật mà DB không đổi.
5. **Chạy**: hỏi lại (host:port/database, file xoá / sửa dữ liệu, bảng sẽ mất dữ liệu; nút mặc định là Huỷ) →
   `BEGIN` → mọi câu → `COMMIT`. Lỗi / Huỷ → `ROLLBACK`, báo file + **dòng** lỗi (vị trí lỗi PostgreSQL trả về đổi ra dòng
   trong file) + SQLSTATE, chi tiết, gợi ý.
6. **Schema** (ô cạnh ô database, nút ✕ = về mặc định; lưu theo hồ sơ: `ConnectionProfile.Schema` / `IncludePublic`): để trống = theo
   search_path mặc định của server / user (thường là public). Có giá trị → `Pg.OpenAsync` chạy
   `SET search_path TO "<schema>"[, public]` ngay sau khi mở kết nối (chỉ kết nối đó, không pool), nên Kiểm tra / Chạy thử /
   Chạy / đếm dữ liệu cùng nhìn 1 schema; tên có ghi schema trong file vẫn theo file. "Tìm cả trong public" (bật sẵn) để
   dùng được bảng / hàm nền ở public. Schema chưa có → báo lỗi, không tự `CREATE SCHEMA`. Kiểm tra "đã có" xét
   `current_schema()` (nơi CREATE tạo đối tượng); trigger / ràng buộc xét cả search_path (bảng có thể ở public). Hộp thoại
   hỏi lại, nhật ký, lịch sử ghi schema.
7. **Xoá đối tượng cũ rồi tạo lại** (`Core\DropScript`): trước khi chạy, `DROP ... IF EXISTS` các đối tượng thư mục tạo
   ra theo thứ tự ngược, **không CASCADE**, trong cùng transaction. Trigger luôn xoá riêng (trước hàm trigger); index /
   ràng buộc trên bảng do thư mục tạo thì đi theo bảng. INSERT / UPDATE / DELETE không tự đảo ngược - liệt kê cuối
   script. **Xem script xoá** ghi script ra `%TEMP%\AppSuite\DbTools\drop_*.sql` và mở ra. Đã chọn schema: bảng / view /
   hàm / sequence / kiểu không ghi schema được ghi rõ schema đó (search_path có public thì `DROP TABLE x` có thể xoá nhầm
   `public.x`); index / trigger / ràng buộc trên bảng có sẵn giữ nguyên.

8. **Sao lưu trước khi chạy** (ô tích bật sẵn, nhớ trong `settings.json` `BackupBeforeRun`; chỉ Chạy, không Chạy thử):
   `Core\BackupPlan` liệt kê đối tượng script sẽ đổi - bảng bị INSERT / UPDATE / DELETE / TRUNCATE / DROP / ALTER ... DROP
   hoặc bị xoá-rồi-tạo (cả dữ liệu), bảng / view / sequence bị thay hoặc thêm trigger / index / ràng buộc (chỉ cấu trúc),
   hàm bị CREATE OR REPLACE / DROP, trigger bị tạo lại. `Services\PreRunBackup` đối chiếu catalog (`to_regclass` theo
   search_path của kết nối) rồi ghi `<thư mục dump>\TruocKhiChay\yyyyMMdd_HHmmss_<db>[_<schema>]\`: `bang-du-lieu.dump`
   (`pg_dump -Fc -t`), `cau-truc.dump` (`-s -t`), `ham-trigger.sql` (`pg_get_functiondef` / `pg_get_triggerdef`),
   `doc-ky.txt` (có gì + lệnh lấy lại). Không có gì đang có bị đụng → bỏ qua. Lỗi (vd không có pg_dump) → không chạy,
   xoá thư mục dở dang. Lịch sử ghi `BackupPath`, trang Lịch sử có nút Mở bản sao lưu.

## Sao lưu / Khôi phục

Gọi `pg_dump` / `pg_restore` / `psql` của PostgreSQL cài trên máy (`Services\PgTools`): Tự động = bản mới nhất trong
`Program Files\PostgreSQL\*\bin` và PATH (đọc phiên bản bằng `--version`); chọn tay thư mục bin được (`settings.json`
`PgBinFolder`). pg_dump cũ hơn server → báo trước (`PgVersion.Mismatch`). Tham số dựng ở `Core\PgCommands` (`PgArgs`,
ArgumentList nên không lo dấu nháy): luôn `-w` (không hỏi mật khẩu → không treo), mật khẩu qua biến môi trường `PGPASSWORD`
của tiến trình con (không trên dòng lệnh, không vào log), `LC_MESSAGES=C`; tên bảng trong `-t` / `-n` luôn trong `"..."`
(khớp nguyên văn kể cả `$`, hoa / thường). Huỷ = kill cả cây tiến trình. Dòng in ra gom 0,25 giây / lần; ô nhật ký chỉ
hiện 200 KB cuối (file log đủ).

- **Thư mục lưu dump**: `settings.json` `DumpFolder`, mặc định `D:\DMP` (như các file bat cũ), đổi trên trang.
- **Dump**: hồ sơ + database (ô gõ được, mở ô = danh sách DB của server), Custom (`-Fc`) / SQL thường (`-Fp`), chỉ cấu trúc
  (`-s`), chỉ 1 schema (`-n`). File `<db>[_<schema>]_yyyyMMdd_HHmm.dump|.sql` (trùng thì `_2`...) + `.log` cùng tên. Lỗi →
  xoá file dở dang.
- **Restore**: Xem nội dung = `pg_restore -l` → `Core\DumpArchiveInfo` (DB gốc, lúc tạo, bản PostgreSQL, số mục theo loại,
  schema, owner); file không có chữ ký `PGDMP` / tar → SQL thường, chạy bằng `psql -q -v ON_ERROR_STOP=1`. Đích: Tạo DB mới
  (`CREATE DATABASE ... TEMPLATE template0` trên DB bảo trì `postgres`, tên đã có → lỗi) hoặc Ghi vào DB có sẵn (phải có;
  tuỳ chọn `--clean --if-exists`). Tuỳ chọn Bỏ owner / quyền (`--no-owner --no-privileges`), Lỗi là huỷ hết (bật sẵn:
  `--single-transaction --exit-on-error`). Hỏi lại (owner trong file khác user đích → cảnh báo). Không có DROP DATABASE.
- **Copy DB**: cùng host + port → `CREATE DATABASE <mới> TEMPLATE <nguồn>`; nguồn còn phiên → hộp thoại báo, bấm "Ngắt phiên
  rồi copy" mới ngắt (`pg_terminate_backend`, thử lại tối đa 3 lần). Khác server → `pg_dump -Fc` ra file tạm trong thư mục
  dump → tạo DB trên đích → `pg_restore` 1 transaction (Bỏ owner / quyền bật sẵn) → xoá file tạm.
- **Xem lệnh** (nút cạnh Dump / Restore / Copy, `Core\CommandPreview`): các bước sẽ chạy với lựa chọn hiện tại thành script
  .bat (`chcp 65001`, `REM` từng bước, `set PGPASSWORD=<mật khẩu của hồ sơ ...>` để trống, câu SQL Npgsql viết thành
  `psql -c`), hộp thoại có nút Copy. Dùng chung `PgArgs` và `PgSql` (CREATE DATABASE, kiểm tra bản / DB có chưa, ngắt phiên)
  với lúc chạy thật nên luôn khớp; dấu nháy theo quy tắc của ArgumentList (MSVC). Test chạy thật script dán ra bằng cmd.
- Mỗi việc ghi 1 dòng Lịch sử (Mode = Dump / Restore / Copy DB, `Folder` = file dump hoặc nguồn) + file log.

## Thông tin DB

`Services\PgInfo` đọc catalog của 1 database (chỉ SELECT; bỏ pg_catalog, information_schema, pg_toast, pg_temp), 1 lần cho cả
trang - lọc / sắp làm phía app:

- **Tổng quan**: bản server, `pg_database_size`, encoding / collate / ctype, owner, `pg_postmaster_start_time`, số phiên, số đối
  tượng; theo schema: owner, số bảng / view / hàm / trigger / sequence, dung lượng.
- **Bảng**: schema.tên, loại (phân vùng / phân vùng con / bảng ngoài), số dòng ước lượng `greatest(n_live_tup, reltuples)` (≈) -
  Đếm chính xác = `count(*)` các bảng đang hiện, dung lượng tổng / bảng / index, số cột, có PK, COMMENT, lần VACUUM / ANALYZE.
  Lọc schema + một phần tên, sắp theo tên / dung lượng / số dòng.
- **Chi tiết bảng** (bấm đúp / Enter / Xem chi tiết): cột (`format_type`, NOT NULL, mặc định / identity / cột tính, COLLATE
  khác mặc định, PK, FK → bảng(cột), comment), index (`pg_get_indexdef`, dung lượng), ràng buộc (`pg_get_constraintdef`),
  trigger (`pg_get_triggerdef`, bật / tắt), DDL. PostgreSQL không có hàm lấy DDL bảng → `Core\TableDdl` dựng lại theo thứ tự
  của pg_dump: CREATE TABLE (cả PARTITION BY / PARTITION OF / INHERITS / UNLOGGED / FOREIGN) → ADD CONSTRAINT (FK sau cùng) →
  CREATE INDEX (không thuộc ràng buộc) → CREATE TRIGGER (+ DISABLE) → COMMENT ON. Không có GRANT / owner. Test khứ hồi: DDL
  dựng ra chạy vào DB trống rồi dựng lại phải y hệt.
- **Đối tượng khác**: view / materialized view (`pg_get_viewdef` → CREATE OR REPLACE VIEW), hàm / procedure
  (`pg_get_functiondef`), trigger, sequence (`pg_sequences`, giá trị hiện tại → CREATE SEQUENCE), extension. Chọn dòng → định
  nghĩa + Copy.
- **Phụ thuộc** (tab cuối chi tiết bảng, 2026-11-20) - `PgInfo.DependentsAsync`: view (`pg_depend` của rule `pg_rewrite`, đệ quy
  tối đa 10 lớp → view dựng trên view; cột view dùng = `refobjsubid`), khoá ngoại `confrelid` từ bảng khác, trigger của bảng,
  sequence thuộc cột (`deptype a / i`), hàm thân SQL chuẩn (`pg_depend` classid pg_proc); hàm / procedure nhắc tên bảng trong
  mã nguồn = `Core\DbDependencies` `SourceSearch.FindTable` (token qua `SqlLexer`: bỏ chú thích, đọc 1 lớp chuỗi '...' cho SQL
  động; bỏ `x.tên` khác schema, `tên(` gọi hàm, tên sau FUNCTION / PROCEDURE; ghi rõ schema / theo tên) + trigger bảng khác gọi
  các hàm đó. `DependencyImpact.Describe` viết dòng Ảnh hưởng (DROP bị chặn / CASCADE xoá gì / xoá theo bảng / hàm lỗi lúc chạy /
  cột đang dùng trong view).
- **Tìm trong mã nguồn** (tab cuối Đối tượng khác, 2026-11-20): `PgInfo.SourcesAsync` đọc 1 lần định nghĩa mọi view + hàm /
  procedure (bỏ hàm của extension; chữ giống hệt ô Định nghĩa nên số dòng / vị trí khớp), `SourceSearch.Find` (cả từ, phân biệt
  hoa thường) tìm phía app; chọn kết quả → `DbInfoPage.Highlight` bôi đen + cuộn tới chỗ khớp đầu. Đọc lại thì bỏ bộ nhớ đệm.

## Sinh dữ liệu mẫu

- **Nguồn** (`Services\MockSource`): bảng trong DB - `PgInfo.TableDetailAsync` (kiểu, NOT NULL, identity / cột tính / DEFAULT
  nextval = DB tự sinh), N dòng đầu ép `::text`, khoá chính / duy nhất (danh sách cột), `max()` của cột không trùng 1 cột, giá trị
  đang có của khoá ngoại 1 cột (`DISTINCT`, tối đa 5000); hoặc file CSV (`MockOutput.ReadCsv`: UTF-8 / Shift-JIS qua
  `SqlScriptFile.Decode`, phẩy / tab, ô trống / `NULL` = NULL; kiểu đoán bằng `MockType.Infer`). Từ Thông tin DB → chi tiết bảng →
  Sinh dữ liệu mẫu (`MockDataViewModel.Pending`).
- **Cách sinh** (`Core\MockData`, `MockSuggest.For` đề xuất): Bỏ qua (DB tự sinh), Tăng dần (số / mã giữ tiền tố + số chữ số, sau
  max đang có; số 1, 2, 3... và mã A001, A002 cùng tiền tố cũng đề xuất Tăng dần), Số ngẫu nhiên min..max (đúng scale), Ngày
  ngẫu nhiên (±30 ngày quanh mẫu), Chọn trong danh sách A|B|C (cờ / mã ngắn / giá trị lặp), Mẫu chuỗi {n}, Giữ nguyên, NULL,
  true / false, UUID, Khoá ngoại.
- **Sinh** (`MockGenerator.Stream`): từng dòng theo 1 `Random(seed)` - seed đổi mỗi lần Đọc mẫu, Xem trước 20 dòng = 20 dòng đầu
  file; CSV và INSERT sinh 2 lượt cùng seed nên khớp nhau, không giữ cả khối trong bộ nhớ (tới 1.000.000 dòng). Lỗi NOT NULL, độ
  dài varchar / char, numeric(p,s), trùng khoá chính / duy nhất → `MockError` báo cột + giá trị, xoá file dở dang.
- **Ghi** (`MockOutput`): CSV (UTF-8 BOM / UTF-8 / Shift-JIS, tiêu đề, NULL = ô trống / chữ NULL, dấu nháy theo RFC 4180), INSERT
  500 dòng / câu, không BEGIN / COMMIT. File `<bảng>_<N>dong_<lúc>.csv|.sql` trong `MockFolder` (mặc định `<thư mục dump>\DuLieuMau`).
  Lựa chọn nhớ trong `settings.json` (`Mock*`).

## Phiên kết nối

`Services\PgAdmin.ListSessionsAsync`: `pg_stat_activity` (chỉ `client backend`), lọc theo database hoặc mọi DB; phiên của
chính DbTools đánh dấu, không ngắt. Ngắt phiên đã chọn (ListView chọn nhiều) / Ngắt hết → hỏi lại →
`pg_terminate_backend` (cần superuser / `pg_signal_backend` / cùng user).

Npgsql chạy ở chế độ raw (`Npgsql.EnableSqlRewriting = false`, đặt trong `App()`): DbTools tự tách câu nên gửi nguyên
văn từng câu, biết chính xác câu nào lỗi.

## Chạy SQL (2026-11-26)

- **Tách câu** (`Core\QuickQuery.Plan`): có chọn đoạn thì chỉ đoạn đó (`Pick`, đếm dòng phía trước để báo lỗi đúng dòng trong ô),
  tách bằng `SqlSplitter` như trang Chạy, lệnh psql `\xxx` bỏ qua (ghi ở Thông báo). Chặn lệnh tự mở / đóng giao dịch
  (`TransactionControl`: BEGIN, START TRANSACTION, COMMIT, END, ROLLBACK, ABORT, SET TRANSACTION, SET SESSION CHARACTERISTICS,
  PREPARE TRANSACTION) - gõ COMMIT giữa chừng sẽ thoát khỏi giao dịch READ ONLY; SAVEPOINT / ROLLBACK TO vẫn cho.
- **Chạy** (`Services\PgQuery.RunAsync`): 1 kết nối (`Pg.OpenAsync`, search_path theo schema hồ sơ), `BEGIN READ ONLY` (mặc định)
  hoặc `BEGIN`, `SET LOCAL statement_timeout` (`AppSettings.QueryTimeoutSeconds`, mặc định 60, 0 = không giới hạn, trang Cài đặt),
  từng câu lần lượt; lỗi / Huỷ (CancellationToken → Npgsql gửi cancel) → ROLLBACK, báo dòng. Chỉ đọc luôn ROLLBACK cuối; cho phép
  ghi thì COMMIT. SELECT / VALUES / TABLE (không có INTO) đọc qua `DECLARE ... NO SCROLL CURSOR` + `FETCH n+1` - chỉ kéo về đủ giới
  hạn; câu khác (WITH, RETURNING, SHOW, EXPLAIN) chạy bằng `NpgsqlBatch` (lấy `StatementType` / `RecordsAffected`), giữ tối đa
  giới hạn dòng nhưng vẫn đếm hết. Không CLOSE cursor khi lỗi (CLOSE trong giao dịch hỏng ra 25P02, che lỗi thật).
- **Giá trị** (`QuickQuery.Format`): theo dạng chữ của PostgreSQL, không theo vùng Windows: date đọc `DateOnly`, timestamptz → giờ
  máy + độ lệch (`+07`), bytea `\x..`, mảng `{a,"b c",NULL}`. Kiểu Npgsql không đọc được → chữ gợi ý thêm `::text`.
- **Bảng** (`Views\QueryPage`): cột động nên không dùng DataGrid - ListView + `ContainerContentChanging` tự dựng TextBlock theo độ
  rộng `QuickQuery.ColumnWidths` (chữ dài nhất trong 200 dòng đầu, 60 - 420 DIP), ScrollViewer ngoài cuộn ngang cả tiêu đề, ListView
  cuộn dọc (ảo hoá). UIA Name của dòng = các ô nối bằng ` | ` (test).
- **Chép** `QuickQuery.ToTsv` (Tab, ô có Tab / xuống dòng / `"` bọc `"..."` như Excel). **Xuất CSV** `PgQuery.ExportCsvAsync`:
  `<thư mục dump>\ChaySql\<hồ sơ>_<lúc>.csv` UTF-8 BOM; bảng còn dòng chưa đọc thì chạy lại câu đó trong `BEGIN READ ONLY`
  (cursor, FETCH 5000 / lần) ghi dần ra file `.tmp` rồi đổi tên; lỗi / huỷ thì xoá file dở. Mở bằng `Shell.OpenData` (CsvEditor).
- **Lịch sử** `Services\QueryHistory` → `Data\History\queries.json`, 200 câu, cùng câu + cùng server thì đưa lên đầu; ô soạn nhớ ở
  `settings.json` (`QueryDraft`, lưu khi rời trang / đóng cửa sổ / chạy), giới hạn dòng `QueryMaxRows`.
- Production: chỉ đọc không hỏi; cho phép ghi → `ProductionGuard.ForQuery` = gõ tên database. Đổi server → công tắc tự tắt.

## Cài đặt (2026-11-23)

`AppSettings.ConnectTimeoutSeconds` (kẹp 5 - 120, file cũ thiếu = 15) → `Pg.ConnectTimeoutSeconds` (đặt trong `AppState` lúc
mở app và ngay khi đổi) → `Timeout` của connection string. Giới hạn thời gian từng thao tác ở các ViewModel dùng `Pg.Within(n)`
= max(n, chờ kết nối + 10) giây - tăng chờ kết nối thì thao tác ngắn (đọc danh sách DB, phiên...) không bị cắt trước khi kịp
kết nối. Thư mục dump, client (`PgBinFolder`, kiểm tra bằng `PgTools.ResolveAsync`), Sao lưu trước khi chạy dùng chung
`AppSettings` với trang Sao lưu / Chạy; các trang này đọc lại khi quay về (`EnsureProfiles` / `EnsureProfile`), trang Cài đặt
đọc lại mỗi lần vào (`SettingsViewModel.Load`).

## Nhật ký server (2026-11-23, đợt 1)

Chỉ đọc, không đổi cấu hình server; không cần quyền vào máy server (thư mục log thường nằm trên máy server, vd Fujitsu
Enterprise Postgres `C:/Users/admin/AppData/Local/Fujitsu/.../log`).
- **Đọc** (`Services\PgServerLog`, qua database bảo trì): `pg_settings` (các tên ở `LogConfig.Names`) + quyền (superuser,
  `pg_monitor`, `pg_read_server_files`); `pg_ls_logdir()` (file, dung lượng, giờ sửa, file đang ghi = `pg_current_logfile()`);
  `pg_read_binary_file(log_directory/tên, offset, độ dài)` đọc N MB cuối (2 / 10 / 50 MB / cả file) rồi giải mã UTF-8 ở máy này
  (file có byte lạ vẫn đọc được).
- **Tách dòng** (`Core\ServerLog.cs`, có unit test): `LogLinePrefix` biến `log_line_prefix` của server thành regex (%t %m %p %l
  %u %d %r %h %a %e %c %x %Q..., canh lề %-10u, phần sau %q là tuỳ chọn) + `LEVEL:  thông báo` (cần `lc_messages` tiếng Anh /
  C). `ServerLogParser.Parse`: dòng bắt đầu bằng Tab = nối tiếp (câu nhiều dòng); DETAIL / HINT / CONTEXT / STATEMENT / QUERY
  cùng pid gắn vào mục trước; `duration: N ms` (log_duration) gắn vào câu gần nhất cùng pid; `statement:` / `execute <tên>:` /
  `duration: N ms  statement:` → câu SQL; đọc giữa file bỏ dòng đầu + dòng nối tiếp mồ côi; dòng không khớp vẫn giữ, đếm
  `UnknownLines`. `KindOf`: từ khoá đầu (bỏ chú thích / ngoặc) → SELECT / INSERT / UPDATE (cả MERGE) / DELETE (cả TRUNCATE) / DDL
  (CREATE / ALTER / DROP / COMMENT / GRANT / REVOKE) / SQL khác; WITH theo INSERT / UPDATE / DELETE bên trong. ERROR / FATAL /
  PANIC / WARNING = Lỗi; `connection ...` / `disconnection:` = Kết nối; còn lại Server. Mật khẩu `PASSWORD '...'` luôn che
  (`MaskPasswords`, cả dòng gốc).
- **Lọc** (`LogFilter`): loại, chữ trong SQL / thông báo / chi tiết, user, database, máy hoặc ứng dụng (chứa, không phân biệt
  hoa thường); mặc định ẩn câu của chính DbTools (`application_name` = `AppSuite DbTools`, `ServerLogParser.OwnApp`) và loại
  Kết nối / Server.
- **Cấu hình bằng lời** (`LogConfig.Describe`): logging_collector, log_statement + log_min_duration_statement, log_line_prefix thiếu
  gì, giữ bao lâu (`log_truncate_on_rotation` + tên file %a = 7 ngày, %d ~ 1 tháng, %H = 24 giờ, còn lại lớn dần - cảnh báo khi > 1
  GB), lc_messages không phải tiếng Anh, csvlog / jsonlog (chưa đọc), thiếu quyền.
- **Xuất** (`ServerLogExport`): các dòng đang hiện → `<thư mục dump>\NhatKyServer\<hồ sơ>_<file>_<lúc>.csv` (UTF-8 BOM, mở bằng
  CsvEditor nếu có - `Shell.OpenData`) hoặc `.txt` (dòng log gốc).
- Giao diện gọn cho cửa sổ ~600 px: nút Cấu hình log cạnh tiêu đề (flyout), nút Loại (flyout ô tích + Ẩn câu của DbTools), trạng
  thái ở chân trang, ô chi tiết chỉ hiện khi chọn dòng.
- **Đợt 2 (2026-11-23) - Theo dõi + dung lượng.** `ServerLogReader` (Core): đọc nối tiếp - `Append(đoạn)` giữ dòng dở cuối đoạn
  tới lần sau, dòng nối tiếp / duration / DETAIL tới sau vẫn ghép đúng mục, trả vị trí mục đầu có thể đã đổi (mục cuối);
  `ServerLogParser.Parse` = Append + Flush (unit test: cắt log ở mọi vị trí ra cùng kết quả). `ServerLogFollower` (Services) giữ
  1 kết nối, mỗi 2 giây `StatAsync` (file đang ghi `pg_current_logfile('stderr')` + dung lượng file đang đọc, `pg_stat_file(...,
  true)`) rồi `ReadAsync` phần mới (tối đa 5 MB / lượt, ghi nhanh hơn thì bỏ phần giữa). File ngắn lại (xoay vòng ghi đè cùng tên)
  → đọc lại từ đầu; server sang file khác → đọc nốt file cũ rồi sang file mới (chọn luôn trong ô File). Vòng theo dõi chạy nền
  (lệnh Theo dõi xong ngay - AsyncRelayCommand đang chạy thì tự tắt nút, nên không await vòng lặp); dừng khi bấm Dừng, chọn
  file khác, đổi server, rời trang (`OnNavigatedFrom`). Bảng là `LogRowCollection` (lọc lại = 1 sự kiện Reset; theo dõi = bỏ dòng
  của mục cuối rồi thêm mục mới); trang tự cuộn xuống cuối nếu đang ở cuối (`RowsAppending` / `RowsAppended`), bắt đầu theo dõi
  hoặc đổi lọc lúc đang theo dõi thì nhảy xuống cuối (`ScrollToEndRequested`). `LogConfig.Volume`: tổng, file lớn nhất, tốc độ
  tăng (file sửa trong 7 ngày / số ngày phủ = khoảng cách + 1), ⚠ khi vượt `AppSettings.LogWarnMb` (mặc định 1024, Cài đặt) hoặc
  server không tự xoá file cũ mà dưới 30 ngày nữa sẽ vượt.
- **Thứ tự (2026-11-23):** bấm tiêu đề cột Giờ đổi cũ trước ↔ mới trước (`AppSettings.LogNewestFirst`, nhớ cho lần sau): lọc lại
  thì đảo danh sách; Theo dõi khi mới trước thì bỏ dòng mục cuối ở đầu bảng rồi chèn mục mới vào vị trí 0, "ở cuối" để tự cuộn
  hiểu là đầu bảng. Xuất CSV theo thứ tự đang hiện, TXT luôn theo thứ tự file. Không chia trang: ListView ảo hoá (chỉ dựng dòng
  đang hiện), chia trang còn làm dòng mới khi Theo dõi rơi sang trang khác.
- **Đổi cấu hình log (2026-11-26, người dùng cho phép ghi lên server):** nút trong flyout Cấu hình log → hộp chọn (`ServerLogPage.ChooseConfigAsync`,
  4 thông số đổi được không cần khởi động lại: log_statement, log_min_duration_statement, log_connections, log_disconnections) →
  `Core\LogConfigEdit` tính câu `ALTER SYSTEM SET` chỉ cho thông số khác giá trị đang áp (giá trị đã kiểm: enum / số / on-off, không
  ghép chữ người dùng gõ) + dòng cũ → mới + lưu ý (ghi mọi câu, đặt riêng theo database / user) → hộp hỏi lại (Production: gõ tên
  database, `ProductionGuard.ForLogConfig`) → `PgServerLog.ApplyAsync`: từng câu (tự commit) + `pg_reload_conf()`, đọc lại bằng kết
  nối mới tới khi thấy giá trị mới (tối đa ~3 giây) rồi Làm mới cấu hình. Về mặc định = `ALTER SYSTEM RESET` các thông số có
  `sourcefile` là postgresql.auto.conf (thông số đặt ở postgresql.conf để nguyên). Lỗi 42501 → báo cần superuser.
- Chưa làm: đọc csvlog /
  jsonlog. Sau này Oracle: giữ `LogEntry` / `LogFilter` / bảng / xuất dùng chung, viết phần đọc riêng (Unified
  Audit `UNIFIED_AUDIT_TRAIL`, alert log `V$DIAG_ALERT_EXT`, `V$SQL`; tránh ASH / AWR vì cần Diagnostics Pack) - lúc đó mới tách
  lớp chung.

## Cây hồ sơ (2026-11-25)

Thư mục là chữ `ConnectionProfile.Folder` của từng hồ sơ ("a/b", rỗng = gốc) - không lưu danh sách thư mục riêng, nên có hồ sơ thì
có thư mục, hết hồ sơ thì mất. `Core\ProfileTree` (thuần, có unit test): chuẩn hoá đường dẫn, dựng cây + lọc, thứ tự phẳng
(`Flatten`), gợi ý thư mục, đổi tên / chuyển / gộp thư mục (`MoveFolder`), theo kiểu chữ hoa thường của thư mục đã có.

- `ConnectionsPage`: TreeView chế độ `TreeViewNode` (Content = `ProfileFolder` / `ConnectionProfile`), mỗi thay đổi dựng lại từ dữ
  liệu (`QueueRebuild`, sau khi xong sự kiện). Kéo-thả để TreeView tự làm rồi đọc `NewParentItem`; cây cao vừa nội dung, phần trống
  bên dưới nhận thả riêng = ra gốc (TreeView không cho nghe `DragOver`, tự nó cho vào thư mục của dòng cuối). Ô Thư mục chỉ áp khi
  rời ô / Enter / chọn gợi ý. Thư mục thu gọn nhớ ở `AppSettings.CollapsedFolders`.
- **Mã hồ sơ** `ConnectionProfile.Id` (GUID, `ConnectionStore` cấp; file cũ chưa có thì cấp và ghi lại ngay, giữ nguyên mật khẩu đã
  mã hoá): "hồ sơ lần trước" nhớ theo `AppSettings.LastProfileId` vì 2 thư mục có thể có hồ sơ cùng tên - nhớ theo tên thì mở lại
  có thể chọn nhầm DB. `LastProfile` (tên) chỉ còn để đọc cài đặt cũ.
- **Ô chọn server** ở các trang: `views:ProfilePicker.Selected` (attached property) thay ItemsSource + SelectedItem, xếp lại theo cây
  mỗi lần ô hiện ra mà không làm trang mất hồ sơ đang chọn. Chữ: `tên ‹thư mục› (host:port/database)` - tên đứng trước vì ô hẹp cắt đuôi.

## Ô gõ được kèm danh sách (Database, Bảng, Schema)

`ComboBox IsEditable` của WinUI có 2 bẫy (2026-11-25), ô mới cùng kiểu phải làm theo:

- Xoá danh sách (`Clear`, kể cả đang rỗng) làm ô hiện trống dù `Text` còn nguyên, đặt lại đúng chữ cũ thì không vẽ lại → đổi danh
  sách bằng `ViewModels\EditableBox.Reset` (bỏ chữ → xoá → đặt chữ). UIA vẫn đọc ra giá trị: test phải đo điểm ảnh.
- Mở danh sách rồi đóng mà không chọn thì ô trả về mục đã chốt lần trước, bỏ qua chữ đặt từ code (có thể là DB của server cũ) →
  gọi `Ui.KeepTextIfNothingPicked` trong constructor của trang.

## Hồ sơ Production (2026-11-20)

`ConnectionProfile.IsProduction` (lưu trong connections.json, file cũ thiếu trường = hồ sơ thường; Nhân bản giữ). Tô đỏ: vạch +
nhãn PRODUCTION ở cây hồ sơ trang Kết nối (`Ui.ProductionBrush`), ô chọn server ở các trang bind `PickerText` ("🔴 PRODUCTION · …").
Mọi hộp hỏi lại (Chạy, Restore, Copy, Ngắt phiên) dùng chung `Views\Dialogs.ConfirmAsync` (cuối `BackupPage.xaml.cs`);
`RunConfirmation.TypeToConfirm` khác null → khung đỏ + ô gõ lại (`ProductionConfirmBox`), nút chính tắt tới khi gõ đúng
(`ProductionGuard.Matches`: bỏ khoảng trắng 2 đầu, phân biệt hoa thường), nút mặc định vẫn là Huỷ. Chữ phải gõ do
`Core\ProductionGuard` quyết định:

| Thao tác | Gõ lại |
|---|---|
| Chạy (thật; Chạy thử không hỏi) | database của hồ sơ |
| Restore (DB mới hoặc có sẵn) | database đích |
| Copy cùng server, phải ngắt phiên DB nguồn Production | database nguồn |
| Copy lên server đích Production | database đích (chỉ đọc nguồn Production bằng pg_dump thì không hỏi) |
| Ngắt phiên | database của các phiên; nhiều DB → tên hồ sơ |

Tên trống (hồ sơ chưa điền Database) → gõ tên hồ sơ; cả 2 trống → gõ `PRODUCTION`.

## Dữ liệu (cạnh exe, `DBTOOLS_DATA_DIR` đổi chỗ khác khi test)

| File | Nội dung |
|---|---|
| `Data\Config\connections.json` | Hồ sơ kết nối (kèm thư mục, mã); mật khẩu mã hoá DPAPI (CurrentUser) - sang máy / tài khoản khác thì trống |
| `Data\Config\settings.json` | Thư mục / hồ sơ lần trước, thứ tự chạy + file đã bỏ theo thư mục, thư mục lưu dump (`DumpFolder`), thư mục bin PostgreSQL (`PgBinFolder`, trống = tự động), `BackupBeforeRun`, Chạy SQL (`QueryMaxRows`, `QueryTimeoutSeconds`, `QueryDraft`), thư mục thu gọn trong cây hồ sơ (`CollapsedFolders`) |
| `<thư mục dump>\*.dump`, `*.log` | File Dump + log (mặc định `D:\DMP`, không nằm trong Data) |
| `<thư mục dump>\TruocKhiChay\...` | Sao lưu trước khi chạy, mỗi lần 1 thư mục (không tự xoá) |
| `Data\History\history.json` | Lịch sử chạy (500 lần gần nhất) |
| `Data\History\queries.json` | Lịch sử câu của trang Chạy SQL (200 câu gần nhất) |
| `<thư mục dump>\ChaySql\*.csv` | File Xuất CSV của trang Chạy SQL |
| `Data\History\Logs\run-*.log` | Log từng lần chạy: thứ tự file + kết quả + toàn bộ nhật ký |
| `Logs\dbtools-*.log` | Log của app (14 ngày) |

Bản svn / git của thư mục: `svn info --show-item revision` + `svn status -q` (hoặc `git rev-parse` / `git status`) nếu
máy có svn.exe / git.exe.

## Unit test

`dotnet test Tests\DbTools.Tests` (hoặc `dotnet run --project Tests\DbTools.Tests`): tách câu, nhận loại lệnh, sắp thứ tự
(thư mục mẫu cùng kiểu add-on mcframe: chạy theo vần ABC thì sai), script xoá ngược, đối chiếu với DB, encoding, hồ sơ
kết nối (DPAPI), cài đặt, lịch sử, Hướng dẫn đủ mục cho mọi trang; đọc phiên bản / chọn bộ client, tham số pg_dump /
pg_restore / psql (không có mật khẩu, tên trong "..."), đọc `pg_restore -l`, tên file dump, kế hoạch Sao lưu trước khi chạy
(`BackupTests`).

`PgBackupIntegrationTests` (cần `DBTOOLS_TEST_PG` + pg_dump.exe trên máy): dump custom → restore vào DB mới, dump SQL chỉ
cấu trúc → psql, restore 1 transaction vào DB có sẵn bị trùng → rollback, `--clean` → được, copy TEMPLATE bị chặn khi còn
phiên → ngắt phiên → copy được, sao lưu trước khi chạy (đủ file, khôi phục lại bảng + hàm từ bản sao lưu), bỏ qua khi chỉ
tạo đối tượng mới.

`LogConfigEditTests` / `LogConfigIntegrationTests` (Đổi cấu hình log): chỉ ALTER các thông số đổi, mô tả cũ → mới, lưu ý, RESET chỉ
dòng của postgresql.auto.conf; trên PostgreSQL thật (superuser): đổi 4 thông số → reload → đọc lại đúng → RESET về như cũ.

`QuickQueryTests` / `PgQueryIntegrationTests` (trang Chạy SQL): chọn đoạn + đếm dòng, chặn lệnh giao dịch, câu nào đọc qua cursor,
dạng giá trị, TSV, lịch sử; trên PostgreSQL thật: giới hạn dòng qua cursor, chỉ đọc từ chối DELETE (25006) DB không đổi, ghi lỗi
giữa chừng rollback hết, RETURNING, NOTICE, hết giờ, Huỷ, Xuất CSV đọc lại đủ dòng / câu ghi không chạy lại được.

`PgIntegrationTests` chạy thật trên PostgreSQL khi có biến môi trường `DBTOOLS_TEST_PG` (vd
`Host=localhost;Port=55432;Username=postgres;Password=...`); mỗi test tạo database tạm `dbtools_test_*` rồi xoá. Server thử
nhanh: `initdb -D <thư mục tạm> -U postgres --pwfile=<file> -A scram-sha-256 -E UTF8` rồi
`pg_ctl -D <thư mục tạm> -o "-p 55432" start`.

## Chưa làm (đợt sau)

Chạy cùng lô lên nhiều DB,
tìm trong mã nguồn hàm / view, so DDL với svn, Oracle (xem `PLAN.md`).
