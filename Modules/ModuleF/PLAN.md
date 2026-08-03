# ModuleF: CSV/TSV Editor cho AppSuite

## Bối cảnh

AppSuite cần 1 công cụ mở/sửa nhanh file CSV/TSV (dữ liệu import/export nội bộ) ngay trong bộ ứng
dụng, không cần mở Excel. Theo đúng luật kiến trúc của repo (`README.md` gốc,
`.claude\skills\appsuite-dev\SKILL.md`), đây là **1 module mới độc lập "ModuleF"**, chỉ
`ProjectReference` tới `Common\Common.csproj`, không đụng `MainLauncher`.

**Lệch với `SKILL.md` mục Coding convention**: SKILL.md nói cả solution dùng DI qua
`Microsoft.Extensions.DependencyInjection`, nhưng thực tế ModuleA-E đều **không** dùng DI container
(chỉ MainLauncher dùng) - ViewModel/Service được `new` thủ công. ModuleF theo đúng convention thực tế
của Module (constructor injection thủ công trong `MainWindow` - xem mục Thiết kế).

**3 quyết định phạm vi đã chốt với người dùng trước khi code** (không phải mặc định tự chọn):
1. Control bảng: **`CommunityToolkit.WinUI.UI.Controls.DataGrid`** (NuGet `CommunityToolkit.WinUI.UI.Controls.DataGrid`
   7.1.2 - xác nhận đúng package ID qua NuGet search lúc code, vì tên "CommunityToolkit.WinUI.Controls.DataGrid"
   đoán ban đầu không tồn tại). Dependency DataGrid đầu tiên trong repo.
2. File lớn (>1GB): đọc **bất đồng bộ + progress + CancellationToken**, nhưng **nạp toàn bộ vào RAM**
   sau khi đọc xong - vì edit/sort/filter/undo cần random-access. Có cảnh báo ngưỡng số dòng ước tính
   quá lớn (3 triệu dòng) trước khi đọc thật.
3. **Bỏ unit test** (repo chưa có project test nào) - chỉ có checklist kiểm chứng thủ công.

## Thiết kế

### 1. Data model

- `Models\CsvColumn.cs`: `Name` mutable (RenameColumn sửa tại đây).
- `Models\CsvRow.cs`: cell truy cập qua **indexer** `this[int]` (ủy quyền cho `GetCell`/`SetCell`) để
  số cột đổi runtime mà không cần đổi kiểu item. `SetCell` raise `PropertyChanged("Item[i]")` cho
  đúng 1 cell; `SetCellSilent` (không raise event, dùng cho mutation hàng loạt trên background
  thread); `InsertCellAt`/`RemoveCellAt` raise refresh cả hàng (`PropertyChanged(null)`), dùng cho
  add/remove cột.
- `Models\CsvDocument.cs`: `Columns` + `Rows` (`ObservableCollection<CsvRow>`, nguồn cho DataGrid).
  `ReplaceAll()` thay thế toàn bộ 1 lần (dựng `List<CsvRow>` xong mới bọc `ObservableCollection` mới,
  không `Clear()+Add()` lặp) - tránh N sự kiện `CollectionChanged` khi mở file lớn.
  `RaiseStructureChanged()` để các Command cấu trúc (add/remove/rename column) báo 1 lần duy nhất cho
  ViewModel rebuild cột DataGrid, thay vì mỗi row tự raise riêng.

**Binding cột động vào DataGrid** (`MainWindow.xaml.cs: RebuildColumns()`): mỗi cột dựng 1
`DataGridTextColumn` với `Binding.Path = new PropertyPath($"[{i}]")`, **`Mode = BindingMode.OneWay`**
(cố ý, không TwoWay) - xác nhận qua đọc metadata thật của DLL package (không đoán) rằng
`DataGridBoundColumn.Binding`/`PropertyPath` tồn tại đúng như kỳ vọng. Lý do không dùng TwoWay: nếu
để TwoWay, DataGrid sẽ tự ghi giá trị mới thẳng vào `CsvRow` qua indexer setter **trước khi** code có
cơ hội tạo `SetCellCommand` - phá Undo/Redo hoàn toàn (sửa xong không undo được, và nếu vẫn gọi
`SetCell` sau đó thì `oldValue == newValue` nên bị coi là no-op). Giải pháp: Binding chỉ OneWay để
hiển thị, còn việc **ghi** đi qua `Grid_CellEditEnding` (không phải `CellEditEnded` - lúc đó ô đã
thoát chế độ sửa, `Column.GetCellContent(Row)` không còn là TextBox nữa) đọc `TextBox.Text` từ
`e.Column.GetCellContent(e.Row)`, rồi gọi `ViewModel.SetCell(viewRowIndex, columnIndex, text)` -
chính là nơi tạo `SetCellCommand` và đẩy qua `UndoRedoStack`. Vì `CsvRow` vẫn raise
`PropertyChanged("Item[i]")` khi `SetCell` chạy, binding OneWay vẫn tự cập nhật hiển thị ngay sau đó -
không cần code thêm gì để refresh cell.

