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
| **Phiên kết nối** | Ai đang kết nối vào DB nào (pg_stat_activity), ngắt phiên đã chọn / ngắt hết |
| **Lịch sử chạy** | Mọi lần Chạy / Chạy thử / Dump / Restore / Copy DB: lúc nào, database nào, thư mục / file nào + bản svn / git, kết quả, file log |
| **Kết nối** | Hồ sơ kết nối PostgreSQL (host, port, database, user, mật khẩu), Thử kết nối |

F1 / nút ? / mục Hướng dẫn: cửa sổ hướng dẫn dùng chung `SharedUI.Help.HelpWindow`, nội dung ở `Core\HelpContent.cs`.

## Chạy thư mục SQL

1. **Đọc thư mục**: mọi `*.sql` (bỏ `.svn`, `.git`), mỗi file được tách thành từng câu (`Core\SqlSplitter`: hiểu chuỗi
   `'...'`, `E'...'`, tên `"..."`, thân hàm `$tag$...$tag$`, chú thích `--` / `/* */` lồng nhau; `$` dính sau tên như
   `msta_x_$s` là 1 phần của tên) rồi nhận loại lệnh (`Core\SqlAnalyzer`: CREATE TABLE / VIEW / FUNCTION / TRIGGER / INDEX,
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
   CREATE INDEX CONCURRENTLY...) → lỗi; DELETE / UPDATE / TRUNCATE / DROP → cảnh báo; file không phải UTF-8 → đọc theo
   Shift-JIS + cảnh báo.
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

## Phiên kết nối

`Services\PgAdmin.ListSessionsAsync`: `pg_stat_activity` (chỉ `client backend`), lọc theo database hoặc mọi DB; phiên của
chính DbTools đánh dấu, không ngắt. Ngắt phiên đã chọn (ListView chọn nhiều) / Ngắt hết → hỏi lại →
`pg_terminate_backend` (cần superuser / `pg_signal_backend` / cùng user).

Npgsql chạy ở chế độ raw (`Npgsql.EnableSqlRewriting = false`, đặt trong `App()`): DbTools tự tách câu nên gửi nguyên
văn từng câu, biết chính xác câu nào lỗi.

## Dữ liệu (cạnh exe, `DBTOOLS_DATA_DIR` đổi chỗ khác khi test)

| File | Nội dung |
|---|---|
| `Data\Config\connections.json` | Hồ sơ kết nối; mật khẩu mã hoá DPAPI (CurrentUser) - sang máy / tài khoản khác thì trống |
| `Data\Config\settings.json` | Thư mục / hồ sơ lần trước, thứ tự chạy + file đã bỏ theo thư mục, thư mục lưu dump (`DumpFolder`), thư mục bin PostgreSQL (`PgBinFolder`, trống = tự động), `BackupBeforeRun` |
| `<thư mục dump>\*.dump`, `*.log` | File Dump + log (mặc định `D:\DMP`, không nằm trong Data) |
| `<thư mục dump>\TruocKhiChay\...` | Sao lưu trước khi chạy, mỗi lần 1 thư mục (không tự xoá) |
| `Data\History\history.json` | Lịch sử chạy (500 lần gần nhất) |
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

`PgIntegrationTests` chạy thật trên PostgreSQL khi có biến môi trường `DBTOOLS_TEST_PG` (vd
`Host=localhost;Port=55432;Username=postgres;Password=...`); mỗi test tạo database tạm `dbtools_test_*` rồi xoá. Server thử
nhanh: `initdb -D <thư mục tạm> -U postgres --pwfile=<file> -A scram-sha-256 -E UTF8` rồi
`pg_ctl -D <thư mục tạm> -o "-p 55432" start`.

## Chưa làm (đợt sau)

Chạy cùng lô lên nhiều DB, ô chạy SQL nhanh,
tìm trong mã nguồn hàm / view, so DDL với svn, Oracle (xem `PLAN.md`).
