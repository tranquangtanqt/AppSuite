# Rdbms.HtmlGenerator

Ứng dụng WinUI 3 độc lập - module "E": công cụ tra cứu từ điển dữ liệu (database dictionary), giống
hệt Mcf.DbDef.HtmlGenerator về kết quả cuối nhưng đọc dữ liệu trực tiếp từ **PostgreSQL, Oracle, MySQL / MariaDB hoặc SQL Server** thay vì file
Excel. Không có bất kỳ tham chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới
`..\..\Common\Common.csproj`.

## Chạy độc lập (không cần MainLauncher)

```powershell
dotnet run --project Modules\Rdbms.HtmlGenerator\Rdbms.HtmlGenerator.csproj
```

hoặc mở `Rdbms.HtmlGenerator.csproj` riêng trong Visual Studio, đặt Startup Project, F5.

## Chức năng

1. **"Thiết lập kết nối"** - mở modal có 4 tab **PostgreSQL** / **Oracle** / **MySQL** / **SQL Server**
   (`Microsoft.UI.Xaml.Controls.Pivot`, mỗi tab bọc trong `ScrollViewer` để không bao giờ bị cắt mất
   trường nào kể cả khi dialog thấp; mở sẵn tab của nguồn đang chọn), mỗi tab nhập thông tin kết nối riêng:
   - Postgres: Host/Port/Database/Username/Password/Schema.
   - Oracle: Host/Port, radio **"Kết nối bằng: Service Name / SID"** (mặc định Service Name) rồi
     tương ứng 1 trong 2 ô Service Name hoặc SID (ô còn lại bị disable), Username/Password/Schema.
     `OracleConnectionSettings` lưu cả `ServiceName`, `Sid`, và cờ `ConnectBySid` - importer dựng
     connection string khác nhau: Service Name dùng EZ Connect `host:port/serviceName`, SID dùng
     connect descriptor đầy đủ `(DESCRIPTION=...(CONNECT_DATA=(SID=...)))` vì EZ Connect không có
     cú pháp ngắn cho SID.
   - MySQL: Host/Port (3306)/Database/Username/Password - không có ô Schema vì trong MySQL schema chính
     là database.
   - SQL Server: Host (hoặc `Host\TenInstance` - khi đó bỏ qua Port, SQL Browser tự tìm cổng), Port (1433),
     Database, checkbox **Windows Authentication** (bật thì khoá ô Username/Password), Schema (trống = mọi
     schema). Kết nối luôn `TrustServerCertificate=true` vì SqlClient mặc định mã hoá và từ chối chứng chỉ
     tự ký của máy chủ nội bộ.

   **"Thử kết nối"** (nút phụ của ContentDialog, `args.Cancel = true` để giữ hộp thoại mở): đọc các ô của tab đang
   chọn (chưa lưu) → `RdbmsHtmlGeneratorViewModel.TestConnectionAsync` → `*SchemaImporter.TestConnectionAsync` (mở kết
   nối qua cùng `CreateConnection` với lúc import + đếm bảng / view trong phạm vi sẽ đọc, trả `ConnectionTestResult`).
   Kết quả hiện ở InfoBar trên các tab: xanh = OK, vàng = kết nối được nhưng 0 bảng, đỏ = lỗi (cùng
   `DescribeConnectionError` với hộp thoại lỗi lúc import). Đang thử thì bấm lại bị bỏ qua (không tắt nút vì focus
   sẽ nhảy sang "Huỷ" và Enter sẽ đóng hộp thoại); đổi tab / đóng hộp thoại thì huỷ lần thử đang chạy và xoá kết quả.
   Oracle: sai Service Name trả ORA-50201 bọc ORA-12514 bên trong - `DescribeConnectionError` dò cả chuỗi
   InnerException. Mọi `OracleCommand` đặt `BindByName = true` (câu đọc bảng dùng `:owner` 2 lần).

   Bấm "Lưu" sẽ lưu **cả 4 tab** cùng lúc vào `Data\Config\config.xml`
   (`Services\ConnectionSettingsStore`, dùng `System.Xml.Serialization.XmlSerializer`, root
   `DatabaseConnectionsConfig` chứa `Postgres` / `Oracle` / `MySql` / `SqlServer` + `SelectedSource` +
   `Options`; config.xml cũ thiếu phần nào thì phần đó lấy mặc định) và tự điền lại sẵn ở những lần mở module
   sau. **Lưu ý**: mật khẩu lưu dưới dạng plain text trong file này (không có tiện ích mã hóa nào sẵn
   có trong repo để dùng) - phù hợp với công cụ nội bộ, 1 người dùng, chạy cục bộ; không copy
   `config.xml` này ra ngoài máy.
2. **Combobox "Nguồn"** (PostgreSQL / Oracle / MySQL / SQL Server - thứ tự mục = thứ tự enum
   `DatabaseSourceType`) - quyết định `ImportDatabaseCommand` dùng importer nào trong `Services\*SchemaImporter`.
   Nguồn đã chọn được lưu vào config.xml, mở lại module chọn sẵn. Khoá khi đang đọc / xuất.
   Nút "1. Đọc database → SQLite" chỉ bật khi nguồn đang chọn có đủ thông tin tối thiểu đã lưu
   (Host + Database; Oracle: Host + Service Name hoặc SID tùy chế độ đang chọn).
