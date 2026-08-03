# ModuleD: Từ điển dữ liệu từ Excel "DBDef" (Excel → SQLite → HTML)

## Bối cảnh

Định nghĩa DB nội bộ được duy trì thủ công trong các workbook Excel "DBDef" tiếng Nhật
(`Data\Excel\*.xlsm`) - mỗi sheet chứa nhiều block bảng theo layout bán tự do (không phải 1 dòng =
1 record). Người dùng cần 1 công cụ duyệt được các định nghĩa này dạng HTML (tìm theo tên bảng/cột,
xem khóa chính/khóa ngoại, mở link chia sẻ tới 1 bảng cụ thể) thay vì mở từng file Excel và Ctrl+F thủ
công. Vì file Excel là nguồn thật (source of truth) do người khác cập nhật, thiết kế phải: (1) đọc lại
được bất cứ lúc nào file thay đổi mà không cần sửa code, (2) không giả định layout cột cố định vì các
sheet không hoàn toàn đồng nhất.

## Thiết kế

### Pipeline 3 bước (SQLite làm cache trung gian)

1. **"1. Doc Excel → SQLite"** - `Services\ExcelDbDefImporter` (EPPlus 4.5.3.3) đọc toàn bộ
   `Data\Excel\*.xlsm`, ghi vào `Data\Database\ModuleD.db` qua `Services\ModuleDDatabase`
   (`Microsoft.Data.Sqlite` ADO.NET thuần). Mỗi lần chạy rebuild toàn bộ 3 bảng
   `Tables`/`Columns`/`ForeignKeys` (drop + create + insert lại) - chấp nhận đánh đổi tốc độ để tránh
   logic diff/merge phức tạp, vì import chỉ chạy khi người dùng chủ động bấm, không phải on mỗi lần mở
   app.
2. **"2. Xuat HTML"** - `Services\HtmlReportGenerator` đọc lại từ SQLite (không đọc thẳng từ Excel),
   sinh 1 file HTML tĩnh, tự chứa (data nhúng JSON, JS thuần, không CDN/mạng) - tách bước đọc và bước
   xuất để có thể xuất lại HTML nhiều lần (vd. sau khi sửa template JS) mà không cần đọc lại Excel.
3. **"Mo file HTML"** - mở bằng trình duyệt mặc định.

### Vì sao qua SQLite thay vì giữ thẳng trong bộ nhớ

Tách rõ 2 mối quan tâm: `ExcelDbDefImporter` chỉ lo parse layout Excel phức tạp, `HtmlReportGenerator`
chỉ lo dựng HTML/JS - SQLite là điểm nối trung gian giúp debug được (`SELECT * FROM Tables` để xác
nhận import đúng) mà không phải chạy lại toàn bộ pipeline UI.

### Bài toán khó nhất: layout Excel không đồng nhất

`ExcelDbDefImporter` dò vị trí cột bằng **tên header** (không hard-code index cột) vì layout lệch nhẹ
giữa các sheet. Mỗi block bảng: 1 hàng marker (`*`/`*w` + tên bảng + alias + tên tiếng Nhật) → vùng mô
tả tự do (`【説明】`/`【管理タイプ】`/`【運用後の変更に注意が必要な項目】`/`【改廃】`, thứ tự không cố
định, không phải mục nào cũng có) → bảng liệt kê cột. Quyết định thiết kế quan trọng:

- **Cột dùng chung (`$EXCTRL_COLS$`,...)**: đọc trước sheet `制御用`/`EXCTRL` để dựng bảng tra
  `$TÊN_GROUP$ -> danh sách cột thật`, rồi khi gặp dòng tham chiếu sẽ chèn thẳng cột thật vào đúng vị
  trí (đánh dấu `IsCommon` để tô màu riêng) thay vì bỏ qua - vì bỏ qua sẽ làm dữ liệu cột bị thiếu mà
  không ai biết. Nhóm không tìm thấy định nghĩa vẫn bị bỏ qua như cũ (hiếm, có log).
- **Khóa ngoại**: không dùng offset cột cố định (lệch nhẹ giữa sheet) mà lấy lần lượt 3 ô không rỗng
  đầu tiên sau cột marker của dòng `FOREIGN`.
- Mọi bước import bọc try/catch để 1 sheet/block lỗi không làm hỏng toàn bộ lần đọc; cảnh báo hiện
  trong khung log của UI.

### HTML output

Menu trái (danh sách bảng, lọc theo tên) + nội dung phải (説明/管理タイプ/cột cần lưu ý/改廃 + bảng
cột). Tô màu: vàng nhạt = khóa chính (`Level == 0`), xanh nhạt = cột dùng chung (`IsCommon`). 3 kiểu
tra cứu (chỉ tên bảng / tên bảng + tên cột / chỉ tên cột - nhóm theo bảng) gộp vào cùng 1 nút "Tim
kiem" thay vì 3 nút riêng, vì input rỗng/không-rỗng của 2 ô đã đủ phân biệt use case. Link khóa ngoại
dùng `#table=TÊN_BẢNG` + `target="_blank"` để mở tab mới đã chọn sẵn bảng đích - cho phép chia sẻ
link trực tiếp tới 1 bảng cụ thể.

## Kiểm chứng

1. `dotnet build Modules\ModuleD\ModuleD.csproj` - build sạch.
2. "1. Doc Excel → SQLite" trên bộ `.xlsm` thật - so khớp thủ công vài bảng với nội dung Excel gốc
   (đặc biệt các bảng có dùng `$...$` group và khóa ngoại nhiều cột).
3. "2. Xuat HTML" → "Mo file HTML" - kiểm tra tìm kiếm theo tên bảng/tên cột, tô màu khóa
   chính/cột dùng chung, link khóa ngoại mở đúng tab/bảng.
4. Thêm/thay 1 file `.xlsm` mới trong `Data\Excel\`, chạy lại bước 1 - xác nhận bảng mới xuất hiện mà
   không cần sửa code.
