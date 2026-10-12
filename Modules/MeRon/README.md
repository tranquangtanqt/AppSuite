# MeRon

Bản máy tính của app điện thoại **me-ron** (Flutter, `D:\tantq\src\me-ron`): quản lý bán hàng — đặt hàng, mua hàng,
thống kê, khách hàng, món ăn — trên SQLite riêng của máy tính. Dữ liệu trao đổi với điện thoại qua **bộ backup `.tsv`**
(8 file / lần): nhập bộ điện thoại xuất ra (mỗi lần nhập là **thay toàn bộ**), và xuất ngược lại đúng định dạng đó.

Thiết kế và lý do các quyết định: [PLAN.md](PLAN.md).

## Chạy

- Độc lập: mở `Modules\MeRon\MeRon.csproj`, đặt làm Startup Project, F5 — hoặc `dotnet run --project Modules\MeRon\MeRon.csproj`.
- Từ MainLauncher: mục **MeRon** trong Module List (sau `.\build\Sync-Modules-Dev.ps1` khi dev).
- Module chỉ tham chiếu `Common` + `SharedUI`, không bao giờ tham chiếu MainLauncher; không đọc `modules.json`.

## Quy trình dùng

1. Điện thoại: Cài đặt → Sao lưu → **Xuất file sao lưu** (lưu `Download/MeRon/<thời gian>` + tải lên Google Drive).
2. Chép thư mục đó (hoặc tải folder từ Google Drive) về máy → MeRon **Nhập dữ liệu** → chọn thư mục (hoặc thư mục cha).
3. Xem / lọc đơn, báo cáo; sửa nhỏ trên máy tính nếu cần.
4. Muốn đưa phần sửa trên máy tính về điện thoại: **Xuất / Xoá dữ liệu** → Xuất TSV → chép sang điện thoại → Cài đặt →
   Sao lưu → Nhập dữ liệu từ tệp sao lưu (từng bảng).

Lần đầu mở (database trống) app vào thẳng trang **Nhập dữ liệu**; có dữ liệu rồi thì vào **Đặt hàng**.

## Chức năng

| Menu | Trang | Làm gì |
|---|---|---|
| Bán hàng | **Đặt hàng** | Lọc trạng thái (Tất cả / Đã lên đơn / Đã thanh toán / Huỷ), khách (gõ tên / SĐT, không dấu), khoảng ngày giao; sắp xếp theo thành tiền / ngày giao; "N đơn · Tổng X". Thẻ đơn: khách, trạng thái, ngày giao, các món, ghi chú, Tạm tính / Giảm giá, Tổng; nền xanh = Đã thanh toán, đỏ = Huỷ; mỗi thẻ là 1 nút (chuột, Tab + Enter). Mở từ menu = mặc định (Đã lên đơn, hôm nay); quay lại từ form / Chi tiết = giữ bộ lọc. Ctrl+N thêm, F5 làm mới |
| | **Thêm / sửa đơn** | Khách (điền sẵn khách đang lọc; ngày giao điền sẵn khi Từ = Đến), dòng món thường (chọn món, − / số / +) và **món tự do** (tên + đơn giá), Giảm giá, Tạm tính / Tổng / tổng số lượng tự tính, Ngày giao, Đã thanh toán trước + Ngày thanh toán, Ghi chú. **Dòng đổi giá bị khoá** (giữ đơn giá lúc đặt). Lưu (Ctrl+S) có **Cảnh báo trùng đơn**. Lỗi hiện cạnh nút Lưu, con trỏ về đúng ô, tự tắt khi sửa. Huỷ đơn, Xoá đơn |
| | **Chi tiết** | Toàn bộ đơn khớp bộ lọc, gộp theo khách + Tổng đơn từng khách; bấm đơn để sửa. **Thanh toán toàn bộ** (từ chối nếu có đơn đã thanh toán; đơn Huỷ không đổi). **Chụp ảnh** cả danh sách (kể cả phần phải cuộn; quá 4096 px thì thu nhỏ) → clipboard + `Pictures\MeRon\chi_tiet_don_hang_*.png` |
| | **Mua hàng** | Phiếu nhập theo khoảng ngày, mới nhất trước; thêm (Ctrl+N) / sửa: Ngày nhập, khoản Tên hàng + Giá tiền, Tổng tự cộng, Lưu (Ctrl+S; ≥ 1 khoản, tên + giá > 0), Xoá phiếu |
| Thống kê | **Theo đơn hàng** | Lọc trạng thái (mặc định Đã lên đơn) / khách / khoảng ngày: Tổng số đơn + Tổng thành tiền, ô từng trạng thái, bảng theo món + tổng số món, danh sách theo khách (4 cách sắp xếp) → bấm mở Chi tiết đơn của khách |
| | **Theo món ăn** | Khoảng ngày + (tuỳ chọn) 1 món: tổng số món, bảng từng món (món tự do gộp theo tên), các đơn liên quan → bấm mở form đơn |
| | **Tổng hợp** | Đơn hàng (số đơn, tiền, theo món) + Mua hàng (số phiếu, tiền, theo khoản) + Chênh lệch (bán − mua) |
| | **Khách hàng tiềm năng** | Tối đa 100 khách xếp theo tổng tiền → bấm mở Chi tiết đơn của khách |
| Danh mục | **Khách hàng** | STT / Tên / Số ĐT / Địa chỉ / Ghi chú; tìm theo tên / SĐT (không dấu); thêm / sửa (Địa chỉ gõ tự do hoặc chọn); trùng tên không cho lưu; không xoá được khách còn đơn |
| | **Món ăn** | STT / Tên / Giá / Danh mục / Mô tả; tìm; thêm / sửa. Không xoá, không quản lý ảnh (như điện thoại; ảnh có sẵn giữ nguyên) |
| | **Danh mục món ăn** | Sửa / Xoá ngay trên dòng; không xoá được danh mục còn món |
| | **Địa chỉ** | Mã / Tên; thêm / sửa (Mã không đổi); xoá trong form sửa |
| Dữ liệu | **Nhập dữ liệu** | Thư mục 1 lần backup hoặc thư mục cha (tự lấy bản mới nhất `yyyyMMdd_HHmmss`, bỏ `…_bk`). Kiểm tra cả 8 file trước, xem trước "Sẽ nhập / Đang có"; lỗi báo rõ file + dòng và **không** động tới dữ liệu. **Nhập (thay toàn bộ)** hỏi xác nhận, 1 transaction, bản sao an toàn trước |
| | **Xuất / Xoá dữ liệu** | **Xuất TSV**: thư mục `yyyyMMdd_HHmmss` + 8 file đúng định dạng điện thoại (nhớ thư mục lần trước; Mở thư mục vừa xuất). **Xoá toàn bộ dữ liệu** ở máy: xác nhận 2 lần, bản sao an toàn trước; Mở thư mục Backups |
| | **Hướng dẫn (F1)** | Cửa sổ hướng dẫn dùng chung (`SharedUI.Help.HelpWindow`), mở đúng mục của trang đang xem; nội dung ở `Core\HelpContent.cs` |

