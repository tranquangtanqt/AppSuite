# ModuleH

Ứng dụng WinUI 3 độc lập - module "H": công cụ tra cứu tài liệu CRUD図 (Create/Read/Update/Delete -
logic nào dùng bảng/logic nào, dùng ra sao), đọc toàn bộ workbook Excel trong 1 thư mục nguồn, lưu vào
SQLite, rồi xuất thành 1 site HTML tĩnh duyệt được (1 file `02_CRUD図.html` + 1 file HTML riêng cho
từng logic). Không có bất kỳ tham chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới
`..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\ModuleH\ModuleH.csproj
```

hoặc mở `ModuleH.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Khác ModuleG ở đơn vị "1 record"

ModuleG là "1 file = 1 màn hình" (nhiều sheet/file). Ở ModuleH, **1 sheet không nằm trong danh sách
bỏ qua = 1 "logic/処理ID"** - tên sheet chính là mã (vd `MABSL0140`). Một workbook CRUD図 có thể chứa
từ vài sheet tới hàng chục/hàng trăm sheet như vậy (file mẫu `M7-DV-0203..._CRUD図（マスタ（ＳＣＭ）
）.xlsx` có 70 sheet không-skip; ngược lại `M7-DV-0206..._CRUD図（マスタ（リスト・入力補助））.xlsx`
gói mọi logic vào **1 sheet duy nhất** dài 1245 dòng). Vì vậy `CrudDocImporter` duyệt theo **sheet**
chứ không theo file, và `CrudRecord`/HTML/nav trong index đều tính theo sheet.

## Chức năng - pipeline 3 bước

1. **"Chon thu muc nguon..."** - chọn thư mục gốc chứa các file `.xlsx` CRUD図 (đệ quy mọi thư mục
   con). Đường dẫn lưu vào `Data\Config\config.xml`, tự điền lại lần mở sau.
2. **"1. Doc Excel → SQLite"** - đọc mọi `*.xlsx` trong thư mục nguồn (EPPlus 4.5.3.3), mỗi file lỗi
   được log cảnh báo và bỏ qua chứ không dừng cả batch. Với mỗi file, duyệt mọi worksheet, bỏ qua
   `表紙`/`変更来歴`/`本ドキュメントについて` và sheet rỗng; mỗi sheet còn lại được
   `CrudSheetParser` phân tích ngữ nghĩa rồi `CrudHtmlRenderer` dựng thành 1 trang HTML riêng ghi ngay
   ra `Data\Database\Html\{ScreenCode}.html`, đồng thời lưu metadata (mã/tên/module/sub-module/văn
   bản/toàn bộ text để tìm kiếm) vào SQLite (`Data\Database\02_CRUD図.db`, bảng `Logics`).
3. **"2. Xuat HTML"** - đọc bảng `Logics`, sinh `Data\Database\Html\02_CRUD図.html` (nhúng sẵn danh
   mục dạng JSON, không `fetch()` file ngoài - lý do giống hệt ModuleG, xem README ModuleG) +
   `manifest.json` (bản sao JSON thuần, chỉ để tiện dùng ngoài nếu cần).
4. **"Mo file HTML"** - mở `02_CRUD図.html` bằng trình duyệt mặc định.

## Giao diện `02_CRUD図.html`

Cột trái có **2 ô tìm kiếm** kết hợp `AND`, y hệt bố cục ModuleG:
- **"Ma logic / ten..."** - lọc theo mã logic (tên sheet) hoặc tên hiển thị (substring, không phân
  biệt hoa/thường).
- **"Tim theo ten bang/object..."** - lọc theo toàn bộ text trong sheet đó, qua đó **cũng chính là
  cách tìm "bảng X được logic nào dùng"** vì tên 使用オブジェクト nằm trong text này - đây là nhu cầu
  chính của 1 công cụ CRUD.

Danh sách kết quả bên dưới, click 1 logic nạp file HTML riêng của nó ngay trong khung nội dung bên
phải (`<iframe name="contentFrame">`), giống cơ chế ModuleG.

## Bộ dịch Excel → HTML: 1 semantic parser duy nhất cho mọi sheet

Khác ModuleG (phải có 3 parser riêng theo từng loại sheet + fallback lưới chung vì mỗi sheet trong
1 file 画面説明書 có bố cục khác nhau), **mọi sheet CRUD図 không nằm trong danh sách bỏ qua đều dùng
chung 1 bố cục**, xác nhận trực tiếp trên cả 2 file mẫu (đầu/giữa/cuối/nhỏ nhất mỗi file):

- **Rows 1-3**: khối metadata mcframe, nhưng **trộn 2 kiểu label/value khác nhau trong cùng khối**
  (xác nhận bằng cách đọc từng ô, không suy đoán):
  - `モジュールID`/`モジュール名` (row1), `ｻﾌﾞﾓｼﾞｭｰﾙID`/`ｻﾌﾞﾓｼﾞｭｰﾙ名` (row2, **half-width
    katakana** - không phải `サブモジュール...` full-width), `ロジック名` (row3, tên hiển thị của
    chính sheet) - label và value nằm **cùng hàng**, value cách label vài cột.
  - `文書番号`/`Version`/`Rev.` - cả 3 đều ở row1, nhưng value nằm **ngay hàng dưới, cùng cột**
    (row2), khác hẳn kiểu trên.
  - `CrudSheetParser.FindLabelValue` xử lý cả 2 kiểu: tìm label, thử ô liền phải cùng hàng trước
    (dừng lại nếu gặp ngay 1 label khác - nghĩa là không có value cùng hàng), rồi mới thử ô ngay bên
    dưới.
- **1 hàng header bảng dữ liệu** (luôn thấy ở row 5 trong mẫu, nhưng dò bằng cách quét text - không
  hardcode số hàng/cột - để chịu được sai lệch nhỏ giữa các file): `ID` | `名称` | `使用オブジェクト`
  | `C` | `R` | `U` | `D` | `種` | `備考`.
- Từ hàng header+1 trở đi là **các block lặp lại**: hàng có `ID` (+ `名称` cùng hàng) mở block mới;
  hàng có `使用オブジェクト` (kèm cờ C/R/U/D, `種`, `備考`) là 1 object thuộc block hiện tại; giữa 2
  block luôn có 1 hàng trắng hoàn toàn.

`CrudSheetParser` **đọc verbatim từng hàng** (không gộp thành cây block/object như bản đầu) - mỗi
hàng nguồn (kể cả hàng trắng) thành đúng 1 `CrudTableRow`, giữ nguyên mọi cột kể cả khi chúng nằm trên
chính hàng ID (vd hàng `Del01` có thể có sẵn `種=JO` dù không có `使用オブジェクト` - bản gộp-theo-block
trước đó làm mất dữ liệu này). `CrudHtmlRenderer` dựng **1 bảng `<table>` duy nhất cho cả sheet**, mỗi
hàng nguồn là đúng 1 `<tr>` (hàng có `ID` được tô đậm cột ID/名称 bằng class `crud-block-row`) - tái
hiện đúng layout gốc của Excel (1 bảng liên tục, không tách card riêng theo từng block).

Nếu 1 sheet nào đó không khớp bố cục này (không tìm thấy header `使用オブジェクト`) - trường hợp chưa
gặp ở 2 file mẫu - `CrudHtmlRenderer` rơi về dump toàn sheet dạng lưới thô đơn giản để không mất dữ
liệu, theo đúng triết lý "never silently lose content" đã áp dụng ở ModuleG.

## Link chéo giữa các logic (`使用オブジェクト` dạng `MaCode.ID`)

Nhiều ô `使用オブジェクト` không phải tên bảng mà là **tham chiếu tới đúng 1 block ID của 1 logic
khác**, vd `MSBBL6020.Slo_Chk03` = "gọi block `Slo_Chk03` bên trong sheet `MSBBL6020`". Vì vậy
`CrudDocImporter` chạy **2 lượt** qua toàn bộ file nguồn:

1. Lượt 1 (`BuildScreenCodeIndex`) - chỉ đọc tên mọi sheet không nằm trong danh sách skip, dựng
   `Dictionary<ScreenCode, HtmlFileName>` (dùng đúng logic dedup tên file như lượt chính, cùng thứ tự
   duyệt file/sheet nên 2 lượt luôn cho ra tên file khớp nhau) - chưa parse nội dung dòng nào.
2. Lượt 2 (như cũ) - parse + render từng sheet, nhưng giờ `CrudHtmlRenderer` nhận thêm map ở trên: ô
   `使用オブジェクト` dạng `{Code}.{ID}` mà `{Code}` khớp đúng 1 ScreenCode đã biết thì render thành
   `<a href="{Code}.html#blk-{ID}">` (mỗi hàng có `ID` được gắn sẵn `id="blk-{ID}"`); không khớp (tên
   bảng thường không có dấu `.`, hoặc gọi vào code ngoài phạm vi tài liệu này) thì giữ nguyên text.

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Đường dẫn dữ liệu resolve theo `AppContext.BaseDirectory` của chính tiến trình ModuleH (giống
  ModuleG) - `Data\Config\config.xml`, `Data\Database\02_CRUD図.db`, `Data\Database\Html\` tự tạo lúc
  chạy, không phụ thuộc ai khởi động tiến trình.