3. **"1. Đọc database → SQLite"** - kết nối theo nguồn đã chọn, đọc schema, lưu vào SQLite tại
   `Data\Database\{ten_database}.db` (`Services\RdbmsHtmlGeneratorDatabase` - bản sao gần như nguyên vẹn của
   `McfDbDefHtmlGeneratorDatabase`, cùng schema 3 bảng `Tables`/`Columns`/`ForeignKeys`). Tên file lấy đúng tên
   database/service đang kết nối (Postgres / MySQL / SQL Server: field `Database`; Oracle: `Service Name` hoặc `SID` tùy
   chế độ đang chọn), ký tự không
   hợp lệ trên Windows bị thay bằng `_` (`RdbmsHtmlGeneratorDatabase.SanitizeFileName`). Nhờ đặt tên theo DB,
   import từ nhiều database khác nhau giữ cache SQLite riêng biệt thay vì ghi đè lên nhau. Mỗi lần
   chạy sẽ rebuild toàn bộ (drop + create + insert lại).
   **Huỷ**: `[RelayCommand(IncludeCancelCommand = true)]` sinh `ImportDatabaseCancelCommand`, nút "Huỷ" chỉ hiện khi
   `IsImporting`. Chờ importer bằng `WaitAsync(token)` nên Huỷ dừng ngay cả khi driver đang kẹt ở bước mở kết nối (ODP.NET
   bỏ qua token; lần thử đó tự hết trong thời gian chờ kết nối, log của nó bị bỏ qua). Huỷ / lỗi đều không đụng `.db`
   (chỉ thay sau khi đọc trọn vẹn) → "2. Xuất HTML" vẫn bật cho bản cũ; huỷ thì `RefreshDatabaseTarget` tính lại bước,
   lỗi thì về bước 1. Huỷ nhận ra bằng `token.IsCancellationRequested` vì mỗi driver báo huỷ một kiểu
   (`OperationCanceledException`, `SqlException` "Operation cancelled by user"...).
   Giới hạn thời gian kết nối / truy vấn lấy từ **Cài đặt** (`AppOptions`). Mọi importer mở kết nối qua
   `DatabaseConnectException.OpenAsync` - lỗi lúc mở kết nối được bọc trong `DatabaseConnectException`, nhờ đó
   phân biệt "không kết nối được" với "kết nối được nhưng truy vấn lỗi / quá giờ" mà không phải dò message của
   từng driver. Lỗi → ghi vào log + `ErrorOccurred` → `MainWindow` hiện ContentDialog; `DescribeConnectionError`
   dịch lỗi thường gặp (không tới được host, sai user/password, database / Service Name không tồn tại, truy vấn
   quá giờ) thành gợi ý dễ hiểu.
4. **"2. Xuất HTML"** - đọc lại `{ten_database}.db`, sinh 1 file HTML tĩnh, tự chứa tại
   `Data\Database\{ten_database}.html` (cùng quy ước đặt tên như bước 3;
   `Services\HtmlReportGenerator`, có sửa riêng so với Mcf.DbDef.HtmlGenerator: cột "STT" thay cho "Level", bỏ cột
   "Ten tieng Nhat"/"Xac dinh", bỏ chú giải/màu "Cot dung chung" vì không áp dụng cho dữ liệu DB quan
   hệ - chỉ còn tô màu khóa chính (vàng nhạt), menu trái + 2 ô tìm kiếm (tên bảng/tên cột), link khóa
   ngoại mở tab mới - xem README của Mcf.DbDef.HtmlGenerator để biết chi tiết các tính năng gốc).
5. **"Mở file HTML"** - mở file HTML vừa xuất bằng trình duyệt mặc định. Đóng rồi mở lại module vẫn
   nhận đúng file `.db`/`.html` đã có sẵn cho database/nguồn đang chọn (`RdbmsHtmlGeneratorViewModel.RefreshDatabaseTarget`
   kiểm tra lại mỗi khi đổi nguồn hoặc lưu settings), không cần import lại nếu đã có cache từ trước.
6. **"Cài đặt"** - modal chỉnh `AppOptions` (lưu trong `config.xml`, phần `Options`):
   - *Giới hạn thời gian kết nối* (giây, mặc định 10, 1-600).
   - *Giới hạn thời gian truy vấn* (giây, mặc định 120, 1-3600) - cho mỗi câu đọc schema; Postgres / MySQL /
     SQL Server đặt qua connection string, Oracle đặt trên từng `OracleCommand`.
   - *Tự mở file HTML sau khi xuất*.
   - *Tự mở thư mục chứa file HTML sau khi xuất* (`explorer.exe /select,"…html"` - chọn sẵn file).

   Nút "Mặc định" điền lại giá trị mặc định (chưa lưu cho tới khi bấm Lưu). Giá trị sửa tay ngoài khoảng
   trong config.xml bị kẹp lại (`EffectiveConnectTimeoutSeconds` / `EffectiveCommandTimeoutSeconds`).

