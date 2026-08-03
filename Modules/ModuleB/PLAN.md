# ModuleB: tìm kiếm file Office theo nhóm thư mục + từ khóa nội dung

## Bối cảnh

Bắt đầu từ khuôn `ModuleA` (module mẫu chứng minh tính độc lập), ModuleB được giao thêm nghiệp vụ
thật: cho một thư mục gốc (thường là thư mục lưu trữ tài liệu Excel/Word theo dự án), người dùng cần
lọc nhanh ra những file thuộc một "nhóm" con (thư mục cấp 1 ngay dưới root, vd. `10.PD`, `20.PG`) và
có chứa 1 từ khóa nào đó trong nội dung, mà không phải mở từng file. `modules.json` vẫn giữ ModuleB
làm ví dụ minh hoạ `AutoStart: false` + `Arguments: "--mode=test"` cho `MainLauncher`, nhưng đó không
còn là mục đích chính của module.

## Thiết kế

- **`Services\OfficeTextSearchService.cs`** - trích xuất text bằng `DocumentFormat.OpenXml`
  (`SpreadsheetDocument`/`WordprocessingDocument`), chỉ đọc được `.xlsx/.xlsm` (shared strings +
  từng cell) và `.docx` (`Body.InnerText`); định dạng binary cũ (`.xls/.doc`) hoặc bất kỳ loại nào
  khác trả về `null` để caller **fallback về so khớp theo tên file** thay vì âm thầm loại file đó ra
  khỏi kết quả. Bọc try/catch quanh `IOException`/`UnauthorizedAccessException`/
  `InvalidOperationException` (file đang mở/khoá bởi Office) để 1 file lỗi không làm hỏng cả lượt tìm.
- **`Services\QueryMatcher.cs`** - cú pháp truy vấn dạng `A AND B OR C` (OR của các nhóm AND), so khớp
  "chứa chuỗi" không phân biệt hoa/thường; tách bằng regex `\s+OR\s+`/`\s+AND\s+` nên từ khóa không
  được chứa chữ "AND"/"OR" viết hoa nguyên từ làm token cú pháp. Query rỗng luôn khớp (dùng cho cả
  `GroupFilter` lẫn `KeywordQuery` để tái dùng 1 hàm).
- **`ViewModels\MainViewModel.cs`** - `SearchCommand` (`Task.Run` để không chặn UI thread) duyệt đệ
  quy toàn bộ `RootPath` (`Directory.EnumerateFiles(..., AllDirectories)`), nhóm file theo segment đầu
  tiên của đường dẫn tương đối (`"(root)"` nếu file nằm ngay tại root), lọc theo `GroupFilter` trước
  (rẻ) rồi mới `ExtractText` + lọc `KeywordQuery` (đắt) - tránh đọc nội dung file không thuộc nhóm cần
  tìm. Kết quả sắp theo `GroupName` rồi `FileName`.
- **`Services\SavedFolderRepository.cs`** - lưu lịch sử các `RootPath` đã dùng vào SQLite
  (`Data\DataFromExcel.db`, `Microsoft.Data.Sqlite` ADO.NET thuần, bảng `SavedFolders(Id, Path UNIQUE)`,
  `INSERT OR IGNORE` để idempotent) để Settings dialog gợi ý lại thư mục cũ thay vì phải duyệt lại từ
  đầu mỗi lần mở module.
- **`ViewModels\SettingsViewModel.cs`** - wrap `SavedFolderRepository`, nạp danh sách thư mục đã lưu
  lúc khởi tạo, `SaveCurrentPath()` thêm + reload.

## Kiểm chứng

1. Trỏ `RootPath` vào thư mục có cấu trúc `<Nhóm>\...\*.xlsx|*.docx`, gõ `GroupFilter`/`KeywordQuery`
   - kết quả chỉ gồm file đúng nhóm và có chứa từ khóa (kể cả trong cell/đoạn văn, không chỉ tên file).
2. Test cú pháp `A AND B OR C` trong `KeywordQuery` - xác nhận đúng ngữ nghĩa OR-của-AND.
3. Đưa 1 file `.xls` (binary cũ) hoặc file đang mở bởi Excel vào thư mục test - xác nhận không crash,
   fallback so khớp theo tên file.
4. Lưu 1 `RootPath` qua Settings, đóng module, mở lại - `SavedFolders` hiện đúng lịch sử từ
   `Data\DataFromExcel.db`.
5. `dotnet build Modules\ModuleB\ModuleB.csproj` - build sạch, không đụng `MainLauncher`.
