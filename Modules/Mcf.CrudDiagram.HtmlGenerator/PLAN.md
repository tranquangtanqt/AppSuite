# Mcf.CrudDiagram.HtmlGenerator: CRUD図 Excel → SQLite → HTML

## Bối cảnh

`D:\Projects\202607_MCF7\02_CRUD図` chứa 2 file `.xlsx` CRUD図 (ma trận Create/Read/Update/Delete:
logic nào dùng bảng/logic nào, dùng ra sao). Người dùng muốn 1 module tương tự Mcf.Screen.HtmlGenerator (Excel →
SQLite → HTML tĩnh), bỏ qua sheet `表紙`/`変更来歴`/`本ドキュメントについて`, và yêu cầu tìm ra cấu
trúc chung giữa các sheet trước để việc render đơn giản.

Đã xác nhận trực tiếp bằng EPPlus trên cả 2 file thật (74→70 sheet không-skip trong
`M7-DV-0203..._CRUD図（マスタ（ＳＣＭ））.xlsx`, 1 sheet duy nhất 1245 dòng trong
`M7-DV-0206..._CRUD図（マスタ（リスト・入力補助））.xlsx`):

- **Khác Mcf.Screen.HtmlGenerator về đơn vị "1 record"**: Mcf.Screen.HtmlGenerator là "1 file = 1 màn hình (nhiều sheet)". Ở đây
  **1 sheet không-bị-skip = 1 "logic/処理ID"** (tên sheet = mã, vd `MABSL0140`).
- Layout dữ liệu (block ID + object rows với cờ C/R/U/D) đồng nhất tuyệt đối qua mọi sheet đã lấy
  mẫu → chỉ cần **1 semantic parser duy nhất**, khác hẳn Mcf.Screen.HtmlGenerator phải có 3 parser theo loại sheet.

## Thiết kế

Scaffold copy nguyên cấu trúc `Modules\Mcf.Screen.HtmlGenerator` sang `Modules\Mcf.CrudDiagram.HtmlGenerator` (đổi namespace), giữ đúng 3
quy tắc kiến trúc AppSuite (csproj chỉ `ProjectReference` `Common`, không đọc `modules.json`, không
reference `MainLauncher`). File output theo lựa chọn người dùng: `Data\Database\02_CRUD図.db` +
`Data\Database\Html\02_CRUD図.html` (giữ quy ước đặt tên theo tên thư mục nguồn, giống Mcf.Screen.HtmlGenerator).

### `CrudSheetParser` - phát hiện quan trọng lúc code so với giả định ban đầu

Giả định ban đầu (label/value nằm cùng hàng, đơn giản) **sai một phần** - đọc trực tiếp từng ô mới
xác nhận đúng: khối header (rows 1-3) trộn **2 kiểu bố cục khác nhau**:

1. `モジュールID`/`モジュール名` (row1), `ｻﾌﾞﾓｼﾞｭｰﾙID`/`ｻﾌﾞﾓｼﾞｭｰﾙ名` (row2 - **half-width
   katakana**, không phải `サブモジュール...` full-width như đoán ban đầu), `ロジック名` (row3) -
   label và value **cùng hàng**, value cách label vài cột.
2. `文書番号`/`Version`/`Rev.` (đều ở row1) - value nằm **ngay hàng dưới, cùng cột** (row2) - khác
   hẳn kiểu 1.

`CrudSheetParser.FindLabelValue` xử lý cả 2 kiểu bằng 1 hàm chung: tìm ô đúng bằng `label`, thử ô
liền phải cùng hàng trước (**dừng ngay nếu gặp 1 label khác** trong tập `HeaderLabels` - nghĩa là
không có value cùng hàng, tránh bug đã gặp lúc test: lấy nhầm tên label kế tiếp làm value), sau đó
mới thử ô ngay bên dưới cùng cột. `ScreenCode` không cần parse - dùng thẳng `sheet.Name`.

