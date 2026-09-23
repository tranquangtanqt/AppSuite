# Mcf.Screen.HtmlGenerator: Tài liệu màn hình (画面説明書) Excel → SQLite → HTML

## Bối cảnh

`D:\Projects\202607_MCF7\mcf7\02_ユーザ詳細\01_画面説明書\` chứa **617 file `.xlsx`** "画面説明書"
(tài liệu đặc tả 1 màn hình nghiệp vụ), mỗi file gồm 6 sheet cố định:
`表紙`(bìa)/`変更来歴`(lịch sử sửa)/`概要`(tổng quan - khớp nội dung 【概要】【オペレーション一覧】
【モード一覧】【ソート項目】【データアクセスコントロールー覧】【画面補足】 trong ảnh mẫu
`mcf7_screen.jpg`)/`画面イメージ`(ảnh chụp màn hình nhúng - phần ảnh ở đầu trang mẫu)/`項目説明`(mô
tả từng field, tới **358 ô merge / 397 dòng**)/`画面遷移`(điều hướng màn hình). Đây **không phải**
dạng bảng dữ liệu phẳng như DBDef của Mcf.DbDef.HtmlGenerator - là tài liệu định dạng phức tạp (merge cell dày đặc,
màu nền, ảnh nhúng, đoạn văn bản dài dạng gạch đầu dòng).

Người dùng muốn cùng mô hình pipeline Mcf.DbDef.HtmlGenerator/E (Excel → cache SQLite → xuất HTML duyệt được), với
UI có 2 ô tìm kiếm bên trái: 1 ô tìm theo **mã màn hình** (vd `MSBBP1210`), 1 ô tìm theo **nội dung**.
Kiến trúc AppSuite cấm module phụ thuộc lẫn nhau nên đây là **module độc lập mới "Mcf.Screen.HtmlGenerator"**.

### 2 quyết định đã chốt với người dùng trước khi thiết kế

1. **Dịch sheet → HTML bằng generic grid renderer** (không parse ngữ nghĩa từng mục như Mcf.DbDef.HtmlGenerator) -
   1 bộ chuyển đổi chung duyệt mọi sheet, dựng `<table>` đúng theo merge cell/màu nền/bold của Excel,
   chèn ảnh nhúng gần đúng vị trí. Áp dụng đồng nhất cho cả 617 file mà không cần hiểu nghĩa từng ô -
   rẻ hơn nhiều so với reverse-engineer chính xác cấu trúc 6 sheet (đặc biệt `項目説明` 358 merge).
2. **Xuất nhiều file**: mỗi màn hình → 1 file `.html` riêng (+ ảnh nhúng lưu file rời cạnh đó) thay vì
   nhúng tất cả (617 màn hình + ảnh) vào 1 file HTML tự chứa như Mcf.DbDef.HtmlGenerator/E - tránh 1 file khổng lồ,
   trình duyệt mở mượt hơn nhiều. `index.html` chung có 2 ô tìm kiếm, đọc 1 file JSON danh mục nhẹ,
   click mở file HTML màn hình tương ứng ở tab mới (`target="_blank"`, giống pattern link FK của
   Mcf.DbDef.HtmlGenerator).

## Thiết kế

### 1. Khung module (giống Mcf.DbDef.HtmlGenerator/E)

`Modules\Mcf.Screen.HtmlGenerator\{App.xaml(.cs), MainWindow.xaml(.cs), Assets/*, Package.appxmanifest, app.manifest,
Mcf.Screen.HtmlGenerator.csproj, README.md, PLAN.md}`. `Mcf.Screen.HtmlGenerator.csproj`: `ProjectReference` chỉ tới `Common`;
`PackageReference` giống hệt Mcf.DbDef.HtmlGenerator (`Microsoft.Windows.SDK.BuildTools`, `Microsoft.WindowsAppSDK
2.2.0`, `CommunityToolkit.Mvvm 8.3.2`, `Microsoft.Data.Sqlite 8.0.10`, **`EPPlus 4.5.3.3`**) - **không
cần package mới nào**, EPPlus 4.x đã đủ API đọc `MergedCells`/`Style.Fill`/`Style.Font`/`Drawings`
(đã dùng ổn định trong Mcf.DbDef.HtmlGenerator). Không có DI container, theo đúng convention thực tế của Module.

### 2. Cấu hình thư mục nguồn (giống ConnectionSettingsStore của Rdbms.HtmlGenerator)

Thư mục nguồn (`D:\Projects\...\01_画面説明書`) nằm ngoài repo, quá lớn để bundle như
`Data\Excel\*.xlsm` của Mcf.DbDef.HtmlGenerator - user chọn qua nút **"Thiet lap thu muc nguon"** mở dialog (TextBox
đường dẫn + nút Browse dùng `FolderPicker`/`WindowNative.GetWindowHandle` như `ModuleB\MainWindow.xaml.cs`),
lưu vào `Data\Config\config.xml` (`Services\SourceFolderSettingsStore`, `XmlSerializer`, cùng quy ước
`AppContext.BaseDirectory`-relative như Rdbms.HtmlGenerator) để tự điền lại lần sau.

### 3. Models (`Modules\Mcf.Screen.HtmlGenerator\Models\`)

- `ScreenRecord.cs`: `ScreenCode`, `ScreenName`, `DocNumber`, `Revision`, `SourceFile`, `SearchText`
  (toàn bộ text mọi cell, gộp lại - dùng cho tìm nội dung), `HtmlFileName` (tên file `.html` xuất ra).
- `SourceFolderSettings.cs`: POCO thuần mutable (`RootFolder`) cho `XmlSerializer`.

### 4. Services

- **`ScreenCodeParser.cs`** - tách `ScreenCode`/`ScreenName`/`DocNumber`/`Revision` từ **tên file**
  bằng regex theo đúng quy ước quan sát được: `{DocNo}_{ScreenCode}_{Revision}_画面説明書（{Name}）.xlsx`
  (vd `M7-US-1102_MSBBP1210_r01.02_画面説明書（受注登録）.xlsx` → code `MSBBP1210`, tên `受注登録`).
  File không khớp pattern (hiếm, vd `M7-US-1100_M7_r01.02_画面説明書（共通）.xlsx`) vẫn import
  best-effort (dùng tên file làm `ScreenCode` fallback) - log cảnh báo, không dừng batch.
- **`ExcelSheetHtmlRenderer.cs`** (trọng tâm - generic, tái dùng cho mọi sheet của mọi file) -
  `RenderSheet(ExcelWorksheet sheet, string imagesOutputDir, StringBuilder searchTextAccumulator) -> string html`:
  - Duyệt `sheet.Dimension` theo `Cells[r,c]`, dựng `<table>` với `<colgroup>` (độ rộng cột xấp xỉ từ
    `Column.Width * 7px`).
  - `sheet.MergedCells` → map `(row,col) -> (rowSpan,colSpan)`, ô nằm trong 1 merge (không phải góc
    trên-trái) thì bỏ qua khi render (đã được `rowspan/colspan` của ô góc phủ).
  - Style: `cell.Style.Fill.BackgroundColor.Rgb` → `background-color` inline; `Font.Bold` → `font-weight`;
    `Font.Color`/`HorizontalAlignment` tương tự - inline style trực tiếp trên `<td>` (đơn giản hơn
    dựng bảng CSS class, chấp nhận HTML hơi dài vì đổi lại không cần theo dõi bảng class riêng).
  - Text: `cell.Text` giữ line-break (`\n` → `<br>`); mọi giá trị non-empty được append vào
    `searchTextAccumulator` (dùng chung cho `ScreenRecord.SearchText`).
  - Ảnh nhúng: `sheet.Drawings` (kiểu `ExcelPicture`) - ghi `Image.ImageBytes` ra file
    `imagesOutputDir\{sheetName}_{index}.png`, chèn `<img>` thành 1 block **trước** dòng chứa ô neo
    (`Drawing.From.Row`) thay vì overlay tuyệt đối theo pixel - đơn giản hoá cố ý (không cố pixel-perfect),
    đủ dùng cho ảnh chụp màn hình vốn nằm ở đầu sheet `画面イメージ`.
- **`ScreenDocImporter.cs`** - `Task<List<ScreenRecord>> ImportAsync(string rootFolder, string imagesRootDir, Action<string> log)`:
  `Directory.EnumerateFiles(rootFolder, "*.xlsx", AllDirectories)`, mỗi file bọc try/catch riêng (lỗi
  1 file không hỏng batch, log cảnh báo - giống `ExcelDbDefImporter`), mở bằng EPPlus, lặp
  `package.Workbook.Worksheets` theo đúng thứ tự sheet trong file, gọi `ExcelSheetHtmlRenderer` cho
  từng sheet rồi nối thành 1 HTML fragment hoàn chỉnh cho màn hình đó (mỗi sheet 1 khối `<section>`
  với `<h2>` = tên sheet), lưu `ScreenRecord.HtmlFileName` (từ `ScreenCode`, ký tự không hợp lệ trên
  Windows thay bằng `_`) và ghi luôn file `.html` đó ra `Data\Database\Html\{HtmlFileName}` **ngay
  lúc import** (ảnh cũng ghi luôn ra `Data\Database\Html\Images\{ScreenCode}\` cùng lúc) - vì bytes
  ảnh/markup HTML tĩnh không cần vòng qua SQLite, chỉ metadata + text tìm kiếm mới cần lưu DB.
- **`McfScreenHtmlGeneratorDatabase.cs`** - cùng pattern ADO.NET thuần (`Microsoft.Data.Sqlite`) như
  `McfDbDefHtmlGeneratorDatabase`/`RdbmsHtmlGeneratorDatabase`: 1 bảng `Screens` (`Id, ScreenCode, ScreenName, DocNumber,
  Revision, SourceFile, SearchText, HtmlFileName`), `ReplaceAllAsync` drop+create+insert lại toàn bộ
  mỗi lần chạy import, file `Data\Database\Mcf.Screen.HtmlGenerator.db`.
- **`HtmlIndexGenerator.cs`** - đọc toàn bộ `Screens` từ DB, ghi `Data\Database\Html\index.html`
  (khung trang tĩnh: 2 ô input trái + khu vực danh sách kết quả bên phải, JS thuần không CDN, cùng
  style/pattern menu trái của Mcf.DbDef.HtmlGenerator) + `Data\Database\Html\manifest.json`
  (`[{code, name, docNumber, revision, searchText, htmlFile}]`). Ô 1 ("Ma man hinh") lọc theo
  `code`/`name` (substring, không phân biệt hoa/thường); ô 2 ("Noi dung") lọc theo `searchText` chứa
  từ khóa; 2 điều kiện **kết hợp AND** trên cùng 1 danh sách kết quả hiển thị (khớp đúng yêu cầu
  "2 input, 1 tìm mã màn hình, 1 tìm nội dung"). Click 1 kết quả mở `htmlFile` ở tab mới.

  **Sửa lại lúc code so với bản plan ban đầu**: `index.html` **không** `fetch()` `manifest.json` lúc
  chạy như dự tính - mở file HTML qua `file://` rồi gọi `fetch()`/`XMLHttpRequest` tới 1 file JSON
  khác trên đĩa bị **CORS chặn** trên trình duyệt Chromium (Edge/Chrome mặc định), đây là hành vi
  trình duyệt không sửa được từ phía JS. Sửa bằng cách nhúng thẳng cùng dữ liệu JSON vào 1 thẻ
  `<script type="application/json">` ngay trong `index.html` (giống hệt kỹ thuật Mcf.DbDef.HtmlGenerator/E đã dùng),
  `manifest.json` vẫn được ghi ra như 1 bản sao thuần túy (không phải thứ trang web đọc) để tiện dùng
  ngoài nếu cần. Vì manifest chỉ chứa metadata nhẹ (không HTML/ảnh từng màn hình), việc nhúng vẫn giữ
  đúng tinh thần quyết định ban đầu (tránh 1 file khổng lồ chứa cả 617 trang + ảnh).

### 5. ViewModel + Giao diện (`McfScreenHtmlGeneratorViewModel`, `MainWindow.xaml(.cs)`)

Giống hệt `McfDbDefHtmlGeneratorViewModel`/`RdbmsHtmlGeneratorViewModel` (không DI, `AppendLog` qua `DispatcherQueue`):
`SourceFolder` (nạp qua `SourceFolderSettingsStore` lúc khởi tạo), `CanImport` (có `SourceFolder` hợp
lệ). Nút: **"Thiet lap thu muc nguon"** (mở dialog Browse, lưu settings) → **"1. Doc Excel → SQLite"**
(`ScreenDocImporter` + `McfScreenHtmlGeneratorDatabase.ReplaceAllAsync` trên `Task.Run`, log theo từng file/617) →
**"2. Xuat HTML"** (`HtmlIndexGenerator`) → **"Mo file HTML"** (mở `index.html` bằng trình duyệt mặc
định). ProgressBar/StatusText/Log giữ nguyên style Mcf.DbDef.HtmlGenerator.

### 6. Gắn vào solution

`MainLauncher\Config\modules.json`, `AppSuite.sln` (project + GUID mới +
`ProjectConfigurationPlatforms`), `build\Sync-Modules-Dev.ps1`, `build\Publish-AppSuite.ps1`, root
`README.md` - đúng 4+1 điểm sửa như các module trước.

## Rủi ro/đánh đổi đã biết trước

- Generic renderer **không pixel-perfect** so với `mcf7_screen.jpg` (không overlay ảnh theo tọa độ
  pixel chính xác, không tái hiện mọi style Excel) - đổi lại áp dụng được đồng nhất cho 617 file khác
  layout nhau mà không cần parse ngữ nghĩa riêng từng loại.
- `manifest.json` nhúng `searchText` đầy đủ cho cả 617 màn hình có thể vài MB - chấp nhận được (tương
  tự quy mô JSON ~3MB mà ModuleC đã load ổn định), sẽ đo kích thước thực tế lúc chạy xong bước Xuất
  HTML; nếu quá nặng, cân nhắc cắt bớt `searchText` (chỉ lưu vài trăm ký tự quanh từ khóa) - nhưng
  không tối ưu trước khi có số liệu thật.
- Import 617 file có ảnh nhúng sẽ chậm hơn nhiều so với ~10 file Excel nhỏ của Mcf.DbDef.HtmlGenerator - chấp nhận
  chạy 1 lần rồi cache, không cần streaming/parallel hoá thêm (giữ đơn giản như Mcf.DbDef.HtmlGenerator).

## Đã kiểm chứng thực tế với dữ liệu thật (617 file)

Đã chạy thử toàn bộ pipeline (ngoài UI, qua 1 harness console tạm dùng lại nguyên `Services`/`Models`
của module) trên đúng thư mục `D:\Projects\202607_MCF7\mcf7\02_ユーザ詳細\01_画面説明書`:

- **617/617 file đọc thành công**, không file nào lỗi (lần chạy đầu có 9 file lỗi giữa chừng do hết
  dung lượng ổ đĩa của máy test - không phải lỗi code; chạy lại trên ổ còn trống thì cả 617 đều qua).
  Thời gian nhập: **~1 phút 40 giây** cho cả 617 file (~10 giây/25 file) sau khi thêm crop theo print
  area (mục dưới) - nhanh hơn hẳn so với ~4 phút 15 giây của lần chạy đầu (chưa crop).
- Tổng dung lượng `Data\Database\Html\` (617 file HTML + ảnh): **~650MB** - chấp nhận được cho 1 cache
  cục bộ, không phải gánh nặng đáng kể trên máy hiện đại.
- `searchText` gộp toàn bộ 617 màn hình: **~9.5MB** - `index.html` (nhúng JSON này) tải và
  `JSON.parse` được bình thường trong Edge (đã kiểm chứng bằng cách mở thật, không chỉ suy đoán từ
  quy mô ModuleC) - không cần cắt bớt như phương án dự phòng đã nêu.
- Ảnh chụp `MSBBP1210.html` (file ví dụ user đưa) so với `mcf7_screen.jpg`: đúng cả 6 section
  (表紙/変更来歴/概要/画面イメージ/項目説明/画面遷移), ảnh `画面イメージ` hiện đúng, màu nền xanh lá
  của hàng tiêu đề trong 【オペレーション一覧】 khớp với bản gốc.

**2 phát hiện từ dữ liệu thật, đã sửa vào code (không có trong bản plan ban đầu)**:

1. **Crop theo print area thay vì `sheet.Dimension`** - `sheet.Dimension` (vùng đã dùng) rộng hơn
   nhiều so với nội dung thực sự có ý nghĩa (nhiều cột/dòng định dạng trống ở rìa), tạo ra 1 mảng ô
   trống dày đặc kiểu "giấy kẻ ô" và làm file HTML nặng hơn cần thiết. Sửa: dùng
   `sheet.PrinterSettings.PrintArea` (đúng vùng `_xlnm.Print_Area` mà chính các workbook này đã khai
   báo sẵn cho mục đích in) khi có, fallback về `Dimension` nếu sheet không có print area. Giảm cả
   kích thước file lẫn thời gian render đáng kể (xem số liệu trên).
2. **Hỗ trợ `cell.Style.TextRotation`** - vài cột nhãn hẹp trong `概要`/`変更来歴` dùng chữ xoay dọc
   trong Excel; không xử lý rotation khiến các cột đó hiện thành "1 ký tự mỗi dòng" rất khó đọc (do
   trình duyệt tự ngắt dòng CJK trong cột hẹp). Sửa: bọc nội dung ô trong `<span>` với
   `transform:rotate()` (góc thường) hoặc `writing-mode:vertical-rl` (giá trị đặc biệt `255` của Excel
   = chữ xếp dọc).

**Giới hạn còn lại đã biết, chấp nhận không sửa thêm** (đúng tinh thần "không pixel-perfect" đã chốt):
một số cột nhãn hẹp trong `表紙`/`変更来歴` vẫn hiện hơi rối do chữ Nhật dài bị ngắt dòng giữa ký tự
khi cột đó không có rotation nhưng vẫn hẹp hơn văn bản - đây là đánh đổi cố hữu của renderer chung
(cột hẹp cần "không wrap" nhưng ô văn bản dài như 【画面補足】 lại cần wrap - không thể tối ưu cả 2 mà
không phân biệt ngữ nghĩa từng ô, điều đã cố tình tránh làm).

### Vòng cải thiện layout thứ 2 (sau khi user xem ảnh chụp thực tế)

Sau khi xem ảnh chụp `MSBBP1210.html` render thật (không chỉ đọc code), phát hiện thêm 3 điểm và đã
sửa, không cần đổi kiến trúc:

1. **Ẩn border của ô trống, không tô màu** (`ExcelSheetHtmlRenderer.BuildCellStyle`) - phần lớn cảm
   giác "giấy kẻ ô" còn lại (đặc biệt ở `表紙`) đến từ việc MỌI ô trong print area đều có border dù ô
   đó hoàn toàn trống (không chữ, không màu nền) - chỉ là khoảng đệm layout của Excel. Sửa: ô trống
   + không có `Fill` → border trong suốt (`border-color:transparent`), giữ nguyên khung layout nhưng
   không "vẽ" ra viền giả. Cải thiện rõ rệt nhất trong 3 điểm.
2. **Cắt bớt dòng/cột trống ở cuối vùng print area** (`TrimTrailingEmpty`) - bản thân print area vẫn
   thường rộng hơn nội dung thật (tác giả để dư chỗ). Quét lùi từ mép print area để tìm dòng/cột cuối
   cùng thật sự có chữ hoặc màu nền, cắt xuống đúng đó - heuristic rẻ tiền (không xét origin của merge
   nằm xa hơn), chấp nhận trường hợp hiếm 1 merge lớn bị cắt hụt vài dòng ở cuối thay vì gãy layout.
3. **Bọc mỗi `<table>` trong `<div class="table-scroll">` (overflow-x:auto) + thêm thanh điều hướng
   mục lục dính (`nav.toc`, sticky top)** - bảng `表紙`/`概要` có thể rất nhiều cột (`CH` = 86 cột),
   trước đây làm cả trang bị đẩy rộng ra buộc cuộn ngang toàn trang; giờ chỉ bảng đó tự cuộn ngang
   trong khung của nó, phần còn lại của trang (đoạn văn bản dài, ảnh) đọc bình thường. `nav.toc` liệt
   kê 6 sheet dạng pill, bấm nhảy thẳng tới `<section id="sheet-N">` tương ứng - hữu ích vì 1 trang
   màn hình có thể dài tới hàng chục nghìn dòng.

Đã build lại + chạy thử trên `MSBB_販売管理` (28 file) và chụp ảnh `MSBBP1210.html` để xác nhận trực
quan cả 3 điểm trên thay vì chỉ tin vào code.

### Vòng 3: parse ngữ nghĩa cho 2 sheet 概要/画面遷移 (đổi ý so với quyết định #1 ban đầu)

Sau khi xem bản HTML generic của `概要`/`画面遷移` (bảng dày đặc ô trống, chữ xoay dọc khó đọc), người
dùng yêu cầu đổi sang parse ngữ nghĩa như app web tham khảo (`mcf7-web-develop/front-end/.../document/
screen/{screen-overview,screen-diagram}`) thay vì generic grid. Đã đọc code React tham khảo: nó chỉ
parse ngữ nghĩa đúng 2 sheet này (`概要`, `画面遷移`) - `表紙`/`変更来歴`/`画面イメージ`/`項目説明`
**không** có màn hình tham khảo nào cả (ảnh chụp màn hình trong app web đến từ 1 thư mục ảnh tĩnh
riêng, không phải sheet `画面イメージ`). Vì vậy chỉ 2 sheet này đổi sang semantic renderer; 4 sheet còn
lại (kể cả `項目説明` - sheet phức tạp nhất, lý do ban đầu của quyết định #1) **giữ nguyên generic grid**.

**Khác biệt so với cách app web tham khảo làm**: code React đó đọc 1 chuỗi text đã được BE dump theo
tab (`content.split('\t')`) rồi lấy giá trị theo **offset cột cố định** (vd `arr[2]`, `arr[45..71]`) -
cách này gắn chặt vào format export riêng của BE đó. Mcf.Screen.HtmlGenerator đọc thẳng workbook bằng EPPlus (đã có
`ExcelRange` theo đúng row/col thật), nên thay vì hard-code offset, `OverviewSheetParser`/
`ScreenDiagramSheetParser` **định vị theo tên nhãn** (vd tìm ô có text đúng bằng "処理名" trong dòng
header, không giả định nó luôn ở cột J) - bền hơn với sai khác cột giữa các file, và cho phép "gãy an
toàn": nếu 1 sheet không khớp cấu trúc mong đợi (thiếu marker 【...】 hoặc thiếu 1 header cột), phần đó
tự động rơi về generic grid render (`ExcelSheetHtmlRenderer.RenderGridRange`, refactor từ `RenderSheet`
gốc thành 1 hàm nhận `(SheetGridContext, minRow, maxRow)` dùng chung được cho toàn sheet lẫn 1 khoảng
dòng con) thay vì hiển thị sai hoặc mất nội dung - giữ đúng tinh thần an toàn của quyết định #1 gốc,
chỉ thu hẹp phạm vi generic renderer xuống còn 4/6 sheet thay vì cả 6.

**Bug phát hiện lúc test thật (đã sửa)**: lần chạy đầu, các nhãn phụ dạng cùng ký hiệu 【...】 nằm sâu
trong 1 ô chi tiết (vd `【利用シーン】`/`【補足】` xuất hiện ở cột AT, bên trong dữ liệu chi tiết của mục
【目的】) bị nhận nhầm thành marker cấp cao mới, cắt đứt giữa chừng bảng 【目的】 và tạo ra 1 "section"
rác render generic-grid rất rối (cả khối ô trống xoay dọc). Sửa: marker cấp cao chỉ được nhận diện nếu
nằm ở vùng cột sát lề trái (`minCol..minCol+3` cho 概要, `minCol..minCol+1` cho 画面遷移) - đúng quan
sát thực tế là mọi marker/label thật đều nằm sát cột B/A, còn nhãn phụ dạng 【...】 lồng trong ô dữ liệu
luôn nằm lệch xa về bên phải.

Đã build `Mcf.Screen.HtmlGenerator.csproj` + `AppSuite.sln` sạch, chạy thử qua harness console tạm (ngoài UI, dùng lại
nguyên `Services`) trên 2 file `M7-US-1102_MSBBP1120/1210` (`D:\Projects\202607_MCF7\screen`), chụp ảnh
headless Edge xác nhận trực quan: bảng 【目的】/【オペレーション一覧】 lên đúng đủ cột dữ liệu, khối
`画面遷移` lên đúng dạng card theo từng ID với bảng 遷移元画面項目/遷移先画面項目 và block 復帰処理 lồng
bên trong (khớp bố cục app web tham khảo) - không còn tường ô trống/chữ xoay dọc khó đọc như bản
generic cũ cho 2 sheet này.

**Giới hạn còn lại đã biết, chấp nhận không sửa thêm**: item nào trong bảng 引継項目 mà ô ghi chú tự do
tình cờ rơi đúng cột thứ 2 của bảng (vd `AJ14=操作種別：「登録・訂正する」` trong sample) bị thêm nhầm
thành 1 dòng item với cột nguồn để trống - lỗi hiển thị nhỏ, không mất dữ liệu (vẫn nằm trong
`searchText`), chấp nhận theo đúng tinh thần "không cần chính xác tuyệt đối" đã chốt từ đầu.

### Vòng 4: bỏ sheet 変更来歴, rút gọn 項目説明 thành vài cột

Người dùng yêu cầu thêm 2 điểm sau khi xem bản render vòng 3:

1. **Bỏ qua sheet `変更来歴`** (lịch sử sửa đổi) hoàn toàn - không render section, không có mục trong
   nav, không tính vào `SheetCount`. Đây là sổ ghi chép hành chính (ai sửa dòng nào lúc nào), không cần
   thiết khi đọc để hiểu hành vi màn hình. Sửa 1 chỗ: `ScreenDocImporter.SkippedSheetNames` (`HashSet`
   tên sheet bị bỏ qua), check trong vòng lặp `foreach (var sheet in package.Workbook.Worksheets)` song
   song với check `sheet.Dimension is null` sẵn có.
2. **`項目説明` không cần generic grid đầy đủ (358 merge) - chỉ cần vài cột gọn**: thêm
   `ItemExplanationSheetParser`, khác hẳn 2 parser semantic kia ở chỗ **không cố tái hiện đúng cấu trúc
   nguồn** (không có DTO phân cấp theo group) mà chỉ trích đúng 4 cột người đọc thật sự cần: dấu bắt
   buộc (cột A, ●/○), tên mục (cột B), mô tả (dò quanh cột có header đúng chữ "説明" vì lệch 1 cột giữa
   header và dữ liệu thật quan sát được), kiểu dữ liệu (cột có header đúng chữ "型", ẩn cột này nếu
   group không có). Sheet có 1 quy ước tiện: dòng công bố group mới (操作種別/検索/登録/...) **chính là**
   dòng header cột (luôn có ô "説明" trên cùng dòng đó) - dùng chính dòng đó vừa làm tên group vừa để dò
   lại cột 説明/型 cho group đó (phòng trường hợp lệch cột giữa các group). Bỏ hẳn màu nền/border/merge/
   406 dòng padding của bản gốc - kết quả gọn hơn nhiều, tải nhanh hơn nhiều so với generic grid.
   Không tìm được dòng header "説明" nào → coi như sheet không khớp quy ước, fallback nguyên sheet về
   generic grid (an toàn như 2 parser kia).

Test lại qua harness console + chụp ảnh headless Edge trên `MSBBP1210.xlsx`: nav còn đúng 5 mục (thiếu
`変更来歴`), `項目説明` lên thành các bảng 4 cột theo từng group (操作種別/検索/登録/...), dễ đọc/lướt hơn
hẳn bản lưới ô dày đặc trước đó.

### Vòng 5: dải màu group full-width giống Excel gốc

Sau khi xem lại, người dùng gửi ảnh chụp Excel gốc của sheet `項目説明` (file `MSBBP1120`) và hỏi vì sao
bản HTML không giống layout. Đối chiếu dữ liệu bằng dump trực tiếp workbook: **dữ liệu khớp 100%** với
Excel gốc kể cả các dòng "説明" để trống (nhóm `検索` gồm các field lọc như 見積日ＦＲＯＭ vốn dĩ không có
mô tả ngay trong Excel - không phải lỗi thiếu dữ liệu). Hỏi lại để làm rõ, người dùng xác nhận đây là
vấn đề **style**: Excel gốc vẽ mỗi dòng tiêu đề group (`操作種別`/`検索`/...) thành 1 dải nền xanh chạy
full-width cả bảng, trong khi bản trước đó tách tên group ra thành `<h3>` riêng phía trên 1 bảng con
theo từng group (nhìn tách rời, không giống Excel).

Sửa: `ItemExplanationSheetParser` đổi từ "1 bảng con cho mỗi group" sang **1 bảng `<table>` liên tục cho
cả sheet**, mỗi group chèn 1 `<tr class="item-group-row"><td colspan="N">...` (dải nền xanh chạy hết bề
rộng bảng, giống hệt cách Excel băng màu cả dòng group). Vì colspan phải khớp số cột thống nhất cho cả
bảng (không thể đổi số cột giữa các group), số cột được tính **1 lần cho toàn sheet** (có cột `型` nếu
BẤT KỲ group nào trong sheet có `型`, thay vì tính riêng theo từng group như bản trước). CSS thêm
`.item-group-row`/`.item-table thead th` dùng tông xanh lá giống Excel (khác tông xanh dương `--accent`
dùng cho `概要`/`画面遷移` để không đổi style đã được chấp nhận ở 2 sheet kia).

### Vòng 6: thêm đủ 6 cột cờ リ/型/参/コ/入/非/O

Người dùng gửi ảnh chụp đúng dải header 7 ký tự `リ 型 参 コ 入 非 O` trong Excel, yêu cầu giữ đủ các cột
này thay vì chỉ giữ mỗi `型` như vòng 3-5. Sửa `ItemExplanationSheetParser`: thay `TypeCol` (1 cột) bằng
mảng `FlagLabels = ["リ","型","参","コ","入","非","O"]` dò cột tương tự `型` trước đó (so khớp đúng text
trên dòng header của từng group); cột nào có ở BẤT KỲ group nào trong sheet thì xuất hiện xuyên suốt cả
bảng (đồng nhất số cột/colspan như đã làm ở vòng 5), group nào thiếu cột đó thì để trống ô tương ứng.

### Vòng 7: bỏ sheet 表紙, bỏ khối header lặp lại ở mỗi sheet

Người dùng gửi ảnh chụp khối bảng header (mcframe 7/文書名/モジュールID/.../文書番号/Version/Rev.) lặp
lại y hệt ở đầu mọi sheet, yêu cầu bỏ 2 thứ:

1. **Bỏ hẳn sheet `表紙`** - giống cách đã bỏ `変更来歴` ở vòng 4: thêm `"表紙"` vào
   `ScreenDocImporter.SkippedSheetNames`. Hợp lý vì nội dung `表紙` (mcframe/module/文書番号/Version/Rev.)
   đã hiển thị sẵn trong phần chip header của trang (`Van ban`/`Revision`/`Nguon`).
2. **Bỏ khối header lặp lại ở đầu MỖI sheet còn lại** (`概要`/`項目説明`/`画面遷移` đều có cùng 1 khối
   bảng metadata y hệt nhau ở vài dòng đầu, trước marker/group header đầu tiên) - trước đó 3 semantic
   parser đều gọi `RenderGridRange` cho đoạn `[minRow, marker đầu tiên - 1]` này; giờ bỏ hẳn lời gọi đó,
   không render đoạn header lặp lại nữa (không cần giữ lại vì cùng lý do - đã có ở chip header trang).

Test lại: nav còn đúng 4 mục (`概要`/`画面イメージ`/`項目説明`/`画面遷移`), mỗi sheet nhảy thẳng vào nội
dung thật (vd `概要` bắt đầu ngay từ 【説明】) không còn khối bảng mcframe lặp lại phía trên.

### Vòng 8: sheet `画面イメージ` vẫn còn header + mở màn hình ngay trong index thay vì tab mới

Người dùng gửi ảnh cho thấy `画面イメージ` (không phải 1 trong 3 sheet có semantic parser) vẫn còn dính
khối bảng mcframe - vì vòng 7 chỉ bỏ header ở "trước marker/group đầu tiên" bên trong 3 parser semantic,
chưa xử lý sheet nào đi thẳng qua nhánh **generic grid toàn sheet** (`画面イメージ`, và bất kỳ sheet nào
3 parser kia fallback toàn bộ). Sửa tại đúng 1 chỗ dùng chung cho mọi sheet: thêm
`ExcelSheetHtmlRenderer.SkipDocumentHeaderBlock` gọi ngay sau `TrimTrailingEmpty` trong `RenderSheet`
(trước khi rẽ nhánh semantic/generic) - nhận diện khối header bằng neo cố định "ô đầu tiên đúng bằng
chữ `mcframe 7`", nếu khớp thì nhảy qua 3 dòng bảng + dòng trống + 1 dòng tiêu đề lặp lại đúng tên sheet
(`sheet.Name`) + dòng trống kế tiếp; không khớp neo thì giữ nguyên `minRow` (an toàn, không cắt nhầm
sheet có cấu trúc khác). Áp dụng đồng nhất cho cả nhánh semantic lẫn generic vì `minRow` được chỉnh
trước khi tạo `SheetGridContext`/gọi bất kỳ parser nào.

**Mở màn hình ngay trong trang `index.html` thay vì tab mới**: đổi link kết quả tìm kiếm từ
`target="_blank"` sang `target="contentFrame"` trỏ vào 1 `<iframe name="contentFrame">` nằm ngay trong
khu `#content` (thay cho đoạn text hướng dẫn tĩnh trước đó - giờ đoạn hướng dẫn nằm trong
`#contentPlaceholder`, ẩn đi khi đã chọn 1 màn hình). Đây là cơ chế HTML thuần (`<a target>` khớp
`<iframe name>`) - trình duyệt tự điều hướng iframe khi click, không cần `fetch()`/JS phức tạp nên
**không đụng tới vấn đề CORS** đã né tránh trước đó (khác hẳn việc `fetch()` nội dung HTML của trang con
qua JS, thứ chắc chắn bị chặn CORS trên `file://` - đây vẫn chỉ là 1 navigation bình thường của khung
`<iframe>`, được phép). Thêm class `active` tô đậm mục đang xem trong sidebar. Nút "← Quay lai danh
sach" trong trang từng màn hình đổi `target="_parent"` để bấm từ trong iframe thì nạp lại đúng
`index.html` ở cấp cao nhất thay vì lồng `index.html` vào trong chính iframe của nó.

Test lại: chụp ảnh xác nhận `画面イメージ` hết header lặp; giả lập iframe đã nạp sẵn 1 màn hình xác nhận
layout 2 cột (sidebar trái + nội dung phải) hiển thị đúng, đầy đủ nav/back-link của trang con.

## Kiểm chứng

1. `dotnet build Modules\Mcf.Screen.HtmlGenerator\Mcf.Screen.HtmlGenerator.csproj -p:Platform=x64` - build sạch.
2. Thiet lap thu muc nguon trỏ vào `D:\Projects\202607_MCF7\mcf7\02_ユーザ詳細\01_画面説明書`, bấm
   "1. Doc Excel → SQLite" - log chạy hết ~617 file, không dừng giữa chừng dù có file lỗi; kiểm tra
   `SELECT COUNT(*) FROM Screens` khớp số file `.xlsx` (trừ file lỗi có log cảnh báo).
3. "2. Xuat HTML" → "Mo file HTML" - xác nhận với riêng file mẫu `M7-US-1102_MSBBP1210_..." (đã dùng
   làm ví dụ): mở `MSBBP1210.html` từ index, so sánh trực quan với `mcf7_screen.jpg` (đủ mọi section,
   ảnh chụp màn hình hiện đúng, bảng `項目説明` không vỡ layout dù nhiều merge).
4. Gõ vào ô "Ma man hinh": `MSBBP1210` → chỉ còn đúng 1 kết quả. Gõ vào ô "Noi dung" 1 cụm từ biết
   chắc có trong 1 màn hình cụ thể (vd 1 dòng trong 【画面補足】) → kết quả lọc đúng, kết hợp AND với ô
   mã màn hình hoạt động đúng.
5. `dotnet build AppSuite.sln` - Mcf.Screen.HtmlGenerator không ảnh hưởng ModuleA-F/MainLauncher/Common.
