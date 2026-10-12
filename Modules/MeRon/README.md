# MeRon

Bản máy tính của app điện thoại **me-ron** (Flutter, `D:\tantq\src\me-ron`): quản lý bán hàng — khách hàng, món ăn,
đơn hàng, phiếu nhập, báo cáo — trên SQLite riêng của máy tính. Dữ liệu lấy từ **file backup `.tsv`** mà điện thoại
xuất ra (Cài đặt → Sao lưu → Xuất file sao lưu), mỗi lần nhập là **thay toàn bộ**.

Thiết kế và lý do các quyết định: [PLAN.md](PLAN.md).

## Chạy

- Độc lập: mở `Modules\MeRon\MeRon.csproj`, đặt làm Startup Project, F5 — hoặc `dotnet run --project Modules\MeRon\MeRon.csproj`.
- Từ MainLauncher: mục **MeRon** trong Module List (sau `.\build\Sync-Modules-Dev.ps1` khi dev).
- Module chỉ tham chiếu `Common` + `SharedUI`, không bao giờ tham chiếu MainLauncher; không đọc `modules.json`.

## Chức năng hiện có (Đợt 1)

| Trang | Làm gì |
|---|---|
| **Nhập dữ liệu** | Chọn thư mục 1 lần backup (vd `20261011_231039`) hoặc thư mục cha (tự lấy thư mục con mới nhất dạng `yyyyMMdd_HHmmss`, bỏ qua `…_bk`). Đọc + kiểm tra cả 8 file trước, bảng xem trước "Sẽ nhập / Đang có"; lỗi (thiếu file, sai tiêu đề, số sai, trùng khoá) báo rõ file + dòng và **không** động tới dữ liệu. **Nhập (thay toàn bộ)** có hỏi xác nhận, ghi trong 1 transaction, trước đó chép database ra `Backups\` |
| **Khách hàng** | Bảng STT / Tên / Số ĐT / Địa chỉ / Ghi chú; tìm theo tên hoặc SĐT (không phân biệt hoa thường, dấu); thêm / sửa (Địa chỉ gõ tự do hoặc chọn từ danh mục); trùng tên (không phân biệt hoa thường) không cho lưu; xoá trong form sửa, không xoá được khách còn đơn hàng |
| **Món ăn** | Bảng STT / Tên / Giá / Danh mục / Mô tả; tìm theo tên; thêm / sửa (Danh mục, Tên, Giá bán, Mô tả). Không xoá, không quản lý ảnh (giống điện thoại hiện tại; ảnh có sẵn giữ nguyên khi sửa) |
| **Danh mục món ăn** | Bảng có nút Sửa / Xoá ngay trên dòng (như điện thoại); không xoá được danh mục còn món |
| **Địa chỉ** | Bảng Mã / Tên; thêm / sửa (Mã không đổi sau khi tạo); xoá trong form sửa |
| **Hướng dẫn (F1)** | Cửa sổ hướng dẫn dùng chung (`SharedUI.Help.HelpWindow`), mở đúng mục của trang đang xem; nội dung ở `Core\HelpContent.cs` |

Lần đầu mở (database trống) app vào thẳng trang **Nhập dữ liệu**; có dữ liệu rồi thì vào **Khách hàng**.

Số và tiền luôn hiển thị kiểu Việt Nam (`2.617`, `25.000 đ`) bất kể ngôn ngữ Windows.

### Chưa làm (theo kế hoạch)

- Đợt 2: **Đặt hàng** (danh sách lọc / sắp xếp, màn Chi tiết gộp theo khách, Thanh toán toàn bộ, tạo / sửa đơn, món tự
  do, khoá dòng đã đổi giá, cảnh báo trùng đơn, huỷ / xoá đơn).
- Đợt 3: **Mua hàng** (phiếu nhập) + 4 **báo cáo** (Theo đơn hàng, Theo món ăn, Tổng hợp, Khách hàng tiềm năng).
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
|   |-- Import/BackupSource.cs   IBackupSource, LocalFolderBackupSource (thư mục / thư mục cha)
|   |-- Import/TsvTable.cs       Đọc + kiểm tra 1 file TSV
|   |-- Import/BackupImporter.cs Prepare (kiểm tra cả bộ) → Apply (thay toàn bộ, bản sao an toàn)
|   |-- TextSearch.cs, VnFormat.cs, HelpContent.cs
|-- Services/                AppServices (database dùng chung), AppLog (Logs\meron-*.log cạnh exe), Pickers
|-- ViewModels/              ImportViewModel, MasterDataViewModels (khung danh sách + 4 trang)
`-- Views/                   5 trang + Dialogs (form Lưu / Xoá / Huỷ, lỗi hiện trong hộp thoại)
```

## Unit test

`Tests\MeRon.Tests` (xUnit v3, exe tự chạy): `dotnet run --project Tests\MeRon.Tests\MeRon.Tests.csproj`.
Đọc TSV (rỗng, null, BOM / CRLF, số sai, lệch số ô, trùng khoá, cột lạ / thiếu), chọn thư mục backup (thư mục cha,
bỏ `_bk`), import (giữ id gốc, thay toàn bộ, bản sao an toàn, bộ lỗi không động tới database, cảnh báo liên kết,
id mới tiếp sau id lớn nhất), quy tắc danh mục, tìm không dấu, hướng dẫn F1 đủ mọi trang.

Chạy thêm với 1 bộ backup thật (không đưa vào repo): đặt `MERON_REAL_BACKUP=<thư mục>` rồi chạy test
`RealBackupTests`.
