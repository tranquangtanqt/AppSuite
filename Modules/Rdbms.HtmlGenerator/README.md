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

   Bấm "Lưu" sẽ lưu **cả 4 tab** của **hồ sơ đang chọn** vào `Data\Config\config.xml`
   (`Services\ConnectionSettingsStore`, dùng `System.Xml.Serialization.XmlSerializer`, root
   `DatabaseConnectionsConfig` chứa `Profiles` (mỗi `ConnectionProfile`: `Name`, `Source`, `Postgres` / `Oracle` /
   `MySql` / `SqlServer`) + `SelectedProfile` + `Options`; config.xml cũ thiếu phần nào thì phần đó lấy mặc định; ghi
   file tạm rồi thay để lỗi giữa chừng không mất hồ sơ) và tự điền lại sẵn ở những lần mở module sau. Tiêu đề hộp
   thoại ghi tên hồ sơ.
   **Hồ sơ kết nối (2026-10-16)**: combobox "Hồ sơ" trên cùng + nút ⋯ (Thêm = sao chép hồ sơ đang chọn, gợi ý tên
   dev / test / prod; Đổi tên; Xoá - có hỏi, không xoá hồ sơ cuối, giữ file .db / .html). Quy tắc ở
   `Services\ConnectionProfiles` (unit test `Tests\Rdbms.HtmlGenerator.Tests`): tên ≤ 40 ký tự, không trùng (không
   phân biệt hoa thường), không ký tự cấm trong tên file; file .db / .html = `{database} ({hồ sơ})`, riêng hồ sơ
   **"Mặc định"** giữ `{database}`. config.xml cũ (các nguồn ở gốc + `SelectedSource`) tự chuyển thành hồ sơ "Mặc
   định" (`Normalize`, lúc Load) - giữ nguyên nguồn, 4 tab và file đã đọc; lần lưu sau bỏ các phần tử cũ ở gốc. **Lưu ý**: mật khẩu lưu dưới dạng plain text trong file này (không có tiện ích mã hóa nào sẵn
   có trong repo để dùng) - phù hợp với công cụ nội bộ, 1 người dùng, chạy cục bộ; không copy
   `config.xml` này ra ngoài máy.
2. **Combobox "Nguồn"** (PostgreSQL / Oracle / MySQL / SQL Server - thứ tự mục = thứ tự enum
   `DatabaseSourceType`) - quyết định `ImportDatabaseCommand` dùng importer nào trong `Services\*SchemaImporter`.
   Nguồn đã chọn được lưu vào config.xml, mở lại module chọn sẵn. Khoá khi đang đọc / xuất.
   Nút "1. Đọc database" chỉ bật khi nguồn đang chọn có đủ thông tin tối thiểu đã lưu
   (Host + Database; Oracle: Host + Service Name hoặc SID tùy chế độ đang chọn).
3. **"1. Đọc database"** - kết nối theo nguồn đã chọn, đọc schema, lưu vào SQLite tại
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
   quá giờ) thành gợi ý dễ hiểu. Phần "Chi tiết" (thông báo gốc) qua `ErrorDetail` → `Services\ServerMessageText.Clean`
   (2026-10-23): PostgreSQL trên Windows tiếng Nhật gửi lỗi đăng nhập bằng Shift-JIS trước khi thống nhất UTF-8, Npgsql đọc
   như UTF-8 → chữ vỡ (U+FFFD, không khôi phục được) → thay bằng dòng "sai bảng mã" + `SqlState` + nghĩa tiếng Nhật chuẩn
   của mã hay gặp (28P01, 28000, 3D000, 53300, 57P03, 42501). Thông báo đọc được (kể cả tiếng Nhật UTF-8) giữ nguyên;
   không ép bảng mã vì máy chủ Linux tiếng Nhật dùng UTF-8 / EUC-JP. Unit test `ServerMessageTextTests`.
