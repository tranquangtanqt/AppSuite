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
2. MeRon **Nhập dữ liệu** → chọn thư mục đã chép về máy (hoặc thư mục cha), **hoặc dán link folder Google Drive** (dán vào là tự đọc):
   app tự tải bản mới nhất (không cần đăng nhập; folder phải chia sẻ "Bất kỳ ai có đường liên kết"), **hoặc đăng nhập Google**
   ở khung "Hoặc lấy từ Google Drive" rồi chọn 1 bản trong danh sách (folder không cần công khai).
3. Xem / lọc đơn, báo cáo; sửa nhỏ trên máy tính nếu cần.
4. Muốn đưa phần sửa trên máy tính về điện thoại: **Xuất / Xoá dữ liệu** → Xuất TSV → chép sang điện thoại → Cài đặt →
   Sao lưu → Nhập dữ liệu từ tệp sao lưu (từng bảng).

Lần đầu mở (database trống) app vào thẳng trang **Nhập dữ liệu**; có dữ liệu rồi thì vào **Đặt hàng**.

## Chức năng

| Menu | Trang | Làm gì |
|---|---|---|
| Bán hàng | **Đặt hàng** | Lọc trạng thái (Tất cả / Đã lên đơn / Đã thanh toán / Huỷ), khách (gõ tên / SĐT, không dấu), khoảng ngày giao; sắp xếp theo thành tiền / ngày giao; "N đơn · Tổng X". Thẻ đơn: khách, trạng thái, ngày giao, các món, ghi chú, Tạm tính / Giảm giá, Tổng; vạch màu bên trái + nhãn trạng thái (cam = Đã lên đơn nền trắng, xanh lá = Đã thanh toán nền xanh nhạt, xám = Huỷ nền xám mờ); mỗi thẻ là 1 nút (chuột, Tab + Enter). Lần đầu mở = mặc định (Đã lên đơn, hôm nay); sau đó giữ bộ lọc khi vào form / Chi tiết hoặc chuyển trang khác rồi quay lại (cả báo cáo, Mua hàng, ô tìm ở danh mục) - đến khi tắt app. Ctrl+N thêm, F5 làm mới |
| | **Thêm / sửa đơn** | Khách (điền sẵn khách đang lọc; ngày giao điền sẵn khi Từ = Đến), dòng món thường (ô món gõ lọc không dấu theo từng từ, Enter chọn, **bắt buộc chọn trong danh sách**; − / số / +) và **món tự do** (tên + đơn giá), Giảm giá, Tạm tính / Tổng / tổng số lượng tự tính, Ngày giao, Đã thanh toán trước + Ngày thanh toán, Ghi chú. **Dòng đổi giá bị khoá** (giữ đơn giá lúc đặt). Lưu (Ctrl+S) có **Cảnh báo trùng đơn**. Lỗi hiện cạnh nút Lưu, con trỏ về đúng ô, tự tắt khi sửa. Huỷ đơn, Xoá đơn. **Sao chép đơn** (chuột phải thẻ đơn / nút trong form): đơn mới cùng khách, món, giảm giá, ghi chú; giữ ngày giao của đơn gốc, Đã lên đơn, giá hiện tại (báo nếu giá đổi; món không còn → món tự do) |
| | **Chi tiết** | Toàn bộ đơn khớp bộ lọc, gộp theo khách + Tổng đơn từng khách; bấm đơn để sửa. **Thanh toán toàn bộ** (từ chối nếu có đơn đã thanh toán; đơn Huỷ không đổi). **Chụp ảnh** cả danh sách (kể cả phần phải cuộn; quá 4096 px thì thu nhỏ) → clipboard + `Pictures\MeRon\chi_tiet_don_hang_*.png` |
| | **Mua hàng** | Phiếu nhập theo khoảng ngày, mới nhất trước (thẻ vạch xanh dương = chi; nhãn "N phiếu · Tổng X" xanh dương); thêm (Ctrl+N) / sửa: Ngày nhập, khoản Tên hàng + Giá tiền, Tổng tự cộng, Lưu (Ctrl+S; ≥ 1 khoản, tên + giá > 0), Xoá phiếu |
| Thống kê | **Theo đơn hàng** | Lọc trạng thái (mặc định Đã lên đơn) / khách / khoảng ngày: 4 ô Tổng số đơn / Tổng thành tiền / TB mỗi đơn / Tổng số món, ô từng trạng thái (số đơn, tiền, thanh % số đơn), bảng theo món (thanh so với món bán nhiều nhất) + tổng số món, danh sách theo khách (4 cách sắp xếp) → bấm mở Chi tiết đơn của khách |
| | **Theo món ăn** | Khoảng ngày + (tuỳ chọn) 1 món: 3 ô Tổng số món / Tổng thành tiền / Số món khác nhau, bảng từng món có thanh tỷ lệ (món tự do gộp theo tên), các đơn liên quan → bấm mở form đơn |
| | **Tổng hợp** | 3 ô: Bán (cam: tiền + số đơn), Mua (xanh dương: tiền + số phiếu), Chênh lệch bán − mua (xanh lá khi lãi, đỏ khi lỗ); bảng Bán theo món + Mua theo khoản, có thanh tỷ lệ |
| | **Khách hàng tiềm năng** | Tối đa 100 khách xếp theo tổng tiền → bấm mở Chi tiết đơn của khách |
| | **Khách lâu không đặt** | Khách đã đặt ≥ N đơn (mặc định 2) mà đơn gần nhất (ngày giao, bỏ đơn Huỷ) cách hôm nay ≥ M ngày (mặc định 30; chọn nhanh 14 / 30 / 60 / 90): lần cuối + số ngày, thói quen (TB ngày giữa các lần đặt, cần ≥ 3 ngày đặt; nhãn cam khi nghỉ ≥ 2 lần thói quen), số đơn + tổng tiền, 3 món hay đặt. Sắp xếp lâu nhất / tiền nhiều nhất / quá thói quen nhất, tìm (Ctrl+F). Nút: Xem đơn, Đặt lại (chép đơn gần nhất, giao hôm nay), Tin nhắn (chép câu hỏi thăm cho Zalo, bỏ mã phòng ở tên), chép SĐT. Tính ở `Core\Reports\LapsedCustomers.cs` |
| Danh mục | **Khách hàng** | STT / Tên (ô tròn chữ cái đầu, bỏ qua số phòng / mã toà) / Số ĐT / Địa chỉ (nhãn) / Ghi chú; tìm theo tên / SĐT (không dấu); thêm / sửa (Địa chỉ gõ tự do hoặc chọn); trùng tên không cho lưu; không xoá được khách còn đơn |
| | **Món ăn** | STT / Tên / Giá (cam) / Danh mục (nhãn xanh dương) / Mô tả; tìm; thêm / sửa. Không xoá, không quản lý ảnh (như điện thoại; ảnh có sẵn giữ nguyên) |
| | **Danh mục món ăn** | Sửa / Xoá ngay trên dòng; không xoá được danh mục còn món |
| | **Địa chỉ** | Mã (nhãn cam) / Tên; thêm / sửa (Mã không đổi); xoá trong form sửa |
| Dữ liệu | **Nhập dữ liệu** | Thư mục 1 lần backup hoặc thư mục cha (tự lấy bản mới nhất `yyyyMMdd_HHmmss`, bỏ `…_bk`; đồng thời liệt kê hết các bản trong thư mục cha / link folder Drive để chọn bản khác). **Google Drive (đăng nhập)**: danh sách các bản backup app điện thoại / MeRon đã tải lên, mới nhất trước (nơi lưu, đủ bảng chưa) → bấm 1 bản để tải về rồi kiểm tra; đọc bằng Drive API (`Core\Cloud\DriveBackupBrowser.cs`, quyền `drive.file`), folder không cần công khai. Kiểm tra cả 8 file trước, xem trước "Sẽ nhập / Đang có"; lỗi báo rõ file + dòng và **không** động tới dữ liệu. **Cảnh báo thay đổi chưa xuất**: trigger SQLite trên 8 bảng đếm dòng thêm / sửa / xoá trên máy (meta `unsyncedEdits`, tắt trong lúc nhập); còn thì trang Nhập hiện khung vàng + nút Mở trang Xuất, hộp xác nhận đổi thành "Vẫn nhập, bỏ thay đổi"; xuất / nhập / xoá toàn bộ thì về 0. **Nhập (thay toàn bộ)** hỏi xác nhận, 1 transaction, bản sao an toàn trước |
| | **Xuất / Xoá dữ liệu** | **Xuất TSV**: thư mục `yyyyMMdd_HHmmss` + 8 file đúng định dạng điện thoại (nhớ thư mục lần trước; Mở thư mục vừa xuất). **Xuất lên Google Drive**: Đăng nhập Google (OAuth Desktop app + PKCE, loopback 127.0.0.1, quyền `drive.file`; refresh token trong Windows Credential Manager - `Services\GoogleServices.cs`), xuất TSV rồi tải lên thành thư mục `yyyyMMdd_HHmmss` trong folder đã chọn hoặc folder riêng "MeRon backup" (`Core\Cloud\`). File OAuth client `client_secret_….json` đặt ở `Assets\` (không commit, `.gitignore`) hoặc `%LOCALAPPDATA%\AppSuite\MeRon`; không có thì báo "chưa cấu hình". **Xoá toàn bộ dữ liệu** ở máy: xác nhận 2 lần, bản sao an toàn trước; Mở thư mục Backups |
| | **Hướng dẫn (F1)** | Cửa sổ hướng dẫn dùng chung (`SharedUI.Help.HelpWindow`), mở đúng mục của trang đang xem; nội dung ở `Core\HelpContent.cs` |
| | **Phím tắt** (mục riêng trong F1) | Ctrl+F ô tìm / lọc khách; Ctrl+N thêm (mọi trang danh sách); Ctrl+1…4 tab trạng thái (Đặt hàng, Theo đơn hàng); Ctrl+D khoảng ngày nhanh; F5 làm mới; form: Ctrl+S lưu, Ctrl+Shift+N lưu + đơn mới cùng ngày giao, Ctrl+M thêm món / thêm hàng, ↑ / ↓ trong ô Số lượng, Ctrl+Shift+C sao chép đơn, Esc quay lại. Gắn bằng `Views\Shortcuts.cs` lên chính trang (không hiện tooltip phím tắt; bỏ qua khi đang mở hộp thoại) |

Mọi trang có khoảng ngày đều có nút chọn nhanh: Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày. Mọi ô ngày là
`Views\DateBox` (thay CalendarDatePicker): gõ thẳng dd/MM/yyyy (gõ tắt "14/10", "14", "141026" - `Core\DateInput.cs`), ↑ / ↓ ±1 ngày,
nút lịch (Alt+↓ / F4) tiếng Việt tuần bắt đầu thứ Hai + Hôm nay + Xoá ngày (ô không bắt buộc). Số và tiền
luôn hiển thị kiểu Việt Nam (`2.617`, `25.000 đ`) bất kể ngôn ngữ Windows. Màu chủ đạo cam (`#E8590C`, khai trong `App.xaml`)
thay màu nhấn của Windows cho nút chính, tab, menu, số tiền; mua hàng (chi) dùng xanh dương `#1C7ED6`. Các trang danh mục: bảng
trong khung bo góc, số mục là nhãn tròn.

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
nói, đồng bộ Firestore, lịch tự sao lưu, ảnh món ăn.

Google Drive (2026-10-13): đọc folder công khai qua trang `embeddedfolderview` của Google (không phải API chính thức,
không cần khoá API / đăng nhập), tải file bằng `uc?export=download` về `%TEMP%\AppSuite\MeRon\GoogleDrive\<bản>` rồi nạp
như thư mục trên máy. Google đổi trang thì báo lỗi rõ, vẫn nhập từ thư mục được. Để sau: đăng nhập Google (OAuth, Drive
API) để folder không phải để công khai - chỉ thêm 1 `IBackupSource` nữa.

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
|   |-- Import/GoogleDriveBackupSource.cs  Link folder Drive công khai → bản mới nhất → tải 8 file về thư mục tạm
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
