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

`Open` `Save` `Save As` | `Undo` `Redo` | `Add Row` `Delete Row` `Copy Row` `Add Column`
`Delete Column` `Rename Column` | `Find` `Replace` `Filter` `Clear Filter` `Sort` `Clear Sort`
`Statistics`.

- **Open**: chọn `.csv`/`.tsv`/`.txt`, tự nhận diện delimiter (`,` `Tab` `;` `|`) và encoding
  (UTF-8, UTF-8 BOM, UTF-16 LE/BE). Nếu nhận diện sai, bấm vào nhãn "Encoding"/"Delimiter" ở thanh
  trạng thái để mở lại file với lựa chọn thủ công.
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
  parse được số, ngược lại so chuỗi. Mở lại dialog sẽ hiện đúng sort đang áp dụng; nút **Clear
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

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Không có dữ liệu/cấu hình lưu cạnh exe (không giống ModuleB/C/D/E) - file CSV/TSV người dùng tự
  chọn qua `FileOpenPicker`/`FileSavePicker` mỗi lần, không phụ thuộc ai khởi động tiến trình (VS,
  `dotnet run`, hay `Process.Start` từ MainLauncher).