Bảng dữ liệu (`ID`/`名称`/`使用オブジェクト`/`C`/`R`/`U`/`D`/`種`/`備考`) dò header bằng cách quét
text (không hardcode số hàng/cột) - `FindDataHeaderColumns` quét 15 hàng đầu tìm hàng có cả `ID` và
`使用オブジェクト`, đọc vị trí mọi cột khác từ cùng hàng đó. Từ hàng header+1: hàng có `ID` không rỗng
mở block mới; hàng có `使用オブジェクト` không rỗng append vào block hiện tại (2 điều kiện độc lập).
Không tìm thấy header → `CrudHtmlRenderer` fallback dump nguyên sheet dạng lưới thô (chưa gặp trường
hợp này ở 2 file mẫu, nhưng giữ đường lui an toàn theo đúng tinh thần "never silently lose content"
của Mcf.Screen.HtmlGenerator).

### Các thành phần khác - mirror 1:1 pattern Mcf.Screen.HtmlGenerator

- `Models\CrudRecord.cs` (1 record/sheet: ScreenCode/ScreenName/ModuleId/ModuleName/SubModuleId/
  SubModuleName/DocNumber/Version/Revision/SourceFile/SearchText/HtmlFileName/BlockCount).
- `Services\SourceFolderSettingsStore.cs` - copy nguyên từ Mcf.Screen.HtmlGenerator.
- `Services\CrudHtmlRenderer.cs` - mỗi block ID → 1 `div.crud-block` (heading ID+名称) chứa
  `table.crud-table` liệt kê object rows; C/R/U/D render nguyên ký tự gốc (○/◎/rỗng).
- `Services\CrudDocImporter.cs` - duyệt mọi `*.xlsx`, mọi worksheet (không phải mọi file), bỏ qua
  skip-list, 1 sheet → 1 file HTML + 1 `CrudRecord`.
- `Services\CrudDatabase.cs` - SQLite bảng `Logics` tại `02_CRUD図.db`.
- `Services\HtmlIndexGenerator.cs` - `02_CRUD図.html`, 2 ô tìm kiếm (mã/tên; nội dung - ô nội dung
  gián tiếp cho phép tìm theo tên bảng/object vì tên object nằm trong `SearchText`, đúng nhu cầu
  chính của công cụ CRUD).
- `ViewModels\McfCrudDiagramHtmlGeneratorViewModel.cs`, `MainWindow.xaml(.cs)` - mirror `McfScreenHtmlGeneratorViewModel` không đổi gì
  về luồng (Import/ExportHtml/OpenHtml commands + log panel).
- `Mcf.CrudDiagram.HtmlGenerator.csproj` **không có SkiaSharp** (không có sơ đồ shape/connector nào cần vẽ lại như Mcf.Screen.HtmlGenerator).

## Kiểm chứng

Đã chạy thử qua 1 harness console tạm (ngoài UI, dùng lại nguyên `Services`/`Models` của module,
tham chiếu EPPlus) trên đúng 2 file thật tại `D:\Projects\202607_MCF7\02_CRUD図`:

- **71/71 sheet đọc thành công** (70 sheet không-skip của file 1 + 1 sheet của file 2), **0 sheet có
  0 block** - `FindDataHeaderColumns` tìm thấy header ở mọi sheet mẫu.
- Tổng **758 block ID** trên 71 sheet (`MAUAL0010` - sheet 1245 dòng của file 2 - có 267 block, nhiều
  nhất).
- Đối chiếu HTML render của `MABSL0140` với dữ liệu gốc dump trực tiếp từ workbook: đúng từng ID/名称/
  使用オブジェクト/cờ C·R·U·D/種 (vd block `AprvAfter_Upd01`/`後処理（申請）` → 3 object đúng thứ tự,
  đúng cờ ◎/○ từng dòng).
- Header metadata (`ModuleId=MA`, `SubModuleId=MABS`/`MAUA`, `DocNumber=M7-DV-0203`/`M7-DV-0206`,
  `Version=7.0.2`, `Revision=r01.04`/`r01.03`) khớp đúng dữ liệu gốc cho cả 2 file - đây là vòng sau
  khi sửa xong bug half-width katakana + label/value 2-kiểu nêu trên (lần chạy đầu tiên bị sai do 2
  lỗi này, đã kiểm chứng lại sau khi sửa).