Mọi trang có khoảng ngày đều có nút chọn nhanh: Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày. Số và tiền
luôn hiển thị kiểu Việt Nam (`2.617`, `25.000 đ`) bất kể ngôn ngữ Windows.

### Khác điện thoại có chủ đích

- Sửa lỗi của điện thoại: lọc theo **ngày** giao (điện thoại so chuỗi nên bỏ sót đơn lưu dạng `yyyy-MM-dd HH:mm:ss` đúng
  ngày "Từ ngày"); xoá đơn xoá cả dòng món; Thanh toán toàn bộ không đổi đơn Huỷ; sửa + Lưu đơn đã Huỷ vẫn giữ Huỷ;
  đơn Huỷ không tính vào "Tổng số đơn" (Theo đơn hàng) và báo cáo Tổng hợp (điện thoại lẫn vào 2 chỗ này, trái hướng dẫn
  của chính nó).
- Hợp desktop: tải hết đơn khớp bộ lọc thay vì "Xem thêm" (2.617 đơn: 20 ms); đổi ngày là tìm ngay; số lượng gõ thẳng vào
  ô; form là trang, danh mục là hộp thoại; Chụp ảnh = clipboard + file; Tổng hợp thêm Chênh lệch; tìm không dấu; không
  xoá khách còn đơn / danh mục còn món.
- Xuất TSV: bảng trống ghi dòng tiêu đề (điện thoại ghi file rỗng mà chính nó không nhập được); xuống dòng `\r` trong ghi
  chú cũng thành dấu cách (điện thoại đọc bằng `readAsLines`, `\r` sẽ cắt đôi dòng). Còn lại giống từng byte.

Không làm (riêng của điện thoại / đang ẩn trên điện thoại): giỏ hàng POS + Giao dịch, in hoá đơn, thêm món bằng giọng
nói, đồng bộ Firestore, lịch tự sao lưu, ảnh món ăn. Để sau: lấy backup thẳng từ link Google Drive (thêm 1 `IBackupSource`).

## Dữ liệu

- Database: `%LOCALAPPDATA%\AppSuite\MeRon\me_ron.db` (đường dẫn hiện ở cuối trang Nhập dữ liệu). Biến môi trường
  `MERON_DB_PATH` đổi chỗ (dùng khi test).
