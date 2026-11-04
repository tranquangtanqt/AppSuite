# CsvEditor

Ứng dụng WinUI 3 độc lập - trình soạn thảo CSV/TSV đầy đủ (mở, sửa trực tiếp trên bảng, thêm/xóa
dòng/cột, đổi tên cột, tìm kiếm, lọc, sắp xếp, thống kê, Undo/Redo). Không có bất kỳ tham chiếu nào
tới `MainLauncher`; chỉ `ProjectReference` tới `..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\CsvEditor\CsvEditor.csproj
```

hoặc mở `CsvEditor.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Build/publish riêng CsvEditor (không build cả AppSuite)

Kiểm tra lỗi biên dịch nhanh (không publish, không copy file):

```powershell
dotnet build Modules\CsvEditor\CsvEditor.csproj -c Release
```

Publish đúng layout deploy (`Application\Modules\CsvEditor\...`) mà không đụng tới MainLauncher/7
module còn lại - `build\Publish-AppSuite.ps1` hỗ trợ sẵn `-Targets`:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\Publish-AppSuite.ps1 -Configuration Release -Runtime win-x64 -Targets CsvEditor
```

## Toolbar

Alt hiện chữ tắt (`AccessKey`): M Mở, G File gần đây, L Lưu, X Xuất, H Hoàn tác, A Làm lại, D Dòng, C Cột, T Tìm, I Đi tới dòng, O Lọc, S Sắp xếp, K Thống kê, V Xem dọc, N Hướng dẫn. Mọi nút và ô lọc có `AutomationProperties.Name`.

Cửa sổ hẹp hơn 1040 logical (`RootGrid_SizeChanged`): Xuất, Dòng, Cột, Tìm, Lọc, Sắp xếp chỉ còn icon + tooltip.

`Mở` `🕘` `Lưu` `Xuất ▾` | `↶` `↷` | `Dòng ▾` `Cột ▾` | `Tìm` `→` `Lọc` `Sắp xếp` `📊` `▤` … `?` (giao diện làm lại
2026-11-03: tiếng Việt, việc ít dùng gom vào menu thả xuống - vừa 1 hàng ở cửa sổ ~1000 px). Nút có phím tắt đều nằm ngoài
menu thả xuống (phím tắt của mục trong menu chỉ chạy khi menu đang mở). Tiêu đề cửa sổ = tên file (`● ` khi chưa lưu).

- `Xuất ▾`: *Lưu thành file khác...*, *Xuất Excel (.xlsx)...*. `Dòng ▾`: *Thêm dòng*, *Nhân bản dòng đang chọn*, *Xoá dòng
  đang chọn*. `Cột ▾`: *Đi tới cột...* (`Ctrl+Shift+G`), *Ẩn / hiện cột...*, *Cố định đến cột đang chọn*, *Bỏ cố định cột*, *Thêm cột...*, *Đổi tên cột đang chọn...*, *Xoá cột đang chọn* (chưa chọn ô nào → thanh trạng thái
  nhắc). `→` = Đi tới dòng, `📊` = Thống kê, `▤` = Xem dọc (`Ctrl+R`), `↶ ↷` = Hoàn tác / Làm lại (tooltip ghi thao tác sẽ hoàn tác).
- **Màn hình bắt đầu** (lúc chưa mở file / chưa có cột - `UpdateStartScreen`): vùng kéo-thả viền nét đứt, *Mở file...*,
  *Bảng trống* (ẩn màn hình rồi mở hộp thoại Thêm cột) và danh sách file gần đây (bấm 1 dòng để mở).
- **Thẻ đang lọc / sắp xếp** (`ChipsBar`, `RefreshChips`) phía trên bảng: `Hiện x / y dòng`, `Lọc: Trạng thái = "Đã giao" ✕`
  (lọc nâng cao), `Ô lọc: Tên chứa "an" ✕` (các ô lọc ở tiêu đề cột), `Sắp xếp: Thành tiền ↓ ✕`. Bấm chữ = mở lại hộp
  thoại, ✕ = bỏ. Nhiều thẻ tự xuống hàng (`Views/WrapPanel`, vị trí tính ở `Services/WrapLayout`). Lọc ra 0 dòng: giữa bảng hiện `NoRowsPanel` kèm nút *Bỏ lọc*
  (bỏ cả lọc nâng cao lẫn ô lọc). Thay cho nút Clear Filter / Clear Sort và nút ✕ ở góc trên-trái bảng (bỏ).