- `dotnet build Modules\Mcf.CrudDiagram.HtmlGenerator\Mcf.CrudDiagram.HtmlGenerator.csproj -p:Platform=x64` build sạch.

Còn lại (đã làm nhưng chưa tự kiểm chứng bằng UI thật, cần user xác nhận): mở `Mcf.CrudDiagram.HtmlGenerator.csproj` F5,
chạy UI thật (chọn thư mục → Doc Excel → Xuat HTML → Mo file HTML), xác nhận index hiện đủ 71 logic,
tìm theo tên object (vd `MAM_BP`) ra đúng các logic có dùng bảng đó.

### Vòng 2: 1 bảng liên tục thay vì tách card riêng theo từng block

Người dùng gửi ảnh chụp Excel gốc (1 bảng liên tục `ID|名称|使用オブジェクト|C|R|U|D|種|備考`, hàng
trắng ngăn cách giữa các block) và yêu cầu giữ đúng layout này thay vì bản đầu (mỗi block 1
`div.crud-block` + `table` riêng). Đổi `CrudSheetModel`: bỏ `List<CrudBlock>` (cây block→object rows),
thay bằng `List<CrudTableRow>` đọc **verbatim từng hàng nguồn** (kể cả hàng trắng) - đơn giản hoá luôn
`CrudSheetParser.ParseRows` (bỏ hẳn state machine "block hiện tại"), và tình cờ sửa luôn 1 bug: bản cũ
chỉ giữ `Id`/`Name` cho hàng block-header, làm mất dữ liệu `種`/`備考` nếu chúng nằm ngay trên hàng ID
(gặp thực tế: hàng `Del01` có `種=JO` dù không có `使用オブジェクト`). `CrudHtmlRenderer` dựng 1
`<table>` duy nhất, mỗi hàng nguồn 1 `<tr>`, hàng có ID được tô đậm cột ID/名称 (`crud-block-row`).

Test lại qua harness console: HTML sinh ra khớp đúng cấu trúc ảnh gốc (hàng trắng đầu bảng, block
header giữ đủ cột kể cả khi có 種 riêng, tổng 758 block/71 sheet không đổi).

### Vòng 3: link chéo cho `使用オブジェクト` dạng `MaCode.ID`

Người dùng chỉ ra: nhiều giá trị `使用オブジェクト` có dấu `.` (vd `MSBBL6020.Slo_Chk03`) thực chất là
tham chiếu sang đúng 1 block ID của 1 sheet/logic khác, không phải tên bảng - muốn biến thành link
nhảy thẳng. Vấn đề: khi render 1 sheet, chưa biết sheet khác sẽ có tên file `.html` là gì (đặc biệt
nếu trùng tên phải thêm hậu tố `_2`). Giải quyết bằng **2 lượt** duyệt file nguồn: lượt 1
(`CrudDocImporter.BuildScreenCodeIndex`) chỉ đọc tên sheet (không parse nội dung dòng) để dựng trước
`Dictionary<ScreenCode, HtmlFileName>` dùng đúng thứ tự file/sheet + đúng thuật toán dedup của lượt
chính (nên tự động khớp nhau, không cần chia sẻ state); lượt 2 (như cũ) parse+render, truyền dict trên
vào `CrudHtmlRenderer` để: (a) gắn `id="blk-{ID}"` lên mỗi hàng có ID, (b) khi `使用オブジェクト` dạng
`{Code}.{ID}` mà `{Code}` khớp 1 ScreenCode đã biết → render `<a href="{Code}.html#blk-{ID}">`; không
khớp (tên bảng thường/gọi code ngoài tài liệu) → giữ nguyên text, không đoán bừa.

Test lại qua harness console trên bộ dữ liệu 販売管理 (`M7-DV-0221`, 43 sheet): tìm được link tự tham
chiếu `MSBBB0020.SlsActReg_Ins02` bên trong chính sheet `MSBBB0020`, xác nhận đúng `id="blk-
SlsActReg_Ins02"` tồn tại trong file `MSBBB0020.html`; giá trị `MAUDL5010.pushEndLog` (module ngoài
phạm vi tài liệu) đúng như dự kiến **không** bị biến thành link.
