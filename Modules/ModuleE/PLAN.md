# ModuleE: Từ điển dữ liệu từ PostgreSQL/Oracle (DB → SQLite → HTML)

## Bối cảnh

ModuleD đọc các workbook Excel "DBDef" tiếng Nhật và biến thành 1 từ điển dữ liệu HTML duyệt được,
qua 1 cache SQLite cục bộ. Người dùng muốn cùng *kết quả cuối* (công cụ từ điển dữ liệu) nhưng lấy dữ
liệu trực tiếp từ 1 **PostgreSQL**/**Oracle** đang chạy thay vì file Excel, dưới dạng **1 module mới,
độc lập "ModuleE"** - không phải thêm chế độ vào ModuleD, vì kiến trúc AppSuite cấm mọi phụ thuộc giữa
các module (`README.md`: mỗi module chỉ `ProjectReference` tới `Common`).

Vì module không được share project reference, ModuleE **sao chép** lại phần code SQLite-cache + xuất
HTML đã kiểm chứng của ModuleD (`ModuleDDatabase.cs`, `HtmlReportGenerator.cs`) gần như nguyên bản
(đổi namespace, và với `HtmlReportGenerator` có chỉnh nhẹ - xem "Khác biệt so với ModuleD" bên dưới)
thay vì viết lại từ đầu - đúng cách các module độc lập khác trong repo đang làm. Chỉ bước *đọc dữ
liệu* là khác: 2 importer mới (`PostgresSchemaImporter` dùng `Npgsql`, `OracleSchemaImporter` dùng
`Oracle.ManagedDataAccess.Core`) đọc thẳng từ catalog/data dictionary của DB thay vì file `.xlsm`. Để
việc thay thế này "miễn phí" (không phải sửa code khác), các model `DbTableRecord`/`DbColumnRecord`/
`DbForeignKeyRecord` giữ nguyên hình dạng như ModuleD (các field đặc thù tiếng Nhật như
`JapaneseName`/`ManagementType`/`CautionItems` để trống với dữ liệu DB quan hệ; `Level == 0` vẫn có
nghĩa là khóa chính, giống quy ước của ModuleD).

**Lưu ý nêu trước**: mật khẩu DB được lưu vào `Data\Config\config.xml` dưới dạng **plain text** (repo
không có sẵn quy ước mã hóa/DPAPI nào để tận dụng, `Common` cũng không có tiện ích kiểu đó) - phù hợp
với mức độ tin cậy của 1 công cụ nội bộ, 1 người dùng, cục bộ trên máy, nhưng cần biết trước để cân
nhắc (không copy `config.xml` ra ngoài máy).

## Thiết kế

### 1. Khung module (giống hệt ModuleD)

Copy khung ModuleA/ModuleD, đổi namespace/assembly sang `ModuleE`, GUID mới cho
`Package.appxmanifest`/`app.manifest`. `ModuleE.csproj`: `ProjectReference` chỉ tới `Common`;
`PackageReference` cùng version với ModuleD (`Microsoft.Data.Sqlite 8.0.10`, `CommunityToolkit.Mvvm`,
Windows App SDK) cộng thêm `Npgsql` và `Oracle.ManagedDataAccess.Core`. `Data\Config\` và
`Data\Database\` tự tạo lúc chạy, giống `Data\Database\` của ModuleD.

### 2. Models

- `DbTableRecord`/`DbColumnRecord`/`DbForeignKeyRecord` - copy nguyên từ ModuleD. Ánh xạ chung cho cả
  2 nguồn: `Level = 0` nếu là cột khóa chính, `1` nếu không; khóa ngoại nhiều cột nối
  `LocalColumns`/`ReferencedColumns` bằng dấu phẩy; `IsCommon` luôn `false` (không áp dụng cho DB quan
  hệ); field tiếng Nhật của ModuleD để trống - giữ lại chỉ để model cùng hình dạng, tái dùng được
  `ModuleEDatabase`/phần tô màu HTML mà không cần sửa gì.
- `PostgresConnectionSettings`/`OracleConnectionSettings` - POCO thuần, mutable (bắt buộc cho
  `XmlSerializer`, khác kiểu `required`/`init` ở model khác). Oracle thêm cờ `ConnectBySid` +
  `ServiceName`/`Sid` riêng vì EZ Connect (`host:port/serviceName`) không có cú pháp ngắn cho SID -
  SID phải dùng connect descriptor đầy đủ (`(DESCRIPTION=...(CONNECT_DATA=(SID=...)))`).

### 3. Services

- **`PostgresSchemaImporter`** - `NpgsqlConnectionStringBuilder` (tránh tự ghép chuỗi), 3 nhóm query
  theo lô (không N+1): bảng+comment (`pg_class`/`pg_namespace`/`pg_description`, `objsubid = 0`), cột+
  comment (`pg_attribute`+`pg_attrdef`, `format_type()`/`pg_get_expr()`), khóa chính/ngoại qua
  `information_schema.table_constraints`+`key_column_usage`(+`constraint_column_usage`), gộp FK nhiều
  cột theo `constraint_name` bên C#.
- **`OracleSchemaImporter`** - `ALL_TABLES`/`ALL_VIEWS` + `ALL_TAB_COMMENTS`/`ALL_COL_COMMENTS` cho
  bảng/cột/comment, `ALL_CONSTRAINTS`/`ALL_CONS_COLUMNS` cho khóa chính/ngoại - FK nhiều cột ghép theo
  `POSITION` giữa 2 phía constraint (Oracle lưu vị trí tường minh nên chính xác hơn cách join của
  Postgres). Không có hàm dựng kiểu hiển thị sẵn như `format_type` nên `DataType` tự dựng từ
  `DATA_TYPE`/`DATA_LENGTH`/`DATA_PRECISION`/`DATA_SCALE` (vd. `VARCHAR2(100)`, `NUMBER(10,2)`).
  Schema/owner mặc định = Username viết hoa (quy ước Oracle) nếu để trống ô Schema.
- **`ModuleEDatabase`** - copy nguyên `ModuleDDatabase` (cùng schema SQLite 3 bảng), đổi tên
  namespace/class. Đặt tên file **theo tên database/service** (`{ten_database}.db`, ký tự không hợp lệ
  trên Windows thay bằng `_` qua `SanitizeFileName`) thay vì tên cố định như ModuleD - vì ModuleE có
  thể kết nối nhiều DB khác nhau, đặt tên cố định sẽ ghi đè cache của nhau.
- **`HtmlReportGenerator`** - copy có sửa nhẹ so với ModuleD: cột "STT" thay cho "Level", bỏ cột "Ten
  tieng Nhat"/"Xac dinh", bỏ chú giải/màu "Cot dung chung" (không áp dụng cho dữ liệu DB quan hệ) -
  chỉ còn tô màu khóa chính.
- **`ConnectionSettingsStore`** - `XmlSerializer`, root `DatabaseConnectionsConfig` chứa cả
  `Postgres` + `Oracle` trong cùng 1 file `Data\Config\config.xml` (lưu cả 2 tab cùng lúc khi bấm
  "Luu", dù chỉ đang dùng 1 nguồn) để chuyển qua lại giữa 2 nguồn không mất thông tin đã nhập của
  nguồn kia.

### 4. ViewModel

Giống hệt `ModuleDViewModel` (không DI, `AppendLog` marshal qua `DispatcherQueue` để tránh
`RPC_E_WRONG_THREAD`), thêm: `ConnectionSettings` (Postgres + Oracle, nạp qua
`ConnectionSettingsStore` trong constructor), lựa chọn "Nguon: PostgreSQL / Oracle" quyết định
`ImportDatabaseCommand` dùng importer nào, `CanImportDatabase` chỉ bật khi nguồn đang chọn có đủ
thông tin tối thiểu. `RefreshDatabaseTarget` kiểm tra lại file `.db`/`.html` đã có sẵn mỗi khi đổi
nguồn hoặc lưu settings - đóng/mở lại module không cần import lại nếu cache đã tồn tại cho DB đó.

### 5. Giao diện

Nút "Thiet lap thong tin database" mở `ContentDialog` với `Pivot` 2 tab (PostgreSQL/Oracle, mỗi tab
bọc `ScrollViewer` để không bị cắt trường khi dialog thấp) - theo pattern dialog sẵn có ở ModuleC
(`XamlRoot = Content.XamlRoot; await Dialog.ShowAsync();`). Tab Oracle có radio "Ket noi bang: Service
Name / SID" (mặc định Service Name), ô còn lại disable theo lựa chọn. Không có nút "test kết nối" -
lỗi kết nối tự lộ ra qua log/status khi bấm "1. Doc Database → SQLite", giữ dialog đơn giản.

### 6. Gắn vào solution

`MainLauncher/Config/modules.json` (entry `ModuleE`), `AppSuite.sln` (project + GUID +
`ProjectConfigurationPlatforms`), `build/Sync-Modules-Dev.ps1`, `build/Publish-AppSuite.ps1`,
`Modules/ModuleE/README.md` - cùng dạng các entry của ModuleD.

## Kiểm chứng

1. `dotnet build Modules/ModuleE/ModuleE.csproj -p:Platform=x64` - build sạch.
2. Kết nối Postgres thật, "1. Doc Database → SQLite" - số dòng trong `{ten_database}.db`
   (`Tables`/`Columns`/`ForeignKeys`) hợp lý so với schema thật.
3. Kết nối Oracle thật (cả 2 chế độ Service Name và SID) - tương tự bước 2, kiểm tra riêng trường hợp
   FK nhiều cột ghép đúng theo `POSITION`.
4. Đóng/mở lại module - dialog tự điền lại đúng từ `config.xml`, `RefreshDatabaseTarget` nhận đúng
   cache `.db`/`.html` đã có mà không phải import lại.
5. "2. Xuat HTML" → "Mo file HTML" - tìm kiếm/tô màu khóa chính/link FK hoạt động, cột "STT" thay
   "Level", không còn chú giải "Cot dung chung".
6. `dotnet build Modules/ModuleD/ModuleD.csproj` và cả solution vẫn build được - ModuleE không ảnh
   hưởng ModuleD/MainLauncher/Common.