- **Ô có xuống dòng** (2026-11-04): dòng tự cao theo nội dung; sửa ô: Shift+Enter xuống dòng ở mọi ô, Enter = ghi (`Grid_PreparingCellForEdit`, `CellEditor_PreviewKeyDown`), `Services/CellText` trả lại kiểu xuống dòng gốc vì TextBox đổi hết thành `\r`. Trước đây ô nhập 1 dòng cắt mất các dòng sau, F2 + Enter làm mất dữ liệu.
- **Bảng** (2026-11-04): dòng gọn thấp nhất 26 logical (`MinHeight` của ô, bỏ `MinHeight` của dòng / `DataGridCellsPresenter`; không đặt `RowHeight` cố định để ô có xuống dòng tự cao lên), kẻ ô, vạch ngăn cột cố định (`FrozenDivider`), số giữ canh trái; `Cột ▾` → *Hiện ô lọc dưới tên cột* ẩn / hiện hàng ô lọc (không nhớ qua lần mở app sau).
- **Hướng dẫn** (nút `?` cuối thanh công cụ, `F1`): mọi tính năng theo danh mục + tìm không dấu. Nội dung ở
  `Views/HelpContent.cs` (cửa sổ dùng chung `SharedUI.Help.HelpWindow`) — thêm / đổi tính năng thì cập nhật cả ở đó.

- **Mở** (`Ctrl+O`): chọn `.csv`/`.tsv`/`.txt`, tự nhận diện delimiter (`,` `Tab` `;` `|`) và encoding
  (UTF-8, UTF-8 BOM, UTF-16 LE/BE, **Shift-JIS** - file không phải UTF-8 hợp lệ mà đọc trọn vẹn được theo Shift-JIS /
  CP932; có kana hoặc 2 kanji liền nhau thì không cảnh báo, không thì kèm 1 cảnh báo "đoán là Shift-JIS"; lưu lại giữ
  nguyên Shift-JIS). Nếu nhận diện sai, bấm nút bảng mã · dấu phân cách ở thanh
  trạng thái để mở lại file với lựa chọn thủ công. Mở lỗi (file bị khoá, không có quyền…) hiện hộp thoại báo lỗi.
- **Kéo-thả**: thả file `.csv`/`.tsv`/`.txt` vào bất kỳ chỗ nào trong cửa sổ để mở (thả nhiều file → mở file đầu
  tiên). Còn thay đổi chưa lưu thì hỏi như Open.
- **File gần đây** (nút 🕘 cạnh Mở, và danh sách ở màn hình bắt đầu): 10 file mở / Save As gần nhất, mới nhất trước; trùng tên thì kèm tên thư mục, di
  chuột thấy đường dẫn đầy đủ. File đã bị xoá / chuyển chỗ → báo và tự bỏ khỏi danh sách; *Xoá danh sách* ở cuối menu.
  Lưu ở `Data\Config\recent-files.json` cạnh exe.
- **Dòng đầu là tiêu đề** (checkbox ở thanh trạng thái cạnh encoding / delimiter - cùng nhóm "cách đọc file"; mặc định có tick — người dùng tự chọn, không tự đoán): bỏ tick cho
  file không có dòng tên cột → mọi dòng là dữ liệu, cột tự đặt tên `Cột 1`, `Cột 2`… (số cột = dòng dài nhất, không
  cắt field nào); tên này chỉ để hiển thị — Save không ghi dòng tên cột. Đổi lựa chọn khi đang mở file → mở lại file đó
  (giữ encoding / delimiter; có thay đổi chưa lưu thì hỏi trước).
- **Save / Save As**: ghi lại đúng delimiter/encoding đang dùng. Hotkey: `Ctrl+S` (Save). Còn thay đổi chưa lưu mà **đóng cửa sổ**,
  **Open** file khác hoặc **mở lại** file (đổi encoding / delimiter / "Dòng đầu là tiêu đề") → hỏi *Lưu* / *Không lưu* /
  *Hủy* (Lưu khi chưa có file → Save As; huỷ Save As hoặc lưu lỗi → dừng lại, không mất thay đổi). Lưu lỗi (file đang bị
  app khác khoá…) hiện hộp thoại báo lỗi.
