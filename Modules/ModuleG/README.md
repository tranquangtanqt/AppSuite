# ModuleG

Ứng dụng WinUI 3 độc lập - module "G": công cụ tra cứu tài liệu đặc tả màn hình (画面説明書), đọc
toàn bộ workbook Excel trong 1 thư mục nguồn, lưu vào SQLite, rồi xuất thành 1 site HTML tĩnh duyệt
được (1 file `index.html` + 1 file HTML riêng cho từng màn hình). Không có bất kỳ tham chiếu nào tới
`MainLauncher`; chỉ `ProjectReference` tới `..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\ModuleG\ModuleG.csproj
```

hoặc mở `ModuleG.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Chức năng - pipeline 3 bước

1. **"Chon thu muc nguon..."** - chọn thư mục gốc chứa các file `.xlsx` "画面説明書" (đệ quy mọi thư
   mục con). Đường dẫn lưu vào `Data\Config\config.xml`, tự điền lại lần mở sau.
2. **"1. Doc Excel → SQLite"** - đọc mọi `*.xlsx` trong thư mục nguồn (EPPlus 4.5.3.3), mỗi file lỗi
   được log cảnh báo và bỏ qua chứ không dừng cả batch. Với mỗi file: tách mã màn hình/tên màn hình/
   số văn bản/revision từ **tên file** (quy ước `{DocNumber}_{ScreenCode}_{Revision}_画面説明書（{Tên}）.xlsx`),
   dựng HTML cho từng sheet bằng bộ dịch **generic** (`Services\ExcelSheetHtmlRenderer`, xem bên dưới),
   ghi ngay ra `Data\Database\Html\{ScreenCode}.html` + ảnh nhúng vào
   `Data\Database\Html\Images\{ScreenCode}\`, rồi lưu metadata (mã/tên/số văn bản/revision/đường dẫn
   nguồn/toàn bộ text để tìm kiếm) vào SQLite (`Data\Database\ModuleG.db`, bảng `Screens`).
3. **"2. Xuat HTML"** - đọc bảng `Screens`, sinh `Data\Database\Html\index.html` (nhúng sẵn danh mục
   dạng JSON, không `fetch()` file ngoài - xem "Vì sao nhúng JSON" bên dưới) + `manifest.json` (bản
   sao JSON thuần, không phải thứ `index.html` đọc, chỉ để tiện dùng ngoài nếu cần).
4. **"Mo file HTML"** - mở `index.html` bằng trình duyệt mặc định.

## Giao diện `index.html`

Cột trái có **2 ô tìm kiếm** kết hợp `AND`:
- **"Ma man hinh / ten man hinh..."** - lọc theo mã màn hình hoặc tên màn hình (substring, không phân
  biệt hoa/thường).
- **"Tim theo noi dung..."** - lọc theo toàn bộ text trong mọi sheet của màn hình đó.

Danh sách kết quả bên dưới, click 1 màn hình nạp file HTML riêng của nó **ngay trong khung nội dung bên
phải** (`<iframe name="contentFrame">`, link dùng `target="contentFrame"`) thay vì mở tab mới - vẫn là
1 navigation `<a>`/`<iframe>` thuần, không phải `fetch()` nên không đụng vấn đề CORS đã né tránh ở phần
dưới. Mục đang xem được tô đậm trong danh sách bên trái.

## Bộ dịch Excel → HTML: semantic cho 3 sheet, generic cho phần còn lại, bỏ qua `変更来歴`

`変更来歴` (lịch sử sửa đổi) **không được render** - bỏ qua hoàn toàn ngay lúc import (không section,
không nav, không tính vào số sheet), vì đây là sổ hành chính không cần thiết khi đọc để hiểu màn hình.

3 sheet còn lại mang nội dung "đọc để hiểu màn hình" được parse ngữ nghĩa thay vì lưới ô Excel thô:

- `概要` (mục đích/operation/SQLID/sort/data access control) và `画面遷移` (điều hướng màn hình) -
  `OverviewSheetParser`/`ScreenDiagramSheetParser` - dựng bảng/card có nhãn rõ ràng theo đúng bố cục app
  web tham khảo (`mcf7-web-develop/front-end/.../document/screen`). Khác với app đó (đọc offset cột cố
  định từ 1 chuỗi text đã dump sẵn), 2 parser này định vị cột bằng cách **so khớp tên nhãn thật** trong
  file (vd tìm ô có đúng text "処理名") vì đọc thẳng workbook qua EPPlus - bền hơn với sai khác cột giữa
  các file.
- `項目説明` (mô tả từng field, sheet phức tạp nhất - tới 358 ô merge/397 dòng) - `ItemExplanationSheetParser`
  - không cố tái hiện đúng cấu trúc nguồn, chỉ rút ra **4 cột gọn**: bắt buộc/項目名/説明/型, nhóm theo
  từng group (操作種別/検索/登録/...). Bỏ hẳn màu nền/border/merge/dòng đệm của bản gốc để bảng gọn, tải
  nhanh, dễ lướt hơn nhiều so với lưới ô đầy đủ.

`表紙`/`画面イメージ` và **bất kỳ sheet 概要/画面遷移/項目説明 nào không khớp cấu trúc mong đợi** (thiếu
marker 【...】/thiếu cột header) vẫn dùng bộ dịch **generic chung** (`ExcelSheetHtmlRenderer.RenderGridRange`):
dựng `<table>` đúng theo merge cell/màu nền/chữ đậm/canh lề của Excel, chèn ảnh nhúng (`sheet.Drawings`)
thành 1 khối `<img>` trước dòng chứa ô neo của ảnh. Việc luôn có generic renderer làm lưới an toàn nghĩa
là 1 file có layout khác lạ vẫn hiển thị đúng nội dung (chỉ kém đẹp hơn) thay vì mất dữ liệu. Kết quả
vẫn **không pixel-perfect** so với bản Excel gốc.

## Vì sao index.html nhúng JSON thay vì `fetch()` manifest.json

Mở `index.html` qua `file://` (double-click hoặc "Mo file HTML") rồi gọi `fetch()`/`XMLHttpRequest`
tới 1 file JSON khác trên đĩa bị **CORS chặn** trên các trình duyệt Chromium (Edge/Chrome mặc định) -
đây là hành vi trình duyệt, không sửa được từ phía HTML/JS. Do đó `index.html` nhúng thẳng cùng dữ
liệu dạng JSON vào 1 thẻ `<script type="application/json">` (giống cách ModuleD/E đã làm), giống nhau
về ý tưởng "1 file tự chứa" nhưng **chỉ áp dụng cho danh mục nhẹ** (mã/tên/số văn bản/revision/text
tìm kiếm) - không nhúng HTML/ảnh của từng màn hình (phần đó vẫn là file riêng, theo đúng quyết định đã
chốt để tránh 1 file khổng lồ).

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Đường dẫn dữ liệu resolve theo `AppContext.BaseDirectory` của chính tiến trình ModuleG (giống
  ModuleD/E) - `Data\Config\config.xml`, `Data\Database\ModuleG.db`, `Data\Database\Html\` tự tạo lúc
  chạy, không phụ thuộc ai khởi động tiến trình.
