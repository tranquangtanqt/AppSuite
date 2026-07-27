# ModuleC

Ứng dụng WinUI 3 độc lập - "Tim kiem" (tra cứu tên bảng/cột theo từ điển dữ liệu). Không có bất kỳ
tham chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới `..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\ModuleC\ModuleC.csproj
```

hoặc mở `ModuleC.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Chức năng

- Cột trái: danh sách cặp (Tên bảng, Alias) có thể thêm/xóa dòng, và ô nhập danh sách cột dạng
  `Alias.NhanTiengNhat` (mỗi dòng một cột cần tra).
- Nút **Tim kiem**: với mỗi dòng nhập, tra `Alias` ra `Tên bảng` tương ứng, rồi tìm trong từ điển
  cột có nhãn tiếng Nhật (phần đứng trước `//` trong `commentColumn`) khớp chính xác với phần sau
  dấu `.`. Kết quả hiện ở bảng bên phải: STT, Tên bảng, Tên cột (`Alias.ColumnName`), Chú thích.
  Alias nào không có dòng nào trong khung nhập cột (kể cả khi khung đó để trống hoàn toàn) sẽ được
  liệt kê **toàn bộ cột** của bảng tương ứng.
- Nút **Copy**: copy toàn bộ bảng kết quả (dạng TSV) vào clipboard để dán sang Excel.
- Nút **Tim kiem ten bang**: mở `ContentDialog` tra cứu tên bảng - nhập tên bảng (tiếng Anh) hoặc
  chú thích (tiếng Nhật), khớp kiểu "chứa chuỗi" (`Contains`, không phân biệt hoa/thường) trên cả
  hai trường, liệt kê mọi bảng khớp (STT, Tên bảng, Chú thích tên bảng). Dùng chung
  `TableColumnCatalog` đã nạp sẵn với ô tìm kiếm chính (không tải lại dữ liệu).
- "Huong dan su dung": placeholder, chưa gắn hành vi.

## Dữ liệu

`Data\table_columns.json` (~103k dòng, `CopyToOutputDirectory=PreserveNewest`) là bản chuyển mã
UTF-8 của `data_backup_ablic_ita_*.json` gốc (Shift-JIS/CP932) - mỗi dòng gồm `tableName`,
`columnName`, `commentColumn`, `commentTable`. `Services\TableColumnCatalog` nạp file này một lần
lúc khởi động (`SearchViewModel.InitializeAsync`) và lập chỉ mục theo `tableName` để tra nhanh.

Khi cần cập nhật từ điển: thay file gốc, chạy lại bước chuyển mã (đọc bằng `cp932`, ghi UTF-8) rồi
đè vào `Data\table_columns.json`.

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Đường dẫn dữ liệu resolve theo `AppContext.BaseDirectory` của chính tiến trình ModuleC, không
  phụ thuộc vào ai khởi động nó (VS, `dotnet run`, hay `Process.Start` từ MainLauncher).