- **Export .xlsx**: xuất **các dòng đang hiện** (theo lọc / sắp xếp hiện tại) ra 1 sheet Excel; file CSV đang mở và
  trạng thái đã lưu không đổi. Ô trông như số (không có số 0 ở đầu, ≤ 15 chữ số) ghi thành số; còn lại ghi thành chữ —
  mã `00123`, số 16 chữ số, `1e5` giữ nguyên, ô bắt đầu bằng `=` không thành công thức. Dòng tiêu đề in đậm, cố định khi
  cuộn, có nút lọc (file không có dòng tiêu đề thì chỉ ghi dữ liệu). Vượt giới hạn Excel (1.048.576 dòng, 16.384 cột,
  32.767 ký tự / ô) → bỏ / cắt phần dư và báo ở thanh trạng thái. Ghi ra file tạm rồi mới thay file đích.
- **Dòng dài hơn tiêu đề**: tự thêm cột `Cột N` cho phần dư (kèm 1 cảnh báo) để không mất field khi lưu; tên cột mới được
  ghi vào dòng tiêu đề khi Save (đổi tên bằng Rename Column).
- **Undo/Redo**: áp dụng cho sửa ô, thêm/xóa dòng, thêm/xóa/đổi tên cột, dán nhiều ô, kéo điền
  (fill handle), thay thế tất cả kết quả Find/Replace (mỗi thao tác trên là **1** bước Undo dù đổi
  nhiều ô/dòng cùng lúc) - **không** áp dụng cho Filter/Sort (2 cái này chỉ là cách hiển thị, không
  đổi dữ liệu gốc). Hotkey: `Ctrl+Z` (Undo), `Ctrl+Y` hoặc `Ctrl+Shift+Z` (Redo).
- **Add/Delete/Copy Row**: Add/Copy chèn dòng mới ngay sau dòng đang chọn (hoặc cuối bảng nếu
  không chọn gì); Delete xóa mọi dòng đang chọn (chọn nhiều dòng được, `SelectionMode="Extended"`).
  Sau mỗi thao tác, selection tự chuyển sang dòng liên quan (dòng mới/dòng vừa copy/dòng thế chỗ
  dòng bị xóa) thay vì mất selection.
- **Add/Delete/Rename Column**: theo cột đang focus trong bảng (`CurrentColumn`).
- **Ẩn / hiện cột, cố định cột** (2026-11-03): chuột phải tiêu đề → *Ẩn cột này* / *Cố định cột 1 – N (đến cột này)* / *Bỏ
  cố định cột*; `Cột ▾` → *Ẩn / hiện cột...* (hộp thoại tick từng cột, ô tìm, Hiện tất cả / Bỏ tick tất cả theo cột đang
  thấy, phải còn ≥ 1 cột hiện). Chỉ là cách xem (`DataGridColumn.Visibility`, `DataGrid.FrozenColumnCount`): lưu, xuất .xlsx,
  khung Xem dọc vẫn đủ cột. Thẻ `Đang ẩn N cột: … ✕` (✕ = hiện tất cả) và `Cố định N cột đầu (đến …) ✕`. Ẩn nhớ theo tên cột
  (thêm / xoá cột không lệch), cố định theo số cột đầu; mở file khác thì bỏ cả 2 (`_columnViewPath`). *Đi tới cột* tới 1 cột
  đang ẩn thì hiện cột đó.
- **Xem dọc 1 dòng** (`▤` / `Ctrl+R` / chuột phải ô → *Xem dọc dòng này*, 2026-11-03): khung 400 px bên phải bảng (bảng
  chừa lề phải) liệt kê dòng đang chọn thành `số. tên cột | giá trị` - đọc 1 bản ghi file nhiều cột (vd file IF 123 cột)
  không phải cuộn ngang. Ô *Tìm cột hoặc giá trị* lọc danh sách (không phân biệt hoa thường). Sửa ngay trong khung: rời ô
  hoặc `Enter` ghi qua `SetCell` (1 bước Hoàn tác, bảng cập nhật), `Shift+Enter` xuống dòng trong ô, `Esc` trả lại giá trị
  cũ; Hoàn tác / Làm lại cập nhật lại khung. `˄ ˅` sang dòng trên / dưới; khung theo dòng đang chọn trên bảng (giữ tham
  chiếu dòng nên lọc / sắp xếp không lẫn dòng). Mục của khung: `ViewModels/RecordField`.
- **Fill handle**: chọn 1 ô, bấm-giữ-kéo ô vuông nhỏ ở góc dưới-phải xuống các dòng dưới để điền
  y nguyên giá trị; giữ `Ctrl` khi thả chuột để tự tăng dần (+1 mỗi dòng) nếu giá trị là số nguyên.
  Chỉ hỗ trợ kéo xuống, không tự cuộn khi kéo ra ngoài vùng đang hiển thị.
