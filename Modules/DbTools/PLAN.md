# DbTools - kế hoạch

## Bối cảnh

Người dùng có các thư mục script `.sql` (vd add-on mcframe 7 trên PostgreSQL: `SEQ`, `OBJECT`, `HST_TAB`, `CO_VT`,
`HST_TR`, `U_TR`, `CHECK`, `COMMENT`) và các file bat dump / restore / chạy script (`batch\postgres\*.bat`,
`runFolderScrip.bat` dùng `FOR /R ... psql -f`). Vấn đề của cách bat:

- Chạy theo vần ABC: `CHECK` / `COMMENT` chạy trước khi có bảng, view `MSTA_X.sql` chạy trước bảng `MSTA_X_ALL.sql` → lỗi.
- Mỗi file 1 lần `psql` riêng: lỗi giữa chừng thì DB đã áp một nửa.
- Mật khẩu ghi rõ trong bat (`PGPASSWORD=...`), không lịch sử "DB nào đã áp bản nào".

Đợt 1 (2026-11-18, người dùng chọn): module riêng, PostgreSQL, **lỗi là rollback hết**, gồm Kết nối + Chạy thư mục SQL +
Kiểm tra trước khi chạy + Script xoá ngược + Lịch sử chạy.

## Thiết kế

- **Npgsql thay vì gọi psql.exe**: không phụ thuộc máy có cài PostgreSQL client đúng bản; 1 kết nối + 1 transaction cho
  cả lô; biết chính xác câu nào lỗi. Đánh đổi: lệnh riêng của psql (`\i`, `\set`...) không chạy được → báo lỗi ở bước
  đọc; chỉ `\encoding` được hiểu (các file mcframe đều bắt đầu bằng `\encoding UTF-8`).
- **Tự tách câu** (`SqlSplitter`) thay vì để Npgsql tách: để biết dòng lỗi trong file và để phân tích. Npgsql chạy raw
  mode (`Npgsql.EnableSqlRewriting=false`) nên gửi nguyên văn - không đụng `$` trong tên (`msta_x_$s`).
- **Nhận loại lệnh bằng vài từ đầu** (`SqlAnalyzer`), không parser SQL đầy đủ: đủ cho sắp thứ tự + script xoá + cảnh báo.
  Tên được nhắc tới = mọi token tên trong câu (kể cả thân hàm), không tính chuỗi '...'.
- **Thứ tự = phụ thuộc + loại** (`RunOrder`): Kahn, trong các file sẵn sàng chọn loại nhỏ nhất rồi đường dẫn. Quy tắc
  "DELETE bảng X trước INSERT vào X" thêm vì thư mục thật có `deleteHistoryTab_*.sql` (xoá dòng đăng ký) phải chạy trước
  `*_R.sql` (thêm lại dòng đó) - theo loại thuần thì DELETE (dữ liệu) bị xếp sau. Thứ tự tự động chỉ là gợi ý: người dùng
  chỉnh tay được, lưu theo thư mục.
- **1 transaction cho cả lô**: PostgreSQL rollback được DDL. File có BEGIN / COMMIT hoặc lệnh không chạy được trong
  transaction bị chặn từ bước đọc. Chạy thử = cùng đường chạy, luôn ROLLBACK.
- **Script xoá ngược không CASCADE**: an toàn hơn - đối tượng ngoài thư mục còn dùng thì DROP lỗi và cả lô rollback,
  thay vì xoá lan. Trigger xoá riêng (bài học từ test thật: hàm trigger tạo trước trigger nên bị DROP trước bảng → lỗi
  "other objects depend on it"); `DROP TRIGGER IF EXISTS ... ON <bảng chưa có>` chỉ báo NOTICE.
- **Mật khẩu DPAPI** (CurrentUser) trong `Data\Config\connections.json`, không ghi chữ rõ.
- Không DI container (như FileTools): `AppState` tĩnh giữ hồ sơ kết nối + cài đặt dùng chung giữa các trang.

## Kiểm chứng

- Unit test `Tests\DbTools.Tests`: tách câu, nhận loại, sắp thứ tự trên thư mục mẫu cùng kiểu mcframe, script xoá,
  đối chiếu DB, encoding, DPAPI, lịch sử.
- `PgIntegrationTests` trên PostgreSQL 17 tạm (initdb trong thư mục tạm, cổng 55432): chạy thử không đổi DB, thứ tự ABC
  lỗi + rollback + đúng dòng, chạy + chạy lại cần "xoá rồi tạo lại", xoá rồi tạo trên DB trống, Huỷ = rollback, sai mật khẩu.
- Thử với thư mục thật (27 file, đối tượng nền mcframe giả lập): thứ tự đúng, chạy thử / chạy / chạy lại xoá-rồi-tạo đều đạt.
- GUI trong Windows Sandbox (UIA): 3 trang, đọc thư mục, thứ tự, kiểm tra / chạy thử / chạy, hỏi lại, lịch sử, cửa sổ hẹp.

## Đợt 2 (2026-11-19, người dùng chọn "làm hết theo đề xuất", thư mục lưu D:\DMP đổi được, sao lưu trước khi chạy bật sẵn)

Thay các bat `backup*.bat` / `restore*.bat` (mật khẩu ghi rõ, đường dẫn PostgreSQL 12 / 17 cố định, không biết lỗi ở đâu).

- **Gọi pg_dump / pg_restore / psql của máy**, không tự viết dump: định dạng custom chuẩn, dùng lẫn được với bat / pgAdmin.
  Tự chọn bản mới nhất (bản mới đọc được server / file cũ; pg_dump cũ hơn server bị từ chối → báo trước). Mật khẩu qua
  `PGPASSWORD` của tiến trình con, `-w` để không bao giờ treo chờ gõ mật khẩu.
- **Restore an toàn mặc định**: 1 transaction + dừng ở lỗi đầu; tạo DB mới từ template0 hoặc ghi vào DB có sẵn (`--clean`
  phải tự bật). Không làm DROP DATABASE.
- **Copy DB**: cùng server dùng TEMPLATE (nhanh, nhưng cần không còn phiên → hỏi trước khi ngắt, không tự ngắt khi chưa
  được đồng ý); khác server dump ra file tạm rồi restore.
- **Phiên kết nối**: trang riêng để xem / ngắt (pg_stat_activity / pg_terminate_backend).
- **Sao lưu trước khi chạy**: không dump cả DB (có thể rất lớn) mà chỉ đối tượng đang có sẽ bị đổi; bảng nền chỉ bị thêm
  trigger / index thì lấy cấu trúc, bảng bị đổi dữ liệu / bị xoá thì lấy cả dữ liệu; hàm / trigger lấy định nghĩa hiện tại
  ra .sql chạy lại được. Sao lưu lỗi thì không chạy.

## Đợt sau (đã đề xuất)

Chạy cùng lô lên nhiều DB; ô chạy
SQL nhanh + mở kết quả bằng CsvEditor; tìm trong mã nguồn hàm / view; so DDL trên DB với file svn; Oracle (tạo user /
tablespace + quyền, impdp / expdp có REMAP_SCHEMA).
