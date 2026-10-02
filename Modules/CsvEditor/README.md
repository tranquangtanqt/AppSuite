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

`Open` ☐ *Dòng đầu là tiêu đề* `Save` `Save As` | `Undo` `Redo` | `Add Row` `Delete Row` `Copy Row` `Add Column`
`Delete Column` `Rename Column` | `Find` `Replace` `Filter` `Clear Filter` `Sort` `Clear Sort`
`Statistics`.

- **Open**: chọn `.csv`/`.tsv`/`.txt`, tự nhận diện delimiter (`,` `Tab` `;` `|`) và encoding
  (UTF-8, UTF-8 BOM, UTF-16 LE/BE). Nếu nhận diện sai, bấm vào nhãn "Encoding"/"Delimiter" ở thanh
  trạng thái để mở lại file với lựa chọn thủ công.
- **Dòng đầu là tiêu đề** (checkbox cạnh Open, mặc định có tick — người dùng tự chọn, không tự đoán): bỏ tick cho
  file không có dòng tên cột → mọi dòng là dữ liệu, cột tự đặt tên `Cột 1`, `Cột 2`… (số cột = dòng dài nhất, không
  cắt field nào); tên này chỉ để hiển thị — Save không ghi dòng tên cột. Đổi lựa chọn khi đang mở file → mở lại file đó
  (giữ encoding / delimiter; có thay đổi chưa lưu thì hỏi trước).
- **Save / Save As**: ghi lại đúng delimiter/encoding đang dùng. Hotkey: `Ctrl+S` (Save).
- **Undo/Redo**: áp dụng cho sửa ô, thêm/xóa dòng, thêm/xóa/đổi tên cột, dán nhiều ô, kéo điền
  (fill handle), thay thế tất cả kết quả Find/Replace (mỗi thao tác trên là **1** bước Undo dù đổi
  nhiều ô/dòng cùng lúc) - **không** áp dụng cho Filter/Sort (2 cái này chỉ là cách hiển thị, không
  đổi dữ liệu gốc). Hotkey: `Ctrl+Z` (Undo), `Ctrl+Y` hoặc `Ctrl+Shift+Z` (Redo).
- **Add/Delete/Copy Row**: Add/Copy chèn dòng mới ngay sau dòng đang chọn (hoặc cuối bảng nếu
  không chọn gì); Delete xóa mọi dòng đang chọn (chọn nhiều dòng được, `SelectionMode="Extended"`).
  Sau mỗi thao tác, selection tự chuyển sang dòng liên quan (dòng mới/dòng vừa copy/dòng thế chỗ
  dòng bị xóa) thay vì mất selection.
- **Add/Delete/Rename Column**: theo cột đang focus trong bảng (`CurrentColumn`).
- **Fill handle**: chọn 1 ô, bấm-giữ-kéo ô vuông nhỏ ở góc dưới-phải xuống các dòng dưới để điền
  y nguyên giá trị; giữ `Ctrl` khi thả chuột để tự tăng dần (+1 mỗi dòng) nếu giá trị là số nguyên.
  Chỉ hỗ trợ kéo xuống, không tự cuộn khi kéo ra ngoài vùng đang hiển thị.
- **Find**: 5 kiểu so khớp (Contains/Equals/StartsWith/EndsWith/Regex), có debounce 300ms khi gõ,
  điều hướng Trước/Sau giữa các kết quả (tự cuộn dọc+ngang tới đúng ô khớp). Hotkey: `Ctrl+F`.
- **Replace**: cùng dialog với Find, "Thay thế tất cả kết quả" ghi đè toàn bộ kết quả đang tìm được,
  gộp thành 1 bước Undo. Hotkey: `Ctrl+H`.
- **Filter**: nhiều điều kiện `== != > < >= <= Contains Regex`, mỗi điều kiện có thể `NOT`, kết hợp
  toàn bộ danh sách bằng 1 phép `AND` hoặc `OR` chung (không phải cây biểu thức lồng nhau). Mở lại
  dialog sẽ hiện đúng điều kiện đang áp dụng; nút **Clear Filter** cạnh bên bỏ lọc ngay không cần
  mở dialog (chỉ sáng khi đang có lọc).
- **Sort**: nhiều cột theo thứ tự ưu tiên (kéo Lên/Xuống để đổi thứ tự), so sánh theo số nếu cả 2 ô
  parse được số (dấu chấm thập phân "2.5", hoặc dấu thập phân của máy "2,5"; không đoán dấu phân nhóm
  "1,000" - xem `Services/CsvNumber`), ngược lại so chuỗi. Filter/Statistics đọc số cùng quy tắc. Mở lại dialog sẽ hiện đúng sort đang áp dụng; nút **Clear
  Sort** cạnh bên bỏ sắp xếp ngay không cần mở dialog (chỉ sáng khi đang có sort).
- **Statistics**: Row/Column Count, Duplicate Row, và theo từng cột: Empty/Unique/Min/Max/
  Average/Sum (Min/Max/Avg/Sum chỉ tính nếu cột có giá trị số).
- **Context menu** (chuột phải trên bảng): Copy, Paste, Delete, Insert Row, Duplicate Row.

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
AND/OR), **Statistics**, **Undo/Redo** (mọi thao tác sửa, dán / thay thế = 1 bước, trạng thái "chưa lưu").

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
- Không có dữ liệu/cấu hình lưu cạnh exe (không giống ModuleB/C/D/E) - file CSV/TSV người dùng tự
  chọn qua `FileOpenPicker`/`FileSavePicker` mỗi lần, không phụ thuộc ai khởi động tiến trình (VS,
  `dotnet run`, hay `Process.Start` từ MainLauncher).
