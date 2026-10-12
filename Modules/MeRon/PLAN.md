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

## Đợt 3 — Mua hàng + 4 báo cáo (2026-10-12)

Port theo `purchase_local_datasource_impl.dart`, `purchase_form_*`, `report_*_notifier.dart`, `report_*_screen.dart`.

- **Báo cáo = truy vấn tổng hợp SQL** (`ReportRepository`) thay vì tải hết đơn rồi cộng như báo cáo Tổng hợp của điện thoại:
  4 báo cáo trên 2.617 đơn thật chạy 36 ms; test backup thật đối chiếu tổng báo cáo với tự cộng từng đơn.
- **Đơn Huỷ không tính** vào tổng tiền / số đơn / số món / xếp hạng ở mọi báo cáo — đúng hướng dẫn của điện thoại. Điện
  thoại lệch ở 2 chỗ (đếm cả đơn huỷ vào "Tổng số đơn"; báo cáo Tổng hợp tính cả đơn huỷ) → sửa có chủ đích. Ô "Huỷ" vẫn
  hiện riêng số đơn / tiền huỷ để biết.
- Món gộp theo productId với **tên món hiện tại** (điện thoại lấy MIN(snapshotName) - đổi tên món là tên cũ / mới lẫn
  lộn); món tự do gộp theo tên. Khoản mua hàng gộp theo tên, bỏ khoảng trắng, không phân biệt hoa thường.
- Báo cáo Tổng hợp thêm **Chênh lệch (bán − mua)** — 2 con số đã có sẵn trên trang, người dùng hay tự trừ.
- `DateRangeFilter` + `DateRangeBar`: 1 khoảng ngày dùng chung (mặc định hôm nay như điện thoại, chọn nhanh), báo 1 lần
  khi đổi. Drill-down từ báo cáo dùng lại màn Chi tiết của Đợt 2 (`OrderDetailArgs(filter)`), không đụng bộ lọc trang Đặt hàng.
- **Lỗi tìm ra khi test GUI**: SelectorBar trong trang `NavigationCacheMode=Enabled` tự áp lại tab cũ khi trang gắn lại vào
  cửa sổ (bộ lọc đã về "Đã lên đơn" nhưng tab hiện "Tất cả" và còn ghi đè ngược bộ lọc). Sửa: 2 trang có SelectorBar
  (Đặt hàng, Theo đơn hàng) không cache, giữ bộ lọc trong 1 ViewModel tĩnh của phiên (Back giữ, New từ menu reset);
  trang không có SelectorBar vẫn cache. Thông báo lỗi form tự tắt khi người dùng sửa.

## Kiểm chứng (Đợt 3)

- [x] Unit test 38/38 (+ backup thật: 4 báo cáo khớp tự cộng, 36 ms)
- [x] GUI Sandbox `meron_reports.ps1` 32/32 trên dữ liệu thật: phiếu nhập (lỗi thiếu tên, lỗi tự tắt, 2 khoản = 165.000,
  sửa còn 120.000); Theo đơn hàng (2.616 đơn / 140.191.000 đ, ô 177 / 1, 18.422 món, lọc khách 38 / 1.785.000, Chi tiết
  khách, quay lại giữ bộ lọc, sắp xếp); Theo món ăn (18.422, lọc 1 món); Tổng hợp (mọi ngày + chênh lệch 140.071.000,
  tháng 9: 670 / 33.497.000); Khách hàng tiềm năng (hạng 1 anh Công 4.516.500, 100 khách, Chi tiết); vào lại từ menu reset.
  Đợt 1 37/37, Đợt 2 36/36 chạy lại đạt
- [ ] Người dùng thử tay Đợt 3

## Đợt 2 — Đặt hàng (2026-10-12)

Port theo `order_local_datasource_impl.dart`, `order_form_notifier.dart`, `order_screen.dart`, `order_detail_screen.dart`.

- **Lõi tách khỏi UI**: `OrderDraft` + `OrderCalculator` (thuần, có unit test) giữ đúng quy tắc tính tiền của điện thoại:
  dòng khoá khi món còn nguyên và giá hiện tại ≠ đơn giá lúc đặt (dùng đơn giá cũ); món tự do lấy giá tự nhập; Tổng =
  max(0, Tạm tính − Giảm giá); thông báo lỗi món tự do giữ nguyên văn và chỉ ra dòng / ô để đưa con trỏ tới. Điện thoại
  áp giá cũ theo *productId* (dòng mới cùng món cũng bị tính giá cũ) — ở đây theo từng dòng.