**`SetCell` nhận thẳng `CsvRow` thay vì index** (`ICsvEditService.SetCell(CsvRow row, int columnIndex, string value)`):
quyết định đổi so với bản nháp ban đầu (vốn định nhận `rowIndex`) - vì DataGrid event
(`e.Row.GetIndex()`) chỉ cho index trong `ViewRows` (danh sách đã lọc/sắp xếp), còn Command cần thao
tác trên đúng instance `CsvRow`; tra `Document.Rows.IndexOf(row)` để đổi qua "index tài liệu" là
`O(n)` - với file hàng triệu dòng, làm việc này *mỗi lần gõ phím* sẽ chậm dần. `CsvRow` tự nó đã đủ
để `SetCellCommand` thao tác (`row.SetCell(...)`), nên bỏ hẳn bước tra index.

### 2. Search / Filter / Sort - view-state, không vào Undo/Redo

`ModuleFViewModel.ViewRows` (không phải `Document.Rows`) là thứ DataGrid bind vào - chứa cùng
**instance** `CsvRow` với tài liệu gốc (không clone), nên sửa 1 ô đang hiển thị trong view đã
lọc/sắp xếp vẫn tạo đúng `SetCellCommand` trên dữ liệu thật. `RebuildView()` (LINQ `Where` rồi
`OrderBy.ThenBy`) chạy lại sau mỗi lần Apply Filter/Sort và sau mỗi Command cấu trúc.

- **Filter**: đơn giản hoá so với bản thiết kế "cây AND/OR/NOT lồng nhau" ban đầu - `FilterExpression`
  là **1 danh sách phẳng** `FilterCondition` + **1 phép kết hợp chung** (`AND` hoặc `OR` cho toàn bộ
  danh sách), mỗi điều kiện có cờ `Negate` (NOT) riêng. Lý do đơn giản hoá: dựng UI cho cây biểu thức
  lồng nhau (nhóm con, ngoặc) là 1 khối lượng công việc UI lớn không tương xứng với lợi ích cho 1 tool
  nội bộ - danh sách phẳng + AND/OR/NOT vẫn phủ được hầu hết nhu cầu lọc thực tế.
- **Find**: debounce 300ms (`DispatcherQueueTimer`), so khớp trên `ViewRows` hiện tại (đúng cái user
  đang thấy). Điều hướng kết quả **chọn dòng+cột** trong DataGrid thay vì tô màu multi-cell overlay -
  `CommunityToolkit.WinUI.UI.Controls.DataGrid` 7.1.2 không có `ScrollIntoView(item, column)` public
  (xác nhận qua đọc metadata DLL, không có trong bản này) nên không đảm bảo cuộn tới đúng vị trí với
  bảng rất dài - hạn chế đã biết, chấp nhận được cho 1 tool nội bộ.
- **Sort**: `IComparer<CsvRow>` gộp nhiều cột qua `OrderBy(...).ThenBy(...)`, số ưu tiên số nếu 2 vế
  parse được `double`, ngược lại so chuỗi.

### 3. Undo/Redo - Command Pattern

`IEditCommand { Execute(); Undo(); Description; }`, `UndoRedoStack` 2 `Stack<IEditCommand>`. Danh sách
command: `SetCellCommand`, `AddRowCommand` (+ `DuplicateRowCommand` kế thừa, chỉ đổi index/description),
`RemoveRowCommand` (snapshot giá trị dòng lúc xóa để Undo khôi phục đúng), `AddColumnCommand`/
`RemoveColumnCommand` (snapshot **toàn bộ giá trị mọi dòng tại cột đó** lúc xóa, mutation hàng loạt
không raise `PropertyChanged` per-row - chỉ raise `StructureChanged` 1 lần), `RenameColumnCommand`,
`PasteCommand` (gói nhiều `SetCellCommand` con, Undo/Redo cả khối cùng lúc).
`ICsvEditService` là nơi duy nhất tạo Command - ViewModel chỉ gọi hành vi nghiệp vụ (SRP).

### 4. Encoding/Delimiter detection

- Encoding: đọc preamble 4 byte (BOM UTF-8/UTF-16 LE/BE); không BOM thì thử decode UTF-8 nghiêm ngặt
  8KB đầu, lỗi thì kiểm tra mẫu byte `0x00` xen kẽ (nghi UTF-16 no-BOM) trước khi fallback UTF-8.
- Delimiter: tokenize (quote-aware) 20 dòng đầu cho 4 ứng viên `, \t ; |`, chọn theo
  `score = (Mode(count) - 1) * (1 - variance)`; file `.tsv` ưu tiên thử Tab trước.
