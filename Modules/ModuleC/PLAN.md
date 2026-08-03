# ModuleC: tra cứu tên bảng/cột theo từ điển dữ liệu tiếng Nhật

## Bối cảnh

Người dùng nội bộ làm việc với các bảng DB được đặt bí danh (`Alias`) trong tài liệu thiết kế, và cần
tra ngược `Alias.NhãnTiếngNhật` (vd. `M_HD.受注番号`) ra tên cột vật lý thật, hoặc tra tên bảng từ một
phần tên/ghi chú tiếng Nhật nhớ mang máng - để dán vào SQL/Excel mà không phải mở từng file định nghĩa
DB. Dữ liệu nguồn (`data_backup_ablic_ita_*.json`, Shift-JIS/CP932) đã có sẵn từ hệ thống cũ; việc còn
lại là 1 UI tra cứu nhanh, không cần ghi/sửa dữ liệu.

## Thiết kế

- **Dữ liệu**: `Data\table_columns.json` (~103k dòng, `CopyToOutputDirectory=PreserveNewest`) là bản
  chuyển mã UTF-8 một lần của file gốc CP932 - chọn nạp phẳng 1 file JSON duy nhất vào bộ nhớ thay vì
  SQLite (khác ModuleD/ModuleE) vì dữ liệu chỉ đọc, không có quan hệ cần join, và 103k dòng nạp 1 lần
  lúc khởi động là đủ nhanh cho 1 tool nội bộ.
- **`Services\TableColumnCatalog.cs`** - nạp JSON 1 lần (`SearchViewModel.InitializeAsync`), lập chỉ
  mục `Dictionary<tableName, List<TableColumnEntry>>` (`OrdinalIgnoreCase`) để tra theo bảng là
  `O(1)` thay vì quét tuyến tính 103k dòng mỗi lần tìm. `CommentLabelMatches` tách nhãn tiếng Nhật
  khỏi `commentColumn` bằng cách cắt tại `//` đầu tiên - quy ước có sẵn trong dữ liệu nguồn
  (`commentColumn` dạng `NhãnTiếngNhật//ghi chú thêm`).
- **`ViewModels\SearchViewModel.cs`** - `SearchCommand` duyệt từng dòng `ColumnListText`
  (`Alias.NhãnTiếngNhật`), map `Alias -> TableName` qua `AliasRows` rồi gọi
  `FindByTableAndLabel`; alias nào **không** xuất hiện trong khung nhập cột (kể cả khi khung đó trống
  hoàn toàn) sẽ liệt kê toàn bộ cột của bảng đó (`GetColumnsByTable`) - hành vi cố ý để hỗ trợ vừa tra
  cột cụ thể vừa xem toàn bộ cấu trúc bảng trong cùng 1 lượt tìm, không cần 2 thao tác riêng.
  `CopyResultsCommand` dựng chuỗi TSV rồi đẩy vào `Clipboard` (Windows `DataPackage`) để dán thẳng
  sang Excel.
- **`ViewModels\TableNameSearchViewModel.cs`** - dialog tra tên bảng riêng, dùng chung
  `TableColumnCatalog` đã nạp sẵn (không tải lại dữ liệu), khớp kiểu `Contains` không phân biệt
  hoa/thường trên cả tên bảng lẫn ghi chú tiếng Nhật.
- Cấu trúc module đi theo khuôn chuẩn (`App/MainWindow/Assets/manifest`, chỉ `ProjectReference` tới
  `Common`); "Huong dan su dung" là placeholder chưa gắn hành vi - chủ đích để lại cho sau, không phải
  thiếu sót.

## Kiểm chứng

1. Nhập vài dòng `Alias.NhãnTiếngNhật` hợp lệ và 1 dòng alias không khai tên bảng - xác nhận dòng hợp
   lệ ra đúng cột, dòng thiếu bị bỏ qua êm (không crash).
2. Để trống khung cột cho 1 alias đã khai tên bảng - xác nhận liệt kê **toàn bộ** cột bảng đó.
3. Bấm Copy, dán vào Excel - xác nhận đúng định dạng TSV (STT/Tên bảng/Tên cột/Chú thích).
4. Mở dialog "Tim kiem ten bang", gõ 1 phần tên hoặc ghi chú tiếng Nhật - kết quả khớp kiểu
   substring, không phân biệt hoa/thường, và không tải lại `table_columns.json`.
5. `dotnet build Modules\ModuleC\ModuleC.csproj` - build sạch, độc lập với `MainLauncher`.