4. **"2. Xuất HTML"** - đọc lại `{ten_database}.db`, sinh 1 file HTML tĩnh, tự chứa tại
   `Data\Database\{ten_database}.html` (cùng quy ước đặt tên như bước 3;
   `Services\HtmlReportGenerator`, có sửa riêng so với Mcf.DbDef.HtmlGenerator: cột "STT" thay cho "Level", bỏ cột
   "Ten tieng Nhat"/"Xac dinh", bỏ chú giải/màu "Cot dung chung" vì không áp dụng cho dữ liệu DB quan
   hệ - chỉ còn tô màu khóa chính (vàng nhạt), menu trái + 2 ô tìm kiếm (tên bảng/tên cột), link khóa
   ngoại mở tab mới - xem README của Mcf.DbDef.HtmlGenerator để biết chi tiết các tính năng gốc).
   **Lọc Tất cả / Bảng / View + "Tìm cả trong mô tả" (2026-10-22)**: 3 nút `#kindFilter` (kèm số lượng; view = `kind` chứa
   `VIEW`, gồm MATERIALIZED / SYSTEM VIEW) lọc qua `tableNameMatches`, nên áp dụng cho cả menu trái lẫn tìm cột (kể cả khi
   ô Tên bảng trống); view có nhãn `.badge`. Ô `#searchDescription`: tên bảng khớp cả `description` của bảng, tên cột khớp
   cả `description` của cột; bảng khớp nhờ mô tả hiện mô tả dưới tên ở menu trái. Chỗ khớp tô `<mark>` (`highlight()` escape
   từng đoạn). Link `#table=` tới bảng đang bị lọc ẩn thì về "Tất cả". Lựa chọn nhớ bằng `localStorage` (bọc try/catch - có
   trình duyệt chặn với file://). Thử bằng Edge headless `--dump-dom` + script chèn vào trang (Sandbox, 20/20).
5. **"3. Mở file HTML"** - mở file HTML vừa xuất bằng trình duyệt mặc định. Đóng rồi mở lại module vẫn
   nhận đúng file `.db`/`.html` đã có sẵn cho database/nguồn đang chọn (`RdbmsHtmlGeneratorViewModel.RefreshDatabaseTarget`
   kiểm tra lại mỗi khi đổi nguồn hoặc lưu settings), không cần import lại nếu đã có cache từ trước.
6. **"Cài đặt"** - modal chỉnh `AppOptions` (lưu trong `config.xml`, phần `Options`). Nội dung nằm trong `OptionsScroller`
   (ScrollViewer riêng, `MaxHeight` = chiều cao cửa sổ - 250 lúc mở): ContentDialog tắt cuộn dọc của chính nó, không bọc thì
   các ô cuối bị cắt mất ở cửa sổ thấp. Mô tả dưới mỗi ô là TextBlock xuống dòng (Description của NumberBox không xuống dòng).
   - *Giới hạn thời gian kết nối* (giây, mặc định 10, 1-600).
   - *Giới hạn thời gian truy vấn* (giây, mặc định 120, 1-3600) - cho mỗi câu đọc schema; Postgres / MySQL /
     SQL Server đặt qua connection string, Oracle đặt trên từng `OracleCommand`.
   - *Tự mở file HTML sau khi xuất*.
   - *Tự mở thư mục chứa file HTML sau khi xuất* (`explorer.exe /select,"…html"` - chọn sẵn file).
   - *Ngôn ngữ Excel định nghĩa bảng* (`TableDefinitionLanguage`, mặc định tiếng Nhật; 2026-10-23) - xem mục 8.
   - *Giữ lại số bản đọc cũ* (`HistoryCount`, mặc định 10, 0-50, `EffectiveHistoryCount`) - xem mục 9. Lưu số nhỏ hơn thì
     `SchemaHistory.Prune` ngay cho file .db đang chọn (các file khác dọn ở lần đọc sau của chúng).
   - *Bỏ qua bảng / view khi đọc* (`ExcludeTablePatterns`, chuỗi, trống = đọc hết; 2026-10-22) - `Services\TableNameFilter`
     (unit test `TableNameFilterTests`): `ParsePatterns` tách theo `,` `;` xuống dòng, bỏ trùng; lưu lại gọn "a, b". Mẫu `%` / `*`
     = nhiều ký tự, `?` = 1 ký tự, `_` là chữ thật (khác LIKE); không phân biệt hoa thường; có `.` thì so với `schema.bảng`.
     `Apply` chạy trong `ImportDatabaseAsync` ngay sau khi đọc xong, trước `TableNameQualifier` - bỏ bảng khớp (theo schema +
     tên) cùng cột / khoá ngoại của nó / index / ràng buộc; khoá ngoại bảng khác trỏ tới bảng bị bỏ giữ nguyên. Nhật ký ghi
     "Bỏ qua N bảng / view theo mẫu ...: tên..." (tối đa 20 tên). Lọc sau khi đọc (không đổi câu SQL của 4 importer).

   Nút "Mặc định" điền lại giá trị mặc định (chưa lưu cho tới khi bấm Lưu). Giá trị sửa tay ngoài khoảng
   trong config.xml bị kẹp lại (`EffectiveConnectTimeoutSeconds` / `EffectiveCommandTimeoutSeconds`).

**Nút màu nhấn theo bước**: `RdbmsHtmlGeneratorViewModel.NextStep` (1 = Đọc database, 2 = Xuất HTML, 3 = Mở file HTML)
→ `MainWindow.StepButtonStyle` trả `AccentButtonStyle` cho nút của bước đó, `DefaultButtonStyle` cho các nút còn lại.
Đọc xong → 2, xuất xong → 3, đọc lỗi → 1; lúc mở module / đổi nguồn tính lại từ file có sẵn (`.html` không cũ hơn `.db` → 3).

7. **"So sánh schema" (2026-10-21)** - nút trên hàng tiêu đề → `CompareDialog` chọn A (gốc) / B từ các file
   `Data\Database\*.db` (`ListSchemaFiles`, mới đọc nhất lên trước; A chọn sẵn file của hồ sơ / nguồn đang chọn; nút
   Đổi chỗ; chọn trùng báo lỗi, giữ hộp thoại). Chưa đủ 2 file thì báo cách tạo. `CompareSchemasAsync`:
   `RdbmsHtmlGeneratorDatabase.OpenFile(path)` (chỉ đọc) `.ReadSnapshot` cả 2 → `Services\SchemaComparer.Compare` →
   `Services\SchemaCompareReport.Build` → `Data\Database\So sánh {A} - {B}.html`, mở trình duyệt, tóm tắt vào trạng thái +
   nhật ký. Quy tắc so (unit test `Tests\Rdbms.HtmlGenerator.Tests\SchemaComparerTests`): tên bảng / cột không phân biệt
   hoa thường; cột so Kiểu / Độ dài / Null / Mặc định / Khoá chính (`Level == 0`) / Mô tả, không so thứ tự; khoá ngoại,
   index, ràng buộc so theo chuỗi định nghĩa (`SchemaComparer.Describe`), không theo tên (tên hệ thống sinh khác nhau ở
   mỗi môi trường); 1 bên là .db bản cũ (`HasExtendedInfo` = false) thì bỏ qua index / ràng buộc và trang ghi rõ. Trang
   HTML tĩnh (không nhúng JSON), ô tích ẩn thay đổi chỉ khác mô tả (`TableDiff.OnlyDescriptionChanges`).
   `ListSchemaFiles` liệt kê cả bản cũ trong Lịch sử (`SchemaFile.IsHistory`, tên `{file} - bản dd-MM-yyyy HH.mm`, hiện
   "(bản cũ)"); B chọn sẵn file *không* phải bản cũ nếu có.
8. **Xuất thêm (2026-10-22)** - dòng "Xuất thêm:" dưới 3 nút bước, 2 nút bật khi có .db (`CanExport`), đọc
   `OpenFile(.db).ReadSnapshot` rồi mở file kết quả (`OpenExported`: máy không có chương trình mở thì chỉ ghi nhật ký).
   - **Excel định nghĩa bảng** (`ExportTableDefinitionCommand` → `Services\TableDefinitionWorkbook.Write`, EPPlus 4.5.3.3 như
     Mcf.*): `Data\Database\{tên} テーブル定義書.xlsx`, tiêu đề tiếng Nhật. Ngôn ngữ (2026-10-23): Cài đặt → *Ngôn ngữ Excel
     định nghĩa bảng* (`AppOptions.TableDefinitionLanguage` = `Japanese` / `English` / `Vietnamese`, lưu config.xml theo tên,
     ComboBox cùng thứ tự enum) → `TableDefinitionWorkbook.For(language)` trả bộ nhãn `Labels` (tiêu đề, tên sheet danh sách
     `テーブル一覧` / `Table List` / `Danh sách bảng`, nhãn cột, ○ / ✓, định dạng ngày); tên file `{tên} {Labels.Title}.xlsx`
     nên mỗi ngôn ngữ 1 file. Ô giá trị của vùng gộp chỉ ghi ở ô đầu; 件数 là số (`#,##0`). 既定値 dùng
     `DbColumnRecord.DisplayDefaultValue` (`StripOuterParentheses`: SQL Server `((0))` → `0`, `(getdate())` → `getdate()`;
     `(1)+(2)` giữ nguyên, ngoặc trong '...' không tính) - cũng dùng ở HTML và trang so sánh; `DefaultValue` gốc vẫn dùng để
     so sánh. Sheet `テーブル一覧` (No / スキーマ / テーブル名 link /
     種別 / 説明 / 件数（概算） / カラム数, AutoFilter, FreezePanes) + mỗi bảng 1 sheet (bảng trước view, theo schema rồi tên):
     thông tin bảng, ■ カラム定義 (`SplitType` tách kiểu / 桁数 từ DataType), ■ インデックス + ■ 制約 (chỉ khi `HasExtendedInfo`),
     ■ 外部キー (link sang sheet bảng tham chiếu), mục trống = （なし）. `AssignSheetNames`: ≤ 31 ký tự, thay `: \ / ? * [ ]`, trùng
     (không phân biệt hoa thường, kể cả `テーブル一覧`) → `~2`. Ghi file tạm rồi `File.Move` - đang mở trong Excel thì báo lỗi, bản
     cũ còn nguyên. Unit test `TableDefinitionWorkbookTests` đọc lại bằng EPPlus.
   - **Kiểm tra chất lượng** (`CheckQualityCommand` → `Services\SchemaQualityChecker.Check` → `SchemaQualityReport.Build`):
     `Data\Database\Kiểm tra chất lượng {tên}.html`. Quy tắc (`AllRules`, mức Error / Warning / Info): no-pk, fk-type (so
     `DataType` đầy đủ, chỉ khi bảng tham chiếu có trong lần đọc), fk-index (cột khoá ngoại phải là phần đầu đúng thứ tự của
     1 index, bỏ ASC / DESC; index biểu thức không tính) + `CREATE INDEX`, dup-index (index thường có cột là phần đầu / trùng
     index khác, không xét UNIQUE / PK / WHERE / biểu thức; 2 index trùng hẳn chỉ báo 1) + `DROP INDEX`, no-stats
     (`EstimatedRows` null), no-table-desc, no-col-desc (gom theo bảng), naming (kiểu đa số snake / camel tính riêng cho
     tên bảng và tên cột; 1 từ không tính). View chỉ xét mô tả / tên. .db bản cũ (`HasExtendedInfo` = false) bỏ fk-index /
     dup-index / no-stats và trang ghi rõ. Tên bảng link `{tên}.html#table=` nếu trang từ điển đã xuất. Unit test
     `SchemaQualityCheckerTests`.
9. **Lịch sử các lần đọc + "So với bản trước" (2026-10-22)** - `Services\SchemaHistory` (unit test `SchemaHistoryTests`).
   Trong `ImportDatabaseAsync`, ngay trước `ReplaceAllAsync`, `SchemaHistory.Save` chép `{tên}.db` cũ sang
   `Data\Database\Lịch sử\{tên}\yyyy-MM-dd HHmmss.db` (tên = giờ sửa file cũ = lúc bản đó được đọc; `File.Copy` giữ giờ sửa).
   Ghi SQLite lỗi → xoá bản chép. Đọc xong `LogChangesSincePreviousAsync` so bản chép (A) với bản mới (B) bằng
   `SchemaComparer`: giống hệt → xoá bản chép + nhật ký "Không có thay đổi so với lần đọc trước (…)"; khác → nhật ký
   "So với lần đọc trước (…): N bảng mới, N bảng bị bỏ, N bảng có thay đổi (N bảng chỉ đổi mô tả)" rồi `Prune` theo Cài đặt.
   Nhờ bỏ bản trùng, bản mới nhất trong Lịch sử luôn là bản trước lần thay đổi gần nhất. `PreviousRead` (bản mới nhất,
   tính lại trong `RefreshDatabaseTarget`) → `HyperlinkButton` "So với bản trước (đọc lúc …)" dưới dòng trạng thái, chỉ hiện
   khi có bản cũ; `CompareWithPreviousCommand` = `CompareSchemasAsync(bản cũ, bản đang dùng)`. `OpenFile` (chỉ đọc) đặt
   `Pooling=False`, nếu không kết nối trong pool giữ file mở và không xoá được bản vừa so. Đổi tên hồ sơ thì thư mục Lịch sử
   cũ giữ nguyên tên cũ (file .db cũng vậy).

**Bố cục cửa sổ (2026-10-21)**: tiêu đề gọn + Cài đặt / Hướng dẫn cùng hàng; thẻ **Kết nối** (nhãn Hồ sơ / Nguồn cùng cột,
2 combobox cùng rộng, dòng `ConnectionSummary` = Host:Port/Database của nguồn đang chọn + "đã đọc lúc …" theo giờ sửa
file .db, hoặc nhắc Thiết lập kết nối khi thiếu Host / Database); thẻ **Các bước** (3 nút chia đều bề ngang có biểu
tượng, ProgressBar chỉ hiện khi bận, dòng trạng thái - đổi nguồn thì trạng thái nói nguồn mới đã đọc chưa); **Nhật ký**
chiếm phần còn lại, nút **Copy** chép mọi dòng log vào clipboard (`MainWindow.CopyLog_Click`).

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
- **Số dòng ước tính, index, ràng buộc UNIQUE / CHECK** (`DbTableRecord.EstimatedRows`, `DbIndexRecord`,
  `DbConstraintRecord`; importer trả `SchemaImportResult`; SQLite thêm cột `Tables.EstimatedRows` + bảng `Indexes`,
  `Constraints`). Số dòng lấy từ thống kê, không `COUNT(*)`:

  | DB | Số dòng | Index | UNIQUE / CHECK |
  |---|---|---|---|
  | PostgreSQL | `pg_class.reltuples` (-1 = chưa ANALYZE → null; bảng partitioned = tổng partition) | `pg_index` + `pg_get_indexdef(i, k)` từng key, DESC từ `indoption`, INCLUDE = key sau `indnkeyatts`, WHERE = `indpred` | `pg_constraint` contype u / c + `pg_get_constraintdef` |
  | Oracle | `ALL_TABLES.NUM_ROWS` (DBMS_STATS) | `ALL_INDEXES` + `ALL_IND_COLUMNS` (bỏ LOB index; khoá chính = `ALL_CONSTRAINTS.INDEX_NAME`); cột ẩn SYS_NC...$ của index theo hàm thay bằng `ALL_IND_EXPRESSIONS.COLUMN_EXPRESSION` (LONG - đọc riêng với `InitialLONGFetchSize = -1`) | `ALL_CONSTRAINTS` U / C, `SEARCH_CONDITION_VC` (12c+), bỏ NOT NULL tự sinh |
  | MySQL | `TABLES.TABLE_ROWS` | `STATISTICS` (SUB_PART, COLLATION D) | `TABLE_CONSTRAINTS` UNIQUE + `CHECK_CONSTRAINTS` (8.0.16+, đọc riêng) |
  | SQL Server | `sys.partitions` index 0 / 1 | `sys.indexes` + `sys.index_columns` (gộp trong C#, không STRING_AGG để chạy cả bản < 2017) | `sys.key_constraints` UQ + `sys.check_constraints` |

  `.db` đọc bằng bản cũ (chưa có cột / bảng mới) vẫn xuất HTML: `GetAllIndexes / GetAllConstraints` trả rỗng,
  `HasExtendedInfo()` = false → trang nhắc đọc lại. Đã thử PostgreSQL 17 tạm + SQL Server LocalDB (partial / filtered,
  INCLUDE, DESC, index theo biểu thức, UNIQUE, CHECK, bảng heap, view) và Oracle 19c thật (schema 6.472 bảng / 245.309
  cột: đọc 18 giây; 5.846 index gồm 2 index theo hàm; 142 UNIQUE + 2.445 CHECK, 19.111 NOT NULL tự sinh được bỏ đúng).
  MySQL chưa thử với máy chủ thật.
- **Dung lượng HTML**: JSON bỏ các trường rỗng / danh sách rỗng (`JsonIgnoreCondition.WhenWritingNull` + `N()` /
  `NullIfEmpty`), script trong trang điền lại `''` / `[]` ngay sau `JSON.parse` ("normalize"). Schema Oracle trên: 52 MB → 24 MB.
- Chung cho cả 4: khóa ngoại đọc theo từng cặp cột rồi gộp theo (schema, bảng, constraint) ở `Services\ForeignKeyGrouper`.
  `Level` tái dùng đúng quy ước của Mcf.DbDef.HtmlGenerator (`0` = cột khóa chính, `1` = cột thường)
  để phần tô màu "khoa chinh" trong HTML dùng lại được mà không cần sửa gì; khóa ngoại nhiều cột nối
  `LocalColumns`/`ReferencedColumns` bằng dấu phẩy. Các field đặc thù workbook tiếng Nhật của Mcf.DbDef.HtmlGenerator
  (`JapaneseName`, `ManagementType`, `CautionItems`, `RevisionHistory`, `Alias`, `Note`, `IsCommon`)
  luôn để trống/`false` - giữ lại chỉ để model cùng hình dạng, tái dùng được `RdbmsHtmlGeneratorDatabase` mà
  không cần sửa.

Khi cần đọc lại dữ liệu mới nhất: chọn đúng nguồn rồi bấm lại "1. Đọc database" rồi
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
