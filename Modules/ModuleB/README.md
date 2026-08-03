# ModuleB

Ứng dụng WinUI 3 độc lập - công cụ tìm kiếm file Office (`.xlsx/.xlsm/.docx`) theo nhóm thư mục và
từ khóa nội dung. Cùng khuôn mẫu với [ModuleA](../ModuleA/README.md) (chỉ `ProjectReference` tới
`..\..\Common\Common.csproj`, không tham chiếu `MainLauncher`). Trong `modules.json`, ModuleB có
`AutoStart: false` và `Arguments: "--mode=test"` để minh hoạ trường hợp module không tự khởi động
cùng launcher và có nhận tham số dòng lệnh.

## Chạy độc lập

```powershell
dotnet run --project Modules\ModuleB\ModuleB.csproj
```

Hoặc mở `ModuleB.csproj` trực tiếp trong Visual Studio và F5. Xem README của
[ModuleA](../ModuleA/README.md) để biết chi tiết về nguyên tắc độc lập (không reference
MainLauncher, chỉ reference `Common`).

## Chức năng

- **Cột trái - Explorer**: cây thư mục (`TreeView`, mở rộng dần khi bấm - `Expanding`) của 1 thư mục
  gốc; nút **"..."** để chọn thư mục gốc mới, nút **"Cai dat thu muc"** mở `SettingsDialog` liệt kê
  các thư mục gốc đã dùng trước đó (lưu trong SQLite, xem "Dữ liệu" bên dưới) để chọn lại nhanh.
- **Cột phải - Tìm kiếm**:
  - Ô **"Nhom / thu muc"**: lọc theo tên thư mục con cấp 1 ngay dưới thư mục gốc (nhóm) - vd. nhập
    `10.PD` để chỉ tìm trong nhóm đó.
  - Ô **"Noi dung tim kiem"**: từ khóa trong nội dung file.
  - Cả 2 ô hỗ trợ cú pháp `A AND B OR C` (OR của các nhóm AND), so khớp không phân biệt hoa/thường,
    dạng "chứa chuỗi" (`Services\QueryMatcher`).
  - Nút **"Tim kiem"**: duyệt đệ quy toàn bộ thư mục gốc, lọc theo nhóm trước (rẻ) rồi mới trích xuất
    nội dung + lọc từ khóa (đắt). Kết quả hiện dạng danh sách (Thư mục | Tên file).
  - Nút **"Xóa"**: xóa 2 ô lọc và kết quả hiện tại.
- Trích xuất nội dung (`Services\OfficeTextSearchService`) chỉ đọc được `.xlsx/.xlsm` (toàn bộ shared
  string + giá trị cell) và `.docx` (`Body.InnerText`) qua `DocumentFormat.OpenXml`; định dạng binary
  cũ (`.xls/.doc`) hoặc bất kỳ loại khác **fallback về so khớp theo tên file** thay vì bị loại khỏi
  kết quả. File đang mở/khoá bởi Office hoặc lỗi đọc không làm crash lượt tìm.

## Dữ liệu

`Data\DataFromExcel.db` (SQLite, tự tạo cạnh exe lúc chạy) lưu lịch sử các thư mục gốc đã dùng
(`Services\SavedFolderRepository`, bảng `SavedFolders(Id, Path UNIQUE)`) để `SettingsDialog` gợi ý lại
mà không cần duyệt lại thư mục từ đầu mỗi lần mở module.

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Đường dẫn dữ liệu (`Data\DataFromExcel.db`) resolve theo `AppContext.BaseDirectory` của chính tiến
  trình ModuleB, không phụ thuộc ai khởi động nó (VS, `dotnet run`, hay `Process.Start` từ
  MainLauncher).
