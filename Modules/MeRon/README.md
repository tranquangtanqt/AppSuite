# MeRon

Bản máy tính của app điện thoại **me-ron** (Flutter, `D:\tantq\src\me-ron`): quản lý bán hàng — khách hàng, món ăn,
đơn hàng, phiếu nhập, báo cáo — trên SQLite riêng của máy tính. Dữ liệu lấy từ **file backup `.tsv`** mà điện thoại
xuất ra (Cài đặt → Sao lưu → Xuất file sao lưu), mỗi lần nhập là **thay toàn bộ**.

Thiết kế và lý do các quyết định: [PLAN.md](PLAN.md).

## Chạy

- Độc lập: mở `Modules\MeRon\MeRon.csproj`, đặt làm Startup Project, F5 — hoặc `dotnet run --project Modules\MeRon\MeRon.csproj`.
- Từ MainLauncher: mục **MeRon** trong Module List (sau `.\build\Sync-Modules-Dev.ps1` khi dev).
- Module chỉ tham chiếu `Common` + `SharedUI`, không bao giờ tham chiếu MainLauncher; không đọc `modules.json`.

## Chức năng hiện có (Đợt 1 + 2 + 3)

| Trang | Làm gì |
|---|---|
| **Mua hàng** | Phiếu nhập trong khoảng ngày (mặc định hôm nay), mới nhất trước: ngày, các khoản (tên, giá), Tổng; "N phiếu · Tổng X". Thêm (Ctrl+N) / sửa: Ngày nhập, khoản Tên hàng + Giá tiền (Thêm hàng, X xoá dòng), Tổng tự cộng, Lưu (Ctrl+S; ≥ 1 khoản, mỗi khoản có tên + giá > 0, lỗi chỉ đúng ô), Xoá phiếu (xoá cả khoản) |
| **Thống kê theo đơn hàng** | Lọc trạng thái (mặc định Đã lên đơn) / khách / khoảng ngày. Tổng số đơn + Tổng thành tiền (không tính Huỷ), ô từng trạng thái, bảng theo món (+ tổng số món), danh sách theo khách (4 cách sắp xếp) → bấm 1 khách mở Chi tiết đơn của khách trong đúng bộ lọc |
| **Thống kê theo món ăn** | Khoảng ngày + (tuỳ chọn) 1 món: tổng số món, bảng từng món (món tự do gộp theo tên), các đơn liên quan (chỉ dòng của món đã chọn) → bấm mở form đơn |
| **Thống kê tổng hợp** | Đơn hàng (số đơn, thành tiền, theo món) + Mua hàng (số phiếu, thành tiền, theo khoản - gộp theo tên không phân biệt hoa thường) + dòng Chênh lệch (bán − mua) |
| **Khách hàng tiềm năng** | Xếp hạng tối đa 100 khách theo tổng tiền (không tính Huỷ): hạng, tên, số đơn, tiền → bấm mở Chi tiết đơn của khách (mọi trạng thái) |
| **Đặt hàng** | Lọc trạng thái (Tất cả / Đã lên đơn / Đã thanh toán / Huỷ), khách (gõ tên / SĐT, không dấu), khoảng ngày giao (+ chọn nhanh Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày); sắp xếp theo thành tiền / ngày giao; dòng tổng "N đơn · Tổng X". Thẻ đơn: khách, trạng thái, ngày giao, các món, ghi chú, Tạm tính / Giảm giá, Tổng; nền xanh = Đã thanh toán, đỏ = Huỷ; mỗi thẻ là 1 nút (chuột, Tab + Enter). Mở từ menu = bộ lọc mặc định (Đã lên đơn, hôm nay); quay lại từ form / Chi tiết = giữ bộ lọc. Ctrl+N thêm đơn, F5 làm mới |
| **Thêm / sửa đơn** | Khách (điền sẵn khách đang lọc; ngày giao điền sẵn khi Từ = Đến), dòng món thường (chọn món, − / số / +) và **món tự do** (tên + đơn giá), Giảm giá, Tạm tính / Tổng / tổng số lượng tự tính, Ngày giao, Đã thanh toán trước + Ngày thanh toán, Ghi chú. **Dòng đổi giá bị khoá** (giữ đơn giá lúc đặt). Lưu (Ctrl+S) có **Cảnh báo trùng đơn** (khách đã có đơn khác cùng ngày giao, không tính đơn huỷ). Món tự do thiếu tên / đơn giá → báo lỗi cạnh nút Lưu và đưa con trỏ về đúng ô. Huỷ đơn, Xoá đơn (xoá cả dòng món) |
| **Chi tiết** | Toàn bộ đơn khớp bộ lọc, gộp theo khách + Tổng đơn từng khách; bấm đơn để sửa. **Thanh toán toàn bộ** (từ chối nếu danh sách có đơn đã thanh toán; đơn Huỷ không đổi). **Chụp ảnh**: cả danh sách (kể cả phần phải cuộn; danh sách rất dài thì ảnh thu nhỏ cho vừa 4096 px) → clipboard + `Pictures\MeRon\chi_tiet_don_hang_*.png` |
| **Nhập dữ liệu** | Chọn thư mục 1 lần backup (vd `20261011_231039`) hoặc thư mục cha (tự lấy thư mục con mới nhất dạng `yyyyMMdd_HHmmss`, bỏ qua `…_bk`). Đọc + kiểm tra cả 8 file trước, bảng xem trước "Sẽ nhập / Đang có"; lỗi (thiếu file, sai tiêu đề, số sai, trùng khoá) báo rõ file + dòng và **không** động tới dữ liệu. **Nhập (thay toàn bộ)** có hỏi xác nhận, ghi trong 1 transaction, trước đó chép database ra `Backups\` |
| **Khách hàng** | Bảng STT / Tên / Số ĐT / Địa chỉ / Ghi chú; tìm theo tên hoặc SĐT (không phân biệt hoa thường, dấu); thêm / sửa (Địa chỉ gõ tự do hoặc chọn từ danh mục); trùng tên (không phân biệt hoa thường) không cho lưu; xoá trong form sửa, không xoá được khách còn đơn hàng |
| **Món ăn** | Bảng STT / Tên / Giá / Danh mục / Mô tả; tìm theo tên; thêm / sửa (Danh mục, Tên, Giá bán, Mô tả). Không xoá, không quản lý ảnh (giống điện thoại hiện tại; ảnh có sẵn giữ nguyên khi sửa) |
| **Danh mục món ăn** | Bảng có nút Sửa / Xoá ngay trên dòng (như điện thoại); không xoá được danh mục còn món |
| **Địa chỉ** | Bảng Mã / Tên; thêm / sửa (Mã không đổi sau khi tạo); xoá trong form sửa |
| **Hướng dẫn (F1)** | Cửa sổ hướng dẫn dùng chung (`SharedUI.Help.HelpWindow`), mở đúng mục của trang đang xem; nội dung ở `Core\HelpContent.cs` |

Lần đầu mở (database trống) app vào thẳng trang **Nhập dữ liệu**; có dữ liệu rồi thì vào **Đặt hàng**.

Khác điện thoại có chủ đích (sửa lỗi / hợp desktop): lọc theo **ngày** giao (điện thoại so chuỗi nên bỏ sót đơn lưu dạng
`yyyy-MM-dd HH:mm:ss` đúng ngày "Từ ngày"); tải hết đơn khớp bộ lọc thay vì "Xem thêm" (2.617 đơn: 20 ms); đổi ngày là
tìm ngay; xoá đơn xoá cả dòng món; Thanh toán toàn bộ không đổi đơn Huỷ; sửa + Lưu đơn đã Huỷ vẫn giữ Huỷ; số lượng gõ
thẳng vào ô (thay hộp thoại "Nhập số lượng"); Chụp ảnh = clipboard + file (thay chia sẻ của điện thoại). Báo cáo: "Tổng số
đơn" (Theo đơn hàng) và cả báo cáo Tổng hợp không tính đơn Huỷ (điện thoại lẫn đơn huỷ vào 2 chỗ này, trái với hướng dẫn
của chính nó); Tổng hợp thêm dòng Chênh lệch; mọi trang có chọn nhanh khoảng ngày.

Số và tiền luôn hiển thị kiểu Việt Nam (`2.617`, `25.000 đ`) bất kể ngôn ngữ Windows.

### Chưa làm (theo kế hoạch)

- Đợt 4: Xuất TSV (cùng định dạng điện thoại), Xoá toàn bộ dữ liệu.
- Sau này: lấy backup thẳng từ link Google Drive (chỉ thêm 1 `IBackupSource` mới).

Không làm (tính năng riêng của điện thoại / đang ẩn trên điện thoại): giỏ hàng POS + Giao dịch, in hoá đơn, thêm món
bằng giọng nói, đồng bộ Firestore, lịch tự sao lưu, ảnh món ăn.

## Dữ liệu

- Database: `%LOCALAPPDATA%\AppSuite\MeRon\me_ron.db` (đường dẫn hiện ở cuối trang Nhập dữ liệu). Biến môi trường
  `MERON_DB_PATH` đổi chỗ (dùng khi test).
- Bản sao trước mỗi lần nhập: `…\MeRon\Backups\me_ron_truoc-import_<yyyyMMdd_HHmmss>.db`, giữ 10 bản gần nhất. Lấy lại
  = đóng app, chép bản sao đè lên `me_ron.db`.
- Schema giống hệt điện thoại (`lib/core/services/database/database_config.dart`, version 2): 8 bảng `Address`,
  `Users`, `Categories`, `Products`, `Orders`, `OrderItems`, `Purchases`, `PurchaseItems`, cùng tên cột. Thêm riêng:
  bảng `MeRonMeta` (lần nhập gần nhất, thư mục đã chọn) và vài index cho truy vấn.
- Định dạng file backup (theo `BackupService.exportToLocal` của điện thoại): `<bảng viết thường>_<yyyyMMdd_HHmmss>.tsv`,
  UTF-8, dòng 1 = tên cột, tab ngăn cách, `\n` xuống dòng, null = ô trống, tab / xuống dòng trong dữ liệu đã thành dấu
  cách; **bảng rỗng = file rỗng hẳn** (không có dòng tiêu đề). Ngày giờ giữ nguyên chuỗi gốc (điện thoại ghi lẫn
  `2026-06-27T14:20:53.242992` và `2026-06-27 07:20:53`).

## Cấu trúc code

```
Modules/MeRon/
|-- Core/                    Không phụ thuộc WinUI (Tests/MeRon.Tests biên dịch kèm trực tiếp)
|   |-- Data/Schema.cs           8 bảng + DDL giống điện thoại
|   |-- Data/MeRonDatabase.cs    Kết nối, tạo bảng, MeRonMeta
|   |-- Data/MasterDataRepository.cs  Địa chỉ / Danh mục / Khách hàng / Món ăn + quy tắc nghiệp vụ
|   |-- Data/OrderRepository.cs  Lọc / sắp xếp đơn, lưu đơn + dòng món, trùng đơn, huỷ, xoá, Thanh toán toàn bộ
|   |-- Orders/OrderDraft.cs     Form đơn + OrderCalculator (khoá dòng đổi giá, món tự do, tổng, kiểm tra)
|   |-- Data/PurchaseRepository.cs  Phiếu nhập + khoản (lọc ngày, lưu / sửa / xoá, kiểm tra)
|   |-- Reports/ReportRepository.cs 4 báo cáo bằng truy vấn tổng hợp SQL (bỏ đơn Huỷ)
|   |-- Models/Orders.cs, Purchases.cs, DbDate.cs (2 dạng ngày giờ của điện thoại), DateRanges.cs
|   |-- Import/BackupSource.cs   IBackupSource, LocalFolderBackupSource (thư mục / thư mục cha)
|   |-- Import/TsvTable.cs       Đọc + kiểm tra 1 file TSV
|   |-- Import/BackupImporter.cs Prepare (kiểm tra cả bộ) → Apply (thay toàn bộ, bản sao an toàn)
|   |-- TextSearch.cs, VnFormat.cs, HelpContent.cs
|-- Services/                AppServices (database dùng chung), AppLog (Logs\meron-*.log cạnh exe), Pickers
|-- ViewModels/              Import, MasterData, Orders (+ Chi tiết), OrderForm, OrderCard, Purchase*, Report*, DateRangeFilter
`-- Views/                   14 trang + OrderCard, ReportTable, DateRangeBar (dùng chung), CustomerSuggest, Dialogs
```