- **Find** (`Views/FindBar`, thanh trên bảng, không chặn bảng): 5 kiểu so khớp (Chứa / Khớp toàn bộ ô / Bắt đầu bằng / Kết thúc bằng / Regex), có debounce 300ms khi gõ,
  Enter / Shift+Enter đi giữa các kết quả (tự cuộn dọc+ngang tới đúng ô khớp), Esc đóng. ViewModel nhớ điều kiện tìm và tính lại mỗi khi bảng đổi; bỏ qua cột đang ẩn (`SetHiddenColumns`); regex dở báo "Regex không hợp lệ.". Hotkey: `Ctrl+F`.
- **Replace**: ô Thay bằng trên thanh tìm, "Thay thế tất cả" chỉ thay phần chữ khớp (`SearchService.CreateReplacer`; Khớp toàn bộ ô thì thay cả ô, Regex dùng được `$1`),
  gộp thành 1 bước Undo. Hotkey: `Ctrl+H`.
- **Đi tới cột** (`Cột ▾` / `Ctrl+Shift+G`, 2026-11-03): hộp thoại AutoSuggestBox - gõ 1 phần tên cột, gợi ý `số. tên`
  (tối đa 30) theo `Services/ColumnFinder`: trùng hẳn tên → vị trí cột nếu gõ toàn số → tên bắt đầu bằng → tên chứa (không
  phân biệt hoa thường). Chọn gợi ý (chuột / ↓ Enter) hoặc Enter = cột khớp nhất; không khớp → báo trong hộp thoại. Cuộn ngang
  tới cột, giữ dòng đang chọn; thanh trạng thái ghi `Cột x/y: tên` (+ số cột khác cũng khớp). Phím tắt gắn ở `RootGrid`
  (mục trong menu thả xuống không nhận phím tắt khi menu đóng), `KeyboardAcceleratorPlacementMode="Hidden"` để không nổi
  tooltip "Ctrl+Shift+G" khi rê chuột.
- **Go To** (`Ctrl+G`): đi tới dòng số N — theo số ở đầu dòng của bảng (tức là theo lọc / sắp xếp đang hiện), giữ cột
  đang chọn. Nhập số rồi `Enter`; số ngoài phạm vi thì báo ngay trong hộp thoại.
- **Filter**: nhiều điều kiện `= != > < >= <= Chứa Regex`, mỗi điều kiện có thể tick *Phủ định*, kết hợp
  toàn bộ danh sách theo *thoả mãn tất cả* hoặc *thoả mãn 1 trong các điều kiện* (không phải cây biểu thức lồng nhau). Mở lại
  dialog sẽ hiện đúng điều kiện đang áp dụng; ✕ trên thẻ *Lọc* bỏ lọc ngay không cần mở dialog. Chuột phải 1 ô →
  *Lọc: cột = "giá trị"* / *Lọc bỏ: cột ≠ "giá trị"* thêm 1 điều kiện `Equal` / `NotEqual` vào lọc đang có (nối `AND`; lọc
  đang có là `OR` nhiều điều kiện thì thay hẳn và báo ở thanh trạng thái).
- **Sort**: nhiều cột theo thứ tự ưu tiên (kéo Lên/Xuống để đổi thứ tự), so sánh theo số nếu cả 2 ô
  parse được số (dấu chấm thập phân "2.5", hoặc dấu thập phân của máy "2,5"; không đoán dấu phân nhóm
  "1,000" - xem `Services/CsvNumber`), ngược lại so chuỗi. Filter/Statistics đọc số cùng quy tắc. Mở lại dialog sẽ hiện đúng sort đang áp dụng; ✕ trên thẻ
  *Sắp xếp* bỏ sắp xếp ngay. Chuột phải tiêu đề cột → sắp xếp tăng / giảm theo riêng cột đó.
- **Statistics**: Row/Column Count, Duplicate Row, và theo từng cột: Empty/Unique/Min/Max/
  Average/Sum (Min/Max/Avg/Sum chỉ tính nếu cột có giá trị số).
- **Menu chuột phải** (`Grid_ContextRequested` chọn menu theo chỗ bấm - hit-test `DataGridColumnHeader` / `DataGridCell`):
  - trên ô: *Sao chép*, *Dán*, *Lọc theo / Lọc bỏ giá trị này* (chữ trên mục ghi luôn cột + giá trị), *Xem dọc dòng này*, *Chèn dòng bên dưới*,
    *Nhân bản dòng*, *Xoá dòng đang chọn*. Ô bấm được chọn (nếu nằm ngoài vùng đang chọn) để Dán / Xoá làm đúng chỗ.
    Shift+F10 / phím Menu → menu ô cho ô đang chọn.
  - trên tiêu đề cột: *Sắp xếp tăng dần / giảm dần*, *Bỏ ô lọc của cột này*, *Đổi tên cột...*, *Chèn cột bên phải...*, *Xoá cột*.

  Sao chép / Dán dùng dạng Tab như Excel (ô có tab / xuống dòng / ngoặc kép được bọc `"…"`), nên dán qua lại với Excel vẫn
  đúng từng ô.

