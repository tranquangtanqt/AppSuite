# ModuleD

Ứng dụng WinUI 3 độc lập - module "D": công cụ tra cứu từ điển dữ liệu (database dictionary) build
từ các workbook Excel định nghĩa DB nội bộ. Không có bất kỳ tham chiếu nào tới `MainLauncher`; chỉ
`ProjectReference` tới `..\..\Common\Common.csproj` để dùng chung model/logging.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\ModuleD\ModuleD.csproj
```

hoặc mở `ModuleD.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Chức năng - pipeline 3 bước

1. **"1. Doc Excel → SQLite"** - đọc toàn bộ `Data\Excel\*.xlsm` (workbook "DBDef" tiếng Nhật) bằng
   EPPlus 4.5.3.3, rồi lưu vào SQLite tại `Data\Database\ModuleD.db` (`Services\ModuleDDatabase`,
   ADO.NET thuần qua `Microsoft.Data.Sqlite`, cùng pattern với `ModuleB\Services\SavedFolderRepository`).
   Mỗi lần chạy sẽ rebuild toàn bộ 3 bảng `Tables`/`Columns`/`ForeignKeys` (drop + create + insert lại).
2. **"2. Xuat HTML"** - đọc lại `ModuleD.db`, sinh 1 file HTML tĩnh, tự chứa (data nhúng dạng JSON,
   JS thuần, không phụ thuộc CDN/mạng) tại `Data\Database\ModuleD.html` (`Services\HtmlReportGenerator`).
3. **"Mo file HTML"** - mở file HTML vừa xuất bằng trình duyệt mặc định.

Log/tiến trình import (file đang đọc, cảnh báo bảng/cột không đọc được) hiện trong khung log ở dưới
cùng cửa sổ. Trang HTML xuất ra có menu trái (danh sách bảng + ô tìm kiếm) và nội dung phải: 説明,
管理タイプ, danh sách cột cần lưu ý khi thay đổi, 改廃, rồi tới bảng cột của bảng đang chọn; tìm kiếm
khớp trên tên bảng, tên tiếng Nhật, và tên/nhãn cột. Mỗi dòng cột có màu nền phân biệt: vàng nhạt =
khóa chính (`Level == 0`), xanh nhạt = cột dùng chung được mở rộng từ `$...$` group (xem bên dưới).
Ngay dưới đó là bảng "Khóa ngoại (FOREIGN)" liệt kê các quan hệ FK của bảng đang chọn; tên bảng tham
chiếu là link `#table=TÊN_BẢNG` trỏ vào chính file HTML này, mở bằng `target="_blank"` nên bấm vào sẽ
mở **tab trình duyệt mới** đã chọn sẵn bảng đó (nếu bảng đó có trong dữ liệu đã đọc) - có thể mở
`ModuleD.html#table=TÊN_BẢNG` trực tiếp để chia sẻ link tới 1 bảng cụ thể.

## Cấu trúc workbook nguồn (`Data\Excel\*.xlsm`)

Mỗi file có 1 sheet index `テーブル・ビュー一覧` (tên bảng | tên tiếng Nhật | loại | ghi chú | tên
sheet chứa định nghĩa). Sheet định nghĩa có thể chứa nhiều "block" bảng, mỗi block gồm: hàng marker
(`*`/`*w` + tên bảng vật lý + alias + tên tiếng Nhật), rồi tới 1 vùng mô tả tự do (có thể có 1 hoặc
nhiều mục dạng `【説明】`/`【管理タイプ】`/`【運用後の変更に注意が必要な項目】`/`【改廃】`, thứ tự
không cố định, không phải mục nào cũng có mặt), rồi tới bảng liệt kê cột (レベル/項目名/型/桁/Null/
日本語/説明...). `Services\ExcelDbDefImporter` dò vị trí cột bằng tên header (không hard-code index
cột) vì layout không hoàn toàn giống nhau giữa các sheet; mục `【運用後の変更に注意が必要な項目】`
là 1 mini-bảng con (項目名/変更不可理由) được gộp lại thành các dòng `tên cột: lý do`.

Một số dòng cột trong bảng chỉ tham chiếu tới 1 nhóm cột dùng chung (level thường là `88`, tên cột
dạng `$EXCTRL_COLS$`, `$IF_COMM_COLS$`,...) thay vì khai báo trực tiếp. `ExcelDbDefImporter` đọc
sheet `制御用` (hoặc `EXCTRL` tùy file) trước - nơi định nghĩa thật của các nhóm này theo đúng format
1 block bảng bình thường - dựng thành 1 bảng tra `$TÊN_GROUP$ -> danh sách cột`, rồi khi gặp dòng
tham chiếu `$...$` sẽ chèn thẳng các cột thật vào đúng vị trí thay vì bỏ qua (đánh dấu `IsCommon` để
tô màu riêng trong HTML). Nhóm nào không tìm thấy định nghĩa (không có trong `制御用`/`EXCTRL` của
chính file đó) thì vẫn bị bỏ qua như trước - trường hợp này rất hiếm và được log lại.

Dòng khai báo khóa ngoại có dạng `FOREIGN | | cot_cuc_bo[,cot...] | | ten_bang_tham_chieu | | cot_tham_chieu[,cot...]`
(cột tham chiếu có thể để trống nếu trùng tên với cột cục bộ). `ExcelDbDefImporter` không dùng offset
cột cố định cho dòng này (vì lệch nhẹ giữa các sheet) mà lấy lần lượt 3 ô không rỗng đầu tiên sau cột
marker, lưu vào bảng `ForeignKeys` riêng (`TableName`, `LocalColumns`, `ReferencedTable`,
`ReferencedColumns`), hiển thị trong HTML dưới dạng bảng "Khóa ngoại (FOREIGN)" (xem bên dưới).

Khi cần cập nhật dữ liệu: chỉ cần thay/thêm file `.xlsm` trong `Data\Excel\`, chạy lại bước "1. Doc
Excel → SQLite" rồi "2. Xuat HTML".

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Đường dẫn dữ liệu resolve theo `AppContext.BaseDirectory` của chính tiến trình ModuleD (giống
  ModuleB/ModuleC) - `Data\Excel\*.xlsm` được copy cạnh exe qua `CopyToOutputDirectory=PreserveNewest`
  trong `ModuleD.csproj`, không phụ thuộc ai khởi động tiến trình (VS, `dotnet run`, hay
  `Process.Start` từ MainLauncher).