- Cả 2 đều có thể bị người dùng ghi đè qua `EncodingPickerDialog` (bấm vào nhãn Encoding/Delimiter ở
  status bar để mở lại file với lựa chọn thủ công).

### 5. Cảnh báo file lớn & Validation & Statistics

- Ước tính số dòng = `FileInfo.Length / (bytes đã đọc cho 200 dòng mẫu / 200)`; vượt 3 triệu dòng thì
  hỏi xác nhận (`ContentDialog`) trước khi đọc toàn bộ.
- `ValidationService` chạy 1 lần sau Open (không chặn mở file): field-count lệch header và lỗi quote
  được gom ngay trong lúc `CsvParser` stream (rẻ, đã duyệt qua rồi); tỉ lệ ô rỗng/cột > 50% tính riêng
  sau khi có `CsvDocument` đầy đủ.
- `StatisticsService` chạy **on-demand** (nút Statistics), không chạy nền liên tục - `Task.Run` +
  `IProgress<int>`, Duplicate row qua `HashSet<string>` nối cell bằng ký tự điều khiển `Chr(1)` (khó
  trùng với dữ liệu thật hơn dấu phẩy/tab).

### 6. Gắn vào solution

`AppSuite.sln` (project `ModuleF` GUID `34DBF542-F78F-4E48-8C17-40626EF024CF`),
`MainLauncher\Config\modules.json`, `build\Sync-Modules-Dev.ps1`, `build\Publish-AppSuite.ps1`, root
`README.md` (cây thư mục + danh sách module) - đúng 4+1 điểm sửa như ModuleD/E đã làm.

## Rủi ro/giả định chưa kiểm chứng bằng cách chạy thật

Toàn bộ thiết kế trên đã build sạch (`dotnet build`), nhưng hành vi runtime của binding indexer
(`Path="[i]"` + `Mode=OneWay` + refresh qua `PropertyChanged("Item[i]")`) và
`DataGridTextColumn.GetCellContent` trong `CellEditEnding` **chưa được xác nhận bằng cách mở app thật
và gõ thử** - đây là hành vi tiêu chuẩn của binding engine WPF/UWP/WinUI (không phải suy đoán tùy
tiện) nhưng nên kiểm chứng ở buổi test đầu tiên (mục 3 phần Kiểm chứng).

## Kiểm chứng

1. `dotnet build Modules\ModuleF\ModuleF.csproj -p:Platform=x64` - build sạch.
2. Mở file CSV có BOM, không BOM, TSV, delimiter `;`/`|` - auto-detect đúng; bấm nhãn Encoding/
   Delimiter ở status bar để mở lại với lựa chọn thủ công, xác nhận override hoạt động.
3. Gõ sửa 1 ô, Tab/Enter để commit - xác nhận: (a) giá trị hiển thị đúng ngay sau khi rời ô, (b) nút
   Undo bật lên và hoàn tác đúng về giá trị cũ, (c) Redo trả lại giá trị mới.
4. Thêm/xóa dòng, thêm/xóa/đổi tên cột - Undo/Redo từng bước.
5. Xóa 1 cột giữa bảng có dữ liệu → Undo → xác nhận cột khôi phục đúng vị trí + đúng giá trị từng
   dòng.
6. Filter (thử cả AND và OR, thử NOT) + Sort đa cột đồng thời, sửa 1 ô trong view đã lọc → Undo hoạt
   động đúng, bỏ filter thấy dữ liệu gốc đã đổi.
7. Find với cả 5 chế độ so khớp (đặc biệt Regex), Trước/Sau điều hướng đúng; Replace All.
8. Statistics trên file vài trăm nghìn dòng - không treo UI, số liệu Min/Max/Avg/Sum đúng với cột số.
9. Mở file cố ý lỗi (thiếu field 1 số dòng, nhiều ô rỗng, 1 dòng quote không đóng) - khu "Cảnh báo"
   hiện đúng, không chặn mở.
10. Mở file rất lớn (giả lập hoặc thật, >3 triệu dòng ước tính) - dialog cảnh báo hiện ra; bấm Hủy khi
    đang đọc hoạt động đúng (không crash, file handle đóng).
11. Save/Save As - mở lại bằng editor khác, xác nhận nội dung/delimiter/encoding đúng như status bar
    lúc lưu.
12. Context menu: Copy nhiều dòng đã chọn dán được vào Excel (TSV), Paste nhiều ô, Delete, Insert Row,
    Duplicate Row.
13. `dotnet build AppSuite.sln` - ModuleF không ảnh hưởng project khác; `Sync-Modules-Dev.ps1` copy
    đúng `ModuleF.exe` cạnh `MainLauncher.exe`.