## Dữ liệu lớn

File được đọc bất đồng bộ (không chặn UI) kèm progress bar và có thể Hủy giữa chừng, nhưng luôn nạp
**toàn bộ** vào bộ nhớ sau khi đọc xong (sửa/lọc/sắp xếp/Undo cần truy cập ngẫu nhiên mọi dòng). Nếu
ước tính file có trên khoảng 3 triệu dòng, sẽ hỏi xác nhận trước khi đọc thật. Xem `PLAN.md` để biết
chi tiết đánh đổi.

## Validation

Sau khi mở file, khu vực "Cảnh báo (N)" (thu gọn mặc định, chỉ hiện khi có cảnh báo) liệt kê: dòng
lệch số cột so với header, lỗi ngoặc kép không đóng, cột có tỉ lệ ô rỗng cao (>50%), và độ tin cậy
thấp của delimiter/encoding tự nhận diện - không cái nào chặn việc mở file.

2 loại đầu (lệch số cột, cột rỗng) được **tính lại** sau khi Add/Delete Row/Column, Rename Column,
Undo/Redo các thao tác đó, và mỗi lần Save/Save As - nên sửa dữ liệu để khắc phục cảnh báo sẽ thấy
nó biến mất mà không cần mở lại file. Lỗi ngoặc kép không đóng và cảnh báo delimiter/encoding thì
không tính lại được (mô tả nội dung file gốc lúc đọc, dữ liệu đó không còn sau khi đã parse).

## Unit test

`Tests\CsvEditor.Tests` (xUnit v3) - 89 test theo spec `Note/ModuleF.txt`: **Open** (delimiter
`,` `;` `|` Tab, encoding UTF-8 / UTF-8 BOM / UTF-16, ngoặc kép, dòng lệch cột, huỷ), **Save** (round-trip,
chỉ bọc ngoặc kép khi cần, giữ delimiter/encoding/BOM, Save As), **Search** (5 kiểu so khớp, phân biệt
hoa thường), **Sort** (số / chữ, nhiều cột, không đổi dữ liệu gốc), **Filter** (mọi toán tử, NOT,
AND/OR), **Statistics**, **Undo/Redo** (mọi thao tác sửa, dán / thay thế = 1 bước, trạng thái "chưa lưu"). Thêm sau:
dòng tiêu đề / dòng dài, **Export .xlsx** (đọc lại bằng Open XML SDK + kiểm schema), clipboard dạng Tab, file gần đây —
tổng 120 test (2026-10-02); tự nhận Shift-JIS + mẫu 8 KB cắt giữa chữ - 125 test; Đi tới cột (`ColumnFinderTests`) - 131 test (2026-11-03).

```powershell
dotnet test Tests\CsvEditor.Tests          # hoặc: dotnet run --project Tests\CsvEditor.Tests
```

Project test biên dịch kèm trực tiếp `Services/`, `Models/`, `Commands/`, `ViewModels/` của CsvEditor (không phụ
thuộc WinUI) nên không cần tách thư viện riêng. Là exe tự chạy, publish self-contained
(`dotnet publish -r win-x64 --self-contained`) chạy được cả trên máy không cài .NET (đã chạy trong
Windows Sandbox). Test mặc định chạy ở culture Invariant; `CultureTests` chạy riêng dưới vi-VN / de-DE / en-US.

Lỗi tìm ra nhờ test (đã sửa 2026-09-29): tooltip Undo sau **Xóa cột** hiện tên cột kế bên, và văng
`ArgumentOutOfRangeException` khi xoá cột cuối (`RemoveColumnCommand.Description` đọc cột sau khi đã xoá).

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Cấu hình duy nhất cạnh exe là danh sách file gần đây (`Data\Config\recent-files.json`, thiếu / hỏng thì coi như
  rỗng) - file CSV/TSV người dùng tự chọn qua `FileOpenPicker`/`FileSavePicker` hoặc kéo-thả, không phụ thuộc ai khởi
  động tiến trình (VS, `dotnet run`, hay `Process.Start` từ MainLauncher).