**Nút màu nhấn theo bước**: `RdbmsHtmlGeneratorViewModel.NextStep` (1 = Đọc database, 2 = Xuất HTML, 3 = Mở file HTML)
→ `MainWindow.StepButtonStyle` trả `AccentButtonStyle` cho nút của bước đó, `DefaultButtonStyle` cho các nút còn lại.
Đọc xong → 2, xuất xong → 3, đọc lỗi → 1; lúc mở module / đổi nguồn tính lại từ file có sẵn (`.html` không cũ hơn `.db` → 3).

**Hướng dẫn (F1)**: nút "Hướng dẫn (F1)" hoặc phím F1 mở cửa sổ Hướng dẫn dùng chung (`SharedUI.Help.HelpWindow`); nội dung ở
`ViewsHelpContent.cs` - thêm / đổi tính năng thì cập nhật cả 2 chỗ.

## Ánh xạ dữ liệu

Các importer đọc thẳng vào catalog/data dictionary của DB (không qua `information_schema` một mình
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
- **MySQL / MariaDB** (`Services\MySqlSchemaImporter`, dùng `MySqlConnector`) - `information_schema.TABLES` /
  `COLUMNS` / `KEY_COLUMN_USAGE` lọc theo `TABLE_SCHEMA = Database`. `DataType` = `COLUMN_TYPE` (đã gồm độ dài,
  `unsigned`...), khóa chính = `COLUMN_KEY = 'PRI'`. Comment của view MySQL luôn là chữ `VIEW` nên bị bỏ.
  *Chưa thử với máy chủ MySQL thật* (máy dev không có MySQL) - mới thử kết nối lỗi / quá giờ.
- **SQL Server** (`Services\SqlServerSchemaImporter`, dùng `Microsoft.Data.SqlClient` 7 - bản 7 tách phần Azure ra
  gói riêng nên không kéo `Azure.Identity` theo) - `sys.objects` / `sys.columns` / `sys.types` /
  `sys.indexes` (khóa chính) / `sys.foreign_keys`, mô tả lấy từ extended property `MS_Description` (cái SSMS
  ghi). `DataType` dựng như SSMS hiển thị: `nvarchar(50)` (max_length tính theo byte nên chia 2 cho kiểu n),
  `varchar(max)`, `decimal(10,2)`, `datetime2(3)`. Đã thử với SQL Server LocalDB: bảng / view / khóa chính ghép /
  khóa ngoại 2 cột / mô tả tiếng Việt / lọc schema.
- **Bảng trùng tên ở nhiều schema** (`Services\TableNameQualifier`, gọi trong `ImportDatabaseAsync` trước khi lưu SQLite):
  SQLite (`Tables.TableName` là PRIMARY KEY) và HTML khoá bảng theo tên, nên trước đây 2 schema có bảng cùng tên làm
  cả lần đọc lỗi `UNIQUE constraint failed`. Giờ chỉ các tên trùng đổi thành `schema.bảng` (bảng, cột, khoá ngoại, bảng
  tham chiếu); khoá ngoại trỏ sang schema không đọc cũng hiện `schema.bảng`. Schema lấy từ `DbTableRecord.SourceSheet`,
  `DbColumnRecord.Schema`, `DbForeignKeyRecord.Schema / ReferencedSchema` (2 field sau không lưu SQLite). Các model là
  `record` để đổi tên bằng `with`.
- PostgreSQL khoá ngoại đọc từ `pg_constraint` (`unnest(conkey, confkey) WITH ORDINALITY`): bản cũ join
  `information_schema.constraint_column_usage` (không có vị trí cột) nên khoá ngoại nhiều cột bị nhân chéo
  (`cust_id,cust_id,cust_branch,cust_branch -> branch,id,branch,id`). Đã thử với PostgreSQL 17 tạm và SQL Server LocalDB.
- Chung cho cả 4: khóa ngoại đọc theo từng cặp cột rồi gộp theo (schema, bảng, constraint) ở `Services\ForeignKeyGrouper`.
  `Level` tái dùng đúng quy ước của Mcf.DbDef.HtmlGenerator (`0` = cột khóa chính, `1` = cột thường)
  để phần tô màu "khoa chinh" trong HTML dùng lại được mà không cần sửa gì; khóa ngoại nhiều cột nối
  `LocalColumns`/`ReferencedColumns` bằng dấu phẩy. Các field đặc thù workbook tiếng Nhật của Mcf.DbDef.HtmlGenerator
  (`JapaneseName`, `ManagementType`, `CautionItems`, `RevisionHistory`, `Alias`, `Note`, `IsCommon`)
  luôn để trống/`false` - giữ lại chỉ để model cùng hình dạng, tái dùng được `RdbmsHtmlGeneratorDatabase` mà
  không cần sửa.

Khi cần đọc lại dữ liệu mới nhất: chọn đúng nguồn rồi bấm lại "1. Đọc database → SQLite" rồi
"2. Xuất HTML".

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