- **Sửa lỗi điện thoại có chủ đích**: lọc theo ngày (`substr(deliveryDatetime,1,10)`) thay vì so chuỗi ngày giờ (bỏ sót
  dạng có dấu cách); sắp theo `julianday` (2 dạng chuỗi không so trực tiếp được); xoá đơn xoá dòng món; Thanh toán toàn
  bộ không chuyển đơn Huỷ; lưu đơn đã Huỷ giữ Huỷ (điện thoại âm thầm bỏ huỷ).
- **Không phân trang**: 2.617 đơn thật + 4.394 dòng món tải trong 20 ms → bỏ "Xem thêm"; danh sách dùng `ItemsRepeater`
  (ảo hoá) với **mỗi thẻ là 1 Button** — chuột, bàn phím, trình đọc màn hình và UI Automation đều mở được (ListView
  ItemClick không có Invoke, Sandbox chặn chuột giả).
- **Điều hướng**: form đơn và Chi tiết là trang trong Frame (form nhiều dòng không hợp ContentDialog). Danh sách
  `NavigationCacheMode=Enabled`: Back giữ bộ lọc, New (từ menu) reset — đúng hành vi điện thoại. Menu xoá back stack;
  bấm lại mục đang chọn khi ở trang con thì về trang gốc.
- **Thêm cho desktop**: khoảng ngày nhanh (`DateRanges`), đổi ngày tìm ngay, Chụp ảnh = `RenderTargetBitmap` cả vùng
  nội dung → clipboard + PNG (Windows giới hạn 4096 px: danh sách rất dài bị thu nhỏ, vẫn đọc được — 38 đơn ≈ 85%).
- Không làm: thêm món bằng giọng nói (đã thống nhất).

## Kiểm chứng (Đợt 2)

- [x] Unit test 32/32 (+ test backup thật: tải toàn bộ đơn khớp số dòng, mọi ngày giao đọc được, 20 ms)
- [x] GUI Sandbox `meron_orders.ps1` 36/36 trên dữ liệu thật: số đơn theo trạng thái (2.617 / 177 / 2.439 / 1), lọc khách
  (38), sắp xếp, dòng khoá (đơn 1), Chi tiết + từ chối thanh toán, Thanh toán toàn bộ, Chụp ảnh (PNG + clipboard, cả phần
  cuộn), thêm đơn món thường + tự do + giảm giá, lỗi món tự do, cảnh báo trùng đơn (Huỷ / Vẫn lưu), huỷ, xoá, reset bộ lọc
  từ menu. Đợt 1 chạy lại 37/37. Sửa nhờ ảnh chụp: thông báo lỗi form khuất cuối trang → chuyển cạnh nút Lưu; nút Khoảng
  ngày bị cắt khi cửa sổ hẹp; vùng Chi tiết rỗng hiện dải xám
- [ ] Người dùng thử tay Đợt 2

## Kiểm chứng (Đợt 1)

- [x] Unit test `Tests\MeRon.Tests` 19/19 (+1 test backup thật chạy khi đặt `MERON_REAL_BACKUP`) — 2026-10-12
- [x] Bộ backup thật mới nhất (268 khách, 2.617 đơn, 4.394 dòng món): nạp không lỗi, không cảnh báo, 0,18 s
- [x] Build module 0 warning; publish Release không có `onnxruntime.dll` / `DirectML.dll`
- [x] GUI trong Windows Sandbox (bản Release, UI Automation, `meron_gui.ps1`) 36/36: bộ lỗi (thiếu file + số sai, nút
  Nhập tắt), thư mục cha → bản mới nhất bỏ `_bk`, xem trước, xác nhận, nhập; 4 trang đếm đúng, tìm không dấu, trùng tên,
  thêm khách / món, không xoá danh mục còn món, F1; mở lại app giữ dữ liệu + nhớ thư mục; nhập lại có bản sao an toàn
- [ ] Người dùng thử tay Đợt 1 (Sandbox / máy thật, chạy từ MainLauncher)