- Bản sao an toàn trong `…\MeRon\Backups\`: `me_ron_truoc-import_<thời gian>.db` (trước mỗi lần Nhập) và
  `me_ron_truoc-xoa_<thời gian>.db` (trước Xoá toàn bộ), giữ 10 bản gần nhất mỗi loại. Lấy lại = đóng app, chép bản
  sao đè lên `me_ron.db`.
- Schema giống hệt điện thoại (`lib/core/services/database/database_config.dart`, version 2): 8 bảng `Address`,
  `Users`, `Categories`, `Products`, `Orders`, `OrderItems`, `Purchases`, `PurchaseItems`, cùng tên cột. Thêm riêng:
  bảng `MeRonMeta` (lần nhập gần nhất, thư mục nhập / xuất đã chọn) và vài index cho truy vấn.
- Định dạng bộ backup (theo `BackupService.exportToLocal` của điện thoại): thư mục `yyyyMMdd_HHmmss`, file
  `<bảng viết thường>_<yyyyMMdd_HHmmss>.tsv`, UTF-8 không BOM, dòng 1 = tên cột theo thứ tự cột của bảng, tab ngăn cách,
  `\n` xuống dòng (không có ở cuối file), null = ô trống, tab / xuống dòng trong dữ liệu thành dấu cách. Điện thoại ghi
  bảng rỗng = file rỗng hẳn; MeRon nhập được cả 2 kiểu. Ngày giờ giữ nguyên chuỗi gốc (điện thoại ghi lẫn
  `2026-06-27T14:20:53.242992` và `2026-06-27 07:20:53`).

## Cấu trúc code

```
Modules/MeRon/
|-- Core/                    Không phụ thuộc WinUI (Tests/MeRon.Tests biên dịch kèm trực tiếp)
|   |-- Data/Schema.cs           8 bảng + DDL giống điện thoại
|   |-- Data/MeRonDatabase.cs    Kết nối, tạo bảng, MeRonMeta, bản sao an toàn, Xoá toàn bộ
|   |-- Data/MasterDataRepository.cs  Địa chỉ / Danh mục / Khách hàng / Món ăn + quy tắc nghiệp vụ
|   |-- Data/OrderRepository.cs  Lọc / sắp xếp đơn, lưu đơn + dòng món, trùng đơn, huỷ, xoá, Thanh toán toàn bộ
|   |-- Data/PurchaseRepository.cs  Phiếu nhập + khoản (lọc ngày, lưu / sửa / xoá, kiểm tra)
|   |-- Orders/OrderDraft.cs     Form đơn + OrderCalculator (khoá dòng đổi giá, món tự do, tổng, kiểm tra)
|   |-- Reports/ReportRepository.cs  4 báo cáo bằng truy vấn tổng hợp SQL (bỏ đơn Huỷ)
|   |-- Import/BackupSource.cs   IBackupSource, LocalFolderBackupSource (thư mục / thư mục cha)
|   |-- Import/TsvTable.cs       Đọc + kiểm tra 1 file TSV
|   |-- Import/BackupImporter.cs Prepare (kiểm tra cả bộ) → Apply (thay toàn bộ, bản sao an toàn)
|   |-- Import/BackupExporter.cs Xuất 8 bảng đúng định dạng điện thoại (ghi thư mục .partial rồi đổi tên)
|   |-- Models/                  MasterData, Orders, Purchases
|   |-- DbDate.cs, DateRanges.cs, TextSearch.cs, VnFormat.cs, HelpContent.cs
|-- Services/                AppServices (database + repository dùng chung), AppLog (Logs\meron-*.log cạnh exe), Pickers
|-- ViewModels/              Import, Export, MasterData, Orders (+ Chi tiết), OrderForm, OrderCard, Purchase*, Report*, DateRangeFilter
`-- Views/                   15 trang + OrderCard, ReportTable, DateRangeBar (dùng chung), CustomerSuggest, Dialogs, Ui
```

## Unit test

`Tests\MeRon.Tests` (xUnit v3, exe tự chạy): `dotnet run --project Tests\MeRon.Tests\MeRon.Tests.csproj` — 43 test.

- Nhập: đọc TSV (rỗng, null, BOM / CRLF, số sai, lệch số ô, trùng khoá, cột lạ / thiếu), chọn thư mục backup (thư mục
  cha, bỏ `_bk`), thay toàn bộ giữ id gốc, bản sao an toàn, bộ lỗi không động tới database, cảnh báo liên kết.
- Xuất / Xoá: đúng tên thư mục / file, không BOM, không xuống dòng cuối, bảng trống = dòng tiêu đề, **xuất → nhập lại cho
  dữ liệu y hệt**, làm sạch tab / `\r` / `\n`; Xoá toàn bộ (bản sao, id bắt đầu lại, giữ thư mục đã chọn).
- Danh mục, đơn hàng (khoá dòng đổi giá, tổng, lỗi, trạng thái, 2 dạng ngày giờ, lọc / sắp xếp, lưu / sửa, trùng đơn,
  Thanh toán toàn bộ, xoá), phiếu nhập, 4 báo cáo, khoảng ngày nhanh, tìm không dấu, hướng dẫn F1 đủ mọi trang.

Chạy thêm với 1 bộ backup thật (không đưa vào repo): đặt `MERON_REAL_BACKUP=<thư mục>` rồi chạy `RealBackupTests` —
nhập, tải toàn bộ đơn, đối chiếu 4 báo cáo với tự cộng từng đơn, và **xuất lại phải giống từng byte file điện thoại**.

GUI: kiểm thử bằng UI Automation trong Windows Sandbox (không chạy trên máy thật) — xem `PLAN.md` mục Kiểm chứng.
