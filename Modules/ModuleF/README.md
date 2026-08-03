# ModuleF

Ứng dụng WinUI 3 độc lập - trình soạn thảo CSV/TSV đầy đủ (mở, sửa trực tiếp trên bảng, thêm/xóa
dòng/cột, đổi tên cột, tìm kiếm, lọc, sắp xếp, thống kê, Undo/Redo). Không có bất kỳ tham chiếu nào
tới `MainLauncher`; chỉ `ProjectReference` tới `..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\ModuleF\ModuleF.csproj
```

hoặc mở `ModuleF.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Toolbar

`Open` `Save` `Save As` | `Undo` `Redo` | `Add Row` `Delete Row` `Add Column` `Delete Column`
`Rename Column` | `Find` `Replace` `Filter` `Sort` `Statistics`.

- **Open**: chọn `.csv`/`.tsv`/`.txt`, tự nhận diện delimiter (`,` `Tab` `;` `|`) và encoding
  (UTF-8, UTF-8 BOM, UTF-16 LE/BE). Nếu nhận diện sai, bấm vào nhãn "Encoding"/"Delimiter" ở thanh
  trạng thái để mở lại file với lựa chọn thủ công.
- **Save / Save As**: ghi lại đúng delimiter/encoding đang dùng.
- **Undo/Redo**: áp dụng cho sửa ô, thêm/xóa dòng, thêm/xóa/đổi tên cột, dán nhiều ô - **không** áp
  dụng cho Filter/Sort (2 cái này chỉ là cách hiển thị, không đổi dữ liệu gốc).
- **Add/Delete Row**: thêm dòng sau dòng đang chọn (hoặc cuối bảng nếu không chọn gì); xóa mọi dòng
  đang chọn (chọn nhiều dòng được, `SelectionMode="Extended"`).
- **Add/Delete/Rename Column**: theo cột đang focus trong bảng (`CurrentColumn`).
- **Find**: 5 kiểu so khớp (Contains/Equals/StartsWith/EndsWith/Regex), có debounce 300ms khi gõ,
  điều hướng Trước/Sau giữa các kết quả (chọn dòng+cột khớp trong bảng).
- **Replace**: cùng dialog với Find, "Thay thế tất cả kết quả" ghi đè toàn bộ kết quả đang tìm được
  (qua Undo/Redo bình thường, gộp thành 1 bước).
- **Filter**: nhiều điều kiện `== != > < >= <= Contains Regex`, mỗi điều kiện có thể `NOT`, kết hợp
  toàn bộ danh sách bằng 1 phép `AND` hoặc `OR` chung (không phải cây biểu thức lồng nhau).
- **Sort**: nhiều cột theo thứ tự ưu tiên (kéo Lên/Xuống để đổi thứ tự), so sánh theo số nếu cả 2 ô
  parse được số, ngược lại so chuỗi.
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
lệch số cột so với header, lỗi ngoặc kép không đóng, cột có tỉ lệ ô rỗng cao, và độ tin cậy thấp của
delimiter/encoding tự nhận diện - không cái nào chặn việc mở file.

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Không có dữ liệu/cấu hình lưu cạnh exe (không giống ModuleB/C/D/E) - file CSV/TSV người dùng tự
  chọn qua `FileOpenPicker`/`FileSavePicker` mỗi lần, không phụ thuộc ai khởi động tiến trình (VS,
  `dotnet run`, hay `Process.Start` từ MainLauncher).
