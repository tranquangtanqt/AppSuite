# MeRon — kế hoạch thiết kế

## Bối cảnh

Người dùng có app Android **me-ron** (Flutter, `D:\tantq\src\me-ron`) quản lý bán hàng nhỏ: khách hàng, món ăn, đặt
hàng, nhập hàng, báo cáo; dữ liệu SQLite trên điện thoại, xuất backup `.tsv` (8 bảng) vào `Download/MeRon/<thời gian>`
và tải lên 1 folder Google Drive. Cần **bản máy tính có cùng chức năng**, cũng dùng SQLite, nạp được dữ liệu mới nhất
từ backup — trước mắt từ thư mục trên máy, sau này thẳng từ link Google Drive.

Quyết định với người dùng (2026-10-12):

- **Hướng B — module mới trong AppSuite** (WinUI 3, C#), không phải build app Flutter cho Windows.
- **Import = thay toàn bộ** dữ liệu (không gộp).
- Đọc **TSV** đúng định dạng điện thoại xuất (không đổi sang CSV: tên món / địa chỉ hay có dấu phẩy).
- Không làm: giỏ hàng POS + Giao dịch, in hoá đơn, giọng nói, Firestore, lịch tự sao lưu, ảnh món ăn.
- Làm **4 đợt**, mỗi đợt người dùng thử trong Sandbox rồi mới sang đợt sau.

| Đợt | Nội dung |
|---|---|
| 1 | Khung module + database + Import TSV + Địa chỉ, Danh mục món, Khách hàng, Món ăn |
| 2 | Đặt hàng (đầy đủ như điện thoại) |
| 3 | Mua hàng + 4 báo cáo |
| 4 | Xuất TSV, Xoá toàn bộ, hướng dẫn, README / PLAN, test |

## Thiết kế

### Schema giống hệt điện thoại

Cùng tên bảng, tên cột, kiểu (`database_config.dart` version 2). Lý do: file TSV nạp 1–1 không cần ánh xạ; sau này
xuất ngược lại cho điện thoại cũng không cần đổi; đọc code / tài liệu điện thoại (`DATABASE.md`, `USER_GUIDE.md`) áp
thẳng sang được. Bổ sung không ảnh hưởng dữ liệu: bảng `MeRonMeta` (key/value) và index cho các truy vấn lọc / báo cáo.

Ngày giờ lưu **nguyên chuỗi gốc** (TEXT). Điện thoại ghi lẫn 2 dạng (`…T14:20:53.242992` từ `toIso8601String()` và
`… 07:20:53` từ `CURRENT_TIMESTAMP`); chuẩn hoá lúc nhập sẽ làm bản máy tính khác điện thoại và mất thông tin, trong khi
`date()` của SQLite hiểu cả hai. Bản ghi tạo trên máy tính dùng đúng dạng `toIso8601String()`.

Database ở `%LOCALAPPDATA%\AppSuite\MeRon\` (không cạnh exe như log): cập nhật / publish lại app không đụng dữ liệu.

### Import: kiểm tra trước, ghi sau, luôn lấy lại được

- `IBackupSource.GetLatestAsync()` → `BackupFileSet` (8 file trên máy). Phần nạp chỉ biết `BackupFileSet`, nên thêm
  nguồn Google Drive = 1 class tải 8 file về thư mục tạm, không sửa phần nạp. `LocalFolderBackupSource` nhận cả thư mục
  1 lần backup lẫn thư mục cha (chọn thư mục con mới nhất dạng `yyyyMMdd_HHmmss`, bỏ `…_bk`) — cùng logic sẽ dùng cho
  folder Drive (folder đó chính là thư mục cha).
- `BackupImporter.Prepare` đọc + kiểm tra **cả 8 file** trước khi ghi: thiếu file, thiếu cột khoá, lệch số ô, số sai,
  trùng khoá → lỗi (dừng ở 20 lỗi / file); cột lạ / thiếu cột không phải khoá → cảnh báo; liên kết hỏng giữa các bảng
  (đơn trỏ tới khách không có...) → cảnh báo, vẫn nạp nguyên văn vì điện thoại không bật khoá ngoại và bản máy tính phải
  phản ánh đúng dữ liệu điện thoại.
- `Apply`: chép database ra `Backups\` (SQLite Online Backup API, an toàn kể cả khi đang mở; giữ 10 bản), rồi trong
  **1 transaction**: xoá 8 bảng, đặt lại `sqlite_sequence`, chèn với **id gốc**. Lỗi giữa chừng = rollback.
- Đã cân nhắc: nạp từng bảng riêng như màn Import của điện thoại — bỏ, vì nạp lệch bộ (đơn mới + khách cũ) dễ làm sai
  báo cáo; người dùng chọn "thay toàn bộ".

Hệ quả cần nói rõ với người dùng (đã nói, có trong hộp thoại xác nhận và Hướng dẫn): sửa trên máy tính sẽ mất ở lần nhập
sau — điện thoại là nơi nhập liệu chính.

### Quy tắc nghiệp vụ

Theo điện thoại: trùng tên khách hàng (so `toLowerCase`, vẫn phân biệt dấu) không cho lưu, sửa giữ tên cũ vẫn lưu được;
món ăn không có Xoá; danh mục món có Sửa / Xoá ngay trên dòng; mã địa chỉ không đổi khi sửa.

Thêm ở máy tính (vì điện thoại không bật khoá ngoại, xoá sẽ để lại dữ liệu mồ côi làm sai báo cáo): không xoá khách còn
đơn hàng, không xoá danh mục còn món ăn. Tìm kiếm không phân biệt dấu (điện thoại dùng `LIKE`, chỉ bỏ qua hoa thường ASCII).

### Giao diện

- `NavigationView` trái như FileTools; lần đầu (database trống) mở thẳng trang Nhập dữ liệu.
- Thay cho các màn hình form riêng của điện thoại: **hộp thoại** Lưu / Xoá / Huỷ (`Views\Dialogs`), lỗi hiện ngay trong
  hộp thoại và hộp thoại không đóng — giống "báo lỗi và không cho lưu" của điện thoại, hợp desktop hơn chuyển trang.
- Danh sách dùng `ListView` + lưới cột (vài trăm dòng, tải 1 lần, lọc ngay khi gõ) thay vì DataGrid của CommunityToolkit
  (tránh kéo metapackage WinAppSDK cũ, xem CsvEditor).
- Số / tiền luôn định dạng `vi-VN` (`VnFormat`) — Sandbox chạy Windows tiếng Nhật đã lộ lỗi `2,617` khi để theo
  ngôn ngữ máy.

## Kiểm chứng (Đợt 1)

- [x] Unit test `Tests\MeRon.Tests` 19/19 (+1 test backup thật chạy khi đặt `MERON_REAL_BACKUP`) — 2026-10-12
- [x] Bộ backup thật mới nhất (268 khách, 2.617 đơn, 4.394 dòng món): nạp không lỗi, không cảnh báo, 0,18 s
- [x] Build module 0 warning; publish Release không có `onnxruntime.dll` / `DirectML.dll`
- [x] GUI trong Windows Sandbox (bản Release, UI Automation, `meron_gui.ps1`) 36/36: bộ lỗi (thiếu file + số sai, nút
  Nhập tắt), thư mục cha → bản mới nhất bỏ `_bk`, xem trước, xác nhận, nhập; 4 trang đếm đúng, tìm không dấu, trùng tên,
  thêm khách / món, không xoá danh mục còn món, F1; mở lại app giữ dữ liệu + nhớ thư mục; nhập lại có bản sao an toàn
- [ ] Người dùng thử tay Đợt 1 (Sandbox / máy thật, chạy từ MainLauncher)
