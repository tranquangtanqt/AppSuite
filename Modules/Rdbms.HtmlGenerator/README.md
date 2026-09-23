# Rdbms.HtmlGenerator

Ứng dụng WinUI 3 độc lập - module "E": công cụ tra cứu từ điển dữ liệu (database dictionary), giống
hệt Mcf.DbDef.HtmlGenerator về kết quả cuối nhưng đọc dữ liệu trực tiếp từ **PostgreSQL hoặc Oracle** thay vì file
Excel. Không có bất kỳ tham chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới
`..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\Rdbms.HtmlGenerator\Rdbms.HtmlGenerator.csproj
```

hoặc mở `Rdbms.HtmlGenerator.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Chức năng

1. **"Thiet lap thong tin database"** - mở modal có 2 tab **PostgreSQL** / **Oracle**
   (`Microsoft.UI.Xaml.Controls.Pivot`, mỗi tab bọc trong `ScrollViewer` để không bao giờ bị cắt mất
   trường nào kể cả khi dialog thấp), mỗi tab nhập thông tin kết nối riêng:
   - Postgres: Host/Port/Database/Username/Password/Schema.
   - Oracle: Host/Port, radio **"Ket noi bang: Service Name / SID"** (mặc định Service Name) rồi
     tương ứng 1 trong 2 ô Service Name hoặc SID (ô còn lại bị disable), Username/Password/Schema.
     `OracleConnectionSettings` lưu cả `ServiceName`, `Sid`, và cờ `ConnectBySid` - importer dựng
     connection string khác nhau: Service Name dùng EZ Connect `host:port/serviceName`, SID dùng
     connect descriptor đầy đủ `(DESCRIPTION=...(CONNECT_DATA=(SID=...)))` vì EZ Connect không có
     cú pháp ngắn cho SID.

   Bấm "Luu" sẽ lưu **cả 2 tab** cùng lúc vào `Data\Config\config.xml`
   (`Services\ConnectionSettingsStore`, dùng `System.Xml.Serialization.XmlSerializer`, root
   `DatabaseConnectionsConfig` chứa `Postgres` + `Oracle`) và tự điền lại sẵn ở những lần mở module
   sau. **Lưu ý**: mật khẩu lưu dưới dạng plain text trong file này (không có tiện ích mã hóa nào sẵn
   có trong repo để dùng) - phù hợp với công cụ nội bộ, 1 người dùng, chạy cục bộ; không copy
   `config.xml` này ra ngoài máy.
2. **Chọn "Nguon: PostgreSQL / Oracle"** (radio button cạnh nút import) - quyết định
   `ImportDatabaseCommand` sẽ dùng `Services\PostgresSchemaImporter` hay `Services\OracleSchemaImporter`.
   Nút "1. Doc Database → SQLite" chỉ bật khi nguồn đang chọn có đủ thông tin tối thiểu đã lưu
   (Postgres: Host+Database; Oracle: Host + Service Name hoặc SID tùy chế độ đang chọn).
3. **"1. Doc Database → SQLite"** - kết nối theo nguồn đã chọn, đọc schema, lưu vào SQLite tại
   `Data\Database\{ten_database}.db` (`Services\RdbmsHtmlGeneratorDatabase` - bản sao gần như nguyên vẹn của
   `McfDbDefHtmlGeneratorDatabase`, cùng schema 3 bảng `Tables`/`Columns`/`ForeignKeys`). Tên file lấy đúng tên
   database/service đang kết nối (Postgres: field `Database`; Oracle: `Service Name` hoặc `SID` tùy
   chế độ đang chọn), ký tự không
   hợp lệ trên Windows bị thay bằng `_` (`RdbmsHtmlGeneratorDatabase.SanitizeFileName`). Nhờ đặt tên theo DB,
   import từ nhiều database khác nhau giữ cache SQLite riêng biệt thay vì ghi đè lên nhau. Mỗi lần
   chạy sẽ rebuild toàn bộ (drop + create + insert lại).
4. **"2. Xuat HTML"** - đọc lại `{ten_database}.db`, sinh 1 file HTML tĩnh, tự chứa tại
   `Data\Database\{ten_database}.html` (cùng quy ước đặt tên như bước 3;
   `Services\HtmlReportGenerator`, có sửa riêng so với Mcf.DbDef.HtmlGenerator: cột "STT" thay cho "Level", bỏ cột
   "Ten tieng Nhat"/"Xac dinh", bỏ chú giải/màu "Cot dung chung" vì không áp dụng cho dữ liệu DB quan
   hệ - chỉ còn tô màu khóa chính (vàng nhạt), menu trái + 2 ô tìm kiếm (tên bảng/tên cột), link khóa
   ngoại mở tab mới - xem README của Mcf.DbDef.HtmlGenerator để biết chi tiết các tính năng gốc).
5. **"Mo file HTML"** - mở file HTML vừa xuất bằng trình duyệt mặc định. Đóng rồi mở lại module vẫn
   nhận đúng file `.db`/`.html` đã có sẵn cho database/nguồn đang chọn (`RdbmsHtmlGeneratorViewModel.RefreshDatabaseTarget`
   kiểm tra lại mỗi khi đổi nguồn hoặc lưu settings), không cần import lại nếu đã có cache từ trước.

## Ánh xạ dữ liệu

Cả 2 importer đọc thẳng vào catalog/data dictionary của DB (không qua `information_schema` một mình
để tránh N+1 - trừ phần constraint vốn đã gọn theo hàng), dựng ra cùng 1 shape
`DbTableRecord`/`DbColumnRecord`/`DbForeignKeyRecord` như Mcf.DbDef.HtmlGenerator:

- **PostgreSQL** (`Services\PostgresSchemaImporter`) - `pg_catalog` (bảng/cột + comment theo lô) +
  `information_schema` (khóa chính/khóa ngoại). `Kind` = `table_type`, `DataType` =
  `format_type(atttypid, atttypmod)` (đã gồm độ dài, vd. `character varying(100)`), `DefaultValue` =
  `pg_get_expr(...)`.
- **Oracle** (`Services\OracleSchemaImporter`, dùng `Oracle.ManagedDataAccess.Core`, EZ Connect
  `host:port/serviceName`) - `ALL_TABLES`/`ALL_VIEWS` + `ALL_TAB_COMMENTS`/`ALL_COL_COMMENTS` cho
  bảng/cột/comment, `ALL_CONSTRAINTS`/`ALL_CONS_COLUMNS` cho khóa chính/khóa ngoại (khóa ngoại nhiều
  cột ghép theo `POSITION` giữa 2 phía constraint - Oracle lưu vị trí tường minh nên chính xác hơn
  cách join của Postgres). `Kind` = `BASE TABLE`/`VIEW`. `DataType` tự dựng dạng hiển thị
  (`VARCHAR2(100)`, `NUMBER(10,2)`,...) từ `DATA_TYPE`/`DATA_LENGTH`/`DATA_PRECISION`/`DATA_SCALE`
  vì Oracle không có hàm dựng sẵn kiểu `format_type`. Schema/owner mặc định = Username viết hoa (quy
  ước Oracle: schema mặc định của user trùng tên user) nếu để trống ô Schema.
- Chung cho cả 2: `Level` tái dùng đúng quy ước của Mcf.DbDef.HtmlGenerator (`0` = cột khóa chính, `1` = cột thường)
  để phần tô màu "khoa chinh" trong HTML dùng lại được mà không cần sửa gì; khóa ngoại nhiều cột nối
  `LocalColumns`/`ReferencedColumns` bằng dấu phẩy. Các field đặc thù workbook tiếng Nhật của Mcf.DbDef.HtmlGenerator
  (`JapaneseName`, `ManagementType`, `CautionItems`, `RevisionHistory`, `Alias`, `Note`, `IsCommon`)
  luôn để trống/`false` - giữ lại chỉ để model cùng hình dạng, tái dùng được `RdbmsHtmlGeneratorDatabase` mà
  không cần sửa.

Khi cần đọc lại dữ liệu mới nhất: chọn đúng nguồn rồi bấm lại "1. Doc Database → SQLite" rồi
"2. Xuat HTML".

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`.
- Không đọc `modules.json` hay bất kỳ cấu hình nào của launcher.
- Đường dẫn dữ liệu resolve theo `AppContext.BaseDirectory` của chính tiến trình Rdbms.HtmlGenerator (giống
  Mcf.DbDef.HtmlGenerator) - `Data\Config\config.xml` và `Data\Database\` tự tạo lúc chạy, không phụ thuộc ai khởi
  động tiến trình (VS, `dotnet run`, hay `Process.Start` từ MainLauncher).

## Vì sao trùng code với Mcf.DbDef.HtmlGenerator?

Kiến trúc AppSuite cấm module tham chiếu lẫn nhau (chỉ được tham chiếu `Common`), nên
`Services\RdbmsHtmlGeneratorDatabase.cs` là bản sao gần như nguyên vẹn của `Mcf.DbDef.HtmlGenerator` (chỉ đổi namespace) và
`Services\HtmlReportGenerator.cs` là bản có chỉnh sửa nhẹ từ bản gốc của Mcf.DbDef.HtmlGenerator - thay vì dùng chung
1 project, đây là đánh đổi có chủ đích để giữ tính độc lập của từng module, giống cách
ModuleA/ModuleB/ModuleC/Mcf.DbDef.HtmlGenerator đã và đang làm.