## Unit test

`Tests\MeRon.Tests` (xUnit v3, exe tự chạy): `dotnet run --project Tests\MeRon.Tests\MeRon.Tests.csproj`.
Đọc TSV (rỗng, null, BOM / CRLF, số sai, lệch số ô, trùng khoá, cột lạ / thiếu), chọn thư mục backup (thư mục cha,
bỏ `_bk`), import (giữ id gốc, thay toàn bộ, bản sao an toàn, bộ lỗi không động tới database, cảnh báo liên kết,
id mới tiếp sau id lớn nhất), quy tắc danh mục, tìm không dấu, hướng dẫn F1 đủ mọi trang; đơn hàng (khoá dòng đổi giá,
tổng không âm, thông báo lỗi giống điện thoại, trạng thái khi lưu, 2 dạng ngày giờ, lọc ngày / trạng thái / khách, 4
cách sắp xếp, lưu đơn món thường + tự do, sửa thay dòng món, trùng đơn, Thanh toán toàn bộ, xoá, khoảng ngày nhanh); phiếu
nhập (thêm / sửa / lọc / xoá, kiểm tra); 4 báo cáo (bỏ đơn huỷ, gộp món / khoản, lọc 1 món, xếp hạng khách). Test backup
thật còn đối chiếu tổng của báo cáo với tự cộng từng đơn.

Chạy thêm với 1 bộ backup thật (không đưa vào repo): đặt `MERON_REAL_BACKUP=<thư mục>` rồi chạy test
`RealBackupTests`.
