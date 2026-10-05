# FileTools: xử lý file text / CSV / log lớn cho AppSuite

## Bối cảnh

Người dùng cần các thao tác trên file text lớn mà Excel/Notepad/CsvEditor làm không nổi: CsvEditor nạp toàn bộ
file vào RAM nên không mở được file vài GB. Đã chốt với người dùng (2026-09-30):

- **Loại file**: chỉ text / CSV / log — không xử lý file nhị phân.
- **Cỡ file**: từ vài trăm MB tới vài GB → mọi thao tác phải **đọc kiểu stream** (RAM không tăng theo cỡ file),
  chạy nền, có tiến độ + nút Huỷ.
- **Tổ chức**: module riêng `Modules\FileTools` (chỉ reference `Common` + `SharedUI`, theo luật kiến trúc).
- **Encoding đầu ra**: mặc định UTF-8, đổi được qua menu (UTF-8 / UTF-8 BOM / Shift-JIS / UTF-16 LE / giữ như file nguồn).
- **Phạm vi**: làm **tất cả** — 3 tính năng chính + 14 tính năng mở rộng đã đề xuất.

## Thiết kế

### 1. Cấu trúc project

```
Modules\FileTools\
  Core\        logic thuần, KHÔNG phụ thuộc WinUI → unit test biên dịch kèm (như CsvEditor.Tests)
  Models\      tuỳ chọn / kết quả của từng thao tác
  ViewModels\  1 ViewModel / trang (CommunityToolkit.Mvvm)
  Views\       1 Page / tính năng
  Services\    AppLog (chép từ ImageCompare), hộp thoại chọn file/thư mục (InitializeWithWindow như
               ImageCompare\Services\ImageFileService.cs), PresetStore, mở kết quả
Tests\FileTools.Tests\  (xUnit v3, Compile Include Core\ + Models\)
```

Khuôn csproj / App.xaml / manifest: copy từ `Modules\CsvEditor` (đã có `System.Text.Encoding.CodePages` +
`Encoding.RegisterProvider` trong `App()` — cần cho Shift-JIS).

### 2. Giao diện

`NavigationView` bên trái, mỗi mục 1 trang, nhóm lại:

| Nhóm | Trang |
|---|---|
| Xem | **Thông tin file** · **Trích dòng** (N dòng đầu / dòng X–Y / N dòng cuối) · **Tìm** · **So sánh 2 file** · **Theo dõi log** |
| Gộp / tách | **Nối file trong thư mục** · **Tách file** (theo dung lượng / số dòng / số phần / giá trị cột CSV) |
| Biến đổi | **Lọc dòng** · **Đổi encoding** (hàng loạt) · **Đổi xuống dòng** (CRLF ↔ LF) · **Thay thế hàng loạt** |
| CSV | **Bỏ dòng trùng** · **Chọn / sắp xếp cột** · **Đổi dấu phân cách** |
| Thư mục | **Tìm file trùng** |

Phần dùng chung ở mọi trang: chọn file (kéo-thả được), khối **Đầu ra** (thư mục / encoding / xuống dòng),
thanh tiến độ + Huỷ, nhật ký, nút **Mở kết quả** / **Mở thư mục**, và **Lưu thành mẫu / Nạp mẫu** (preset).

### 3. Lõi đọc/ghi (Core) — dùng chung cho mọi tính năng

- **`EncodingSniffer`**: BOM (UTF-8/16) → mẫu 0x00 (UTF-16 không BOM) → UTF-8 nghiêm ngặt → **Shift-JIS nghiêm ngặt**
  (tài liệu mcframe hay dùng) → mặc định UTF-8 + cảnh báo. Dựa trên `Modules\CsvEditor\Services\EncodingDetector.cs`
  (chép, vì module không reference nhau) + thêm bước Shift-JIS. Mẫu 64 KB, cắt ở biên ký tự để không báo sai.
- **`LineReader`**: đọc dòng **giữ nguyên ký tự xuống dòng** (`StreamReader.ReadLine` làm mất CRLF/LF — không dùng
  được cho tách/trích/đổi xuống dòng). Buffer 1 MB, `FileOptions.SequentialScan`, `FileShare.ReadWrite` (đọc được log
  đang ghi), báo tiến độ theo byte đã đọc.
- **`CsvRecordReader`**: bản ghi CSV có thể xuống dòng trong ngoặc kép → các thao tác CSV đọc theo **bản ghi**, không
  theo dòng. Tách field quote-aware: chép từ `Modules\CsvEditor\Services\CsvParser.cs`; nhận dấu phân cách:
  chép `DelimiterDetector.cs`.
- **`OutputOptions`**: encoding (mặc định UTF-8), xuống dòng (giữ nguyên / CRLF / LF), ghi đè hay thêm hậu tố.
- **`JobRunner`**: chạy trên `Task.Run`, `CancellationToken`, `IProgress` gộp tối đa ~10 lần/giây; huỷ giữa chừng
  thì xoá file đầu ra dở dang.
- **An toàn dữ liệu**: không bao giờ sửa file nguồn tại chỗ mặc định. Đổi encoding / xuống dòng / thay thế hàng
  loạt ghi ra thư mục đầu ra; tuỳ chọn "ghi đè file gốc" bắt buộc tạo `.bak` trước.

### 4. Từng tính năng — cách làm với file vài GB

| Tính năng | Cách làm |
|---|---|
| Thông tin file | 1 lượt stream: số dòng, dung lượng, encoding, kiểu xuống dòng (CRLF/LF/lẫn lộn), dòng dài nhất, dấu phân cách + số cột (mẫu đầu) |
| Trích dòng | N đầu: đọc đủ là dừng (tức thì). X–Y: stream bỏ qua tới X. **N cuối: đọc ngược từ cuối file** theo khối 64 KB (UTF-16 xử lý theo cặp byte) — không quét cả file. Ghi `%TEMP%\AppSuite\FileTools\` rồi mở |
| Tìm | Chữ / regex, hoa-thường, giới hạn số kết quả hiển thị (vd 10.000), xuất toàn bộ dòng khớp ra file tạm |
| So sánh 2 file | Myers trên mã băm từng dòng (như `Modules\ImageCompare\Engine\RowAligner.cs`), giới hạn số bước sửa; file quá khác → báo thay vì treo. Hiện dòng thêm / bớt / sửa, xuất báo cáo |
| Theo dõi log | Đọc đuôi file, `FileSystemWatcher` + đọc định kỳ phần mới; nhận biết file bị xoay vòng (cỡ nhỏ lại → đọc từ đầu); lọc theo từ khoá; tạm dừng cuộn |
| Nối file | Chọn thư mục + mẫu lọc + thư mục con; sắp theo tên (hiểu số) / ngày / tự kéo; tuỳ chọn chèn tên file / dòng phân cách; CSV giữ header 1 lần; mỗi file tự nhận encoding → ghi 1 encoding thống nhất; đảm bảo có xuống dòng giữa 2 file |
| Tách file | Theo dung lượng (cắt ở cuối dòng), số dòng, số phần; CSV lặp header ở mọi phần; **theo giá trị cột** → mỗi giá trị 1 file (giới hạn số file mở cùng lúc, đóng/mở lại kiểu LRU) |
| Lọc dòng | Giữ / bỏ dòng chứa từ khoá (mọi / một trong các từ) hoặc regex |
| Đổi encoding / xuống dòng | Hàng loạt trên file hoặc thư mục, xem trước encoding nhận được của từng file |
| Thay thế hàng loạt | Nhiều file, chữ / regex; bước **xem trước** (số chỗ thay / file) rồi mới ghi |
| Bỏ dòng trùng (CSV/text) | Giữ lần xuất hiện đầu; lưu **mã băm 64-bit** thay vì cả dòng → vài chục triệu dòng vẫn vừa RAM; tuỳ chọn so theo 1 số cột |
| Chọn / sắp cột, đổi dấu phân cách | Theo bản ghi CSV, ghi lại có ngoặc kép khi cần |
| Tìm file trùng | Nhóm theo dung lượng → băm SHA-256 chỉ các file cùng cỡ |
| Mẫu (preset) | JSON trong `Data\Config\presets.json` cạnh exe (cùng quy ước với các module khác) |

**Mở kết quả**: nếu có `..\CsvEditor\CsvEditor.exe` (cùng bộ deploy) và file là CSV → mở bằng CsvEditor; ngược lại
mở bằng ứng dụng mặc định. Không đọc `modules.json`.

### 5. Đăng ký vào solution

`AppSuite.sln` (+ `Tests\FileTools.Tests`), `MainLauncher\Config\modules.json`, `build\Sync-Modules-Dev.ps1`,
`build\Publish-AppSuite.ps1`, README gốc (cây thư mục, số project), `Tiến trình.md` (mục FileTools mới).

### 6. Thứ tự làm (mỗi đợt build sạch + test đạt rồi mới sang đợt sau)

1. **Khung + lõi**: project, NavigationView, khối Đầu ra / tiến độ / nhật ký, `EncodingSniffer`, `LineReader`,
   `CsvRecordReader`, `JobRunner`.
2. **3 tính năng chính**: Nối file, Tách file (3 cách đầu), Trích dòng.
3. **Thông tin file, Tìm, Đổi encoding, Đổi xuống dòng, Lọc dòng.**
4. **CSV**: tách theo giá trị cột, bỏ dòng trùng, chọn/sắp cột, đổi dấu phân cách.
5. **So sánh 2 file, Theo dõi log, Thay thế hàng loạt, Tìm file trùng, Mẫu (preset).**

Sau mỗi đợt tôi báo lại để bạn thử, rồi mới làm đợt tiếp.

## Kiểm chứng

1. `dotnet build AppSuite.sln -p:Platform=x64` — 0 warning; mở riêng `FileTools.csproj` F5 chạy được không cần MainLauncher.
2. `Tests\FileTools.Tests`: encoding (UTF-8/BOM/UTF-16/Shift-JIS/lẫn lộn), giữ đúng CRLF/LF/dòng cuối không có xuống
   dòng, nối (thứ tự tên có số, header CSV 1 lần, encoding khác nhau), tách (tổng các phần = file gốc từng byte,
   không cắt ngang dòng, header lặp), trích đầu/giữa/cuối (kể cả file 1 dòng, rỗng, UTF-16), CSV có xuống dòng trong
   ngoặc kép, bỏ trùng, diff, thay thế, huỷ giữa chừng không để lại file dở.
3. **Test file lớn** (đánh dấu riêng, chạy tay): sinh file ~2 GB trong `%TEMP%` → trích 1.000 dòng đầu / cuối < 1 s,
   tách 10 phần, nối lại, so từng byte với file gốc; RAM tiến trình không vượt ~200 MB.
4. GUI trong Windows Sandbox (harness sẵn có): mỗi trang chạy 1 thao tác thật, Huỷ giữa chừng, mở kết quả.
5. Chạy từ MainLauncher sau `Sync-Modules-Dev.ps1`.

## Kết quả đợt 1–2 (2026-09-30)

- Khung + lõi + 3 tính năng chính (Nối file, Tách file, Trích dòng) xong; build 0 warning.
- `Tests\FileTools.Tests`: 59/59 đạt.
- File ~2 GB (25,9 triệu dòng CSV có tiếng Nhật / Việt): 1.000 dòng đầu 15 ms, 1.000 dòng cuối 5 ms, tách 10 phần
  26 s, nối lại 18 s → giống bản gốc từng byte (SHA-256); RAM cao nhất 75 MB.
- GUI (UI Automation trên máy dev): mở đủ 3 trang; nối 3 CSV (thứ tự a1 → a2 → a10, tiêu đề 1 lần, nguồn có BOM → UTF-8),
  tách 4 phần có tiêu đề, trích 1.000 dòng cuối kèm tiêu đề. Lỗi tìm ra và đã sửa: ô đường dẫn chỉ cập nhật
  ViewModel khi mất focus (dán đường dẫn xong nút vẫn mờ) → `UpdateSourceTrigger=PropertyChanged`.

## Đợt 3 (2026-09-30): Thông tin file, Tìm, Đổi encoding, Đổi xuống dòng, Lọc dòng

Quyết định thiết kế:
- **Đổi encoding và Đổi xuống dòng dùng chung 1 lõi `FileRewriter`** ("ghi lại file với encoding X, xuống dòng Y"),
  chỉ khác tuỳ chọn mặc định của từng trang. Ghi đè tại chỗ: file mới ghi xong mới đổi tên, bản cũ → `.bak`; file
  không có gì thay đổi thì không ghi, không tạo `.bak` thừa.
- **Tìm và Lọc dùng chung `TextMatcher`**. "Không phân biệt dấu" mặc định bật (người dùng hay gõ không dấu); bỏ dấu qua
  bảng tra 65.536 ký tự tính 1 lần - `Normalize` từng ký tự của file vài GB quá chậm - và giữ nguyên độ dài chuỗi để vị
  trí khớp vẫn đúng với dòng gốc.
- **Mất ký tự khi đổi encoding không được âm thầm**: `OutputFile` dùng bản sao encoding có fallback đếm số ký tự thay
  bằng "?" (vd chữ Việt → Shift-JIS) và báo trong nhật ký. Bản dùng để đo byte tách riêng vì `GetByteCount` cũng gọi
  fallback (dùng chung thì 1 ký tự bị đếm nhiều lần); số đếm chỉ đủ sau khi xả buffer (`Commit`).
- **Thông tin file đọc 1 lượt**: đếm bản ghi / số cột CSV ngay trong vòng đọc dòng bằng cách mang trạng thái "đang trong
  ngoặc kép" qua các dòng (bản đầu đọc 2 lượt: 36 s → 12,6 s trên file 2 GB).
- Trang hàng loạt có ô **dán đường dẫn** (ngoài hộp thoại / kéo-thả) - hay dùng khi copy đường dẫn từ Explorer, và là
  cách duy nhất UI Automation thêm được file để kiểm thử.

Kết quả: test 79/79; file 2 GB: thông tin file 12,6 s, tìm không dấu 26 s, lọc 20 s, RAM ≤ 75 MB. GUI (UI Automation):
thông tin CSV (phát hiện bản ghi lệch cột), tìm không dấu 30/3.000 dòng + xuất file tạm, lọc giữ bản ghi CSV 2 dòng,
đổi Shift-JIS → UTF-8 đúng byte, đổi LF → CRLF ghi đè + `.bak` (file đã CRLF được bỏ qua). Lỗi tìm ra và đã sửa: các dòng
nhật ký của 1 lần chạy hiện ngược thứ tự (danh sách phần hiện part004 → part001).

### Kiểm thử trong Windows Sandbox (2026-09-30)

- Unit test self-contained trên máy không cài .NET: 79/79; file 300 MB: thông tin 0,8 s, tìm không dấu 1,6 s, lọc 1,5 s,
  tách 10 phần 3,2 s, nối lại 4,5 s, RAM 76 MB.
- GUI bản self-contained (150% DPI, cửa sổ ~1010×570 logical): 8 trang chạy thật qua UI Automation đều đúng. Ảnh chụp
  lộ 4 lỗi bố cục mà test chức năng không bắt được, đã sửa: (1) trang hàng loạt: danh sách file bị ép về 0 (thấy "2 file"
  nhưng không thấy dòng nào); (2) trang không cuộn được → phần tiến độ / nhật ký bị cắt; (3) trang Tìm: ô tuỳ chọn cuối
  tràn mép phải; (4) trang Lọc: ô từ khoá bị đẩy xuống giữa hàng. Cách sửa (1)(2): mỗi trang bọc trong ScrollViewer,
  chiều cao nội dung = max(vùng nhìn thấy, chiều cao tối thiểu của trang) (`Views\ScrollFit`) - cửa sổ lớn vẫn giãn đầy
  như cũ, cửa sổ nhỏ thì cuộn; đặt Grid thẳng trong ScrollViewer thì hàng "*" mất tác dụng nên không dùng. Kiểm lại:
  danh sách hiện đủ, lăn chuột xuống 100% thấy đủ nhật ký.

## Đợt 4 (2026-09-30): CSV - tách theo cột, bỏ dòng trùng, chọn / sắp cột, đổi dấu phân cách

Quyết định thiết kế:
- **Chọn / sắp cột và Đổi dấu phân cách dùng chung `CsvTransformer`** (đọc bản ghi → chọn cột theo thứ tự → ghi với dấu
  phân cách X, chỉ bọc ngoặc kép khi cần hoặc bọc hết). Trang giao diện vẫn tách riêng cho dễ tìm.
- **Tách theo giá trị cột mở tối đa 64 file cùng lúc**: file ít dùng nhất được ghi xong (`Commit`) rồi mở lại ở chế độ ghi
  tiếp (`OutputFile(append: true)` - đổi tên về .partial, không ghi lại BOM). Không giới hạn thì cột có hàng nghìn giá trị
  làm cạn handle / RAM (mỗi file 1 buffer). Tên file: bỏ ký tự cấm, cắt 80 ký tự; Windows không phân biệt hoa-thường nên
  "A" và "a" phải ra 2 file khác tên (`_2`). Quá 5.000 giá trị → dừng, xoá hết (thường là chọn nhầm cột mã / số tiền).
- **Bỏ dòng trùng chỉ nhớ mã băm 64-bit (XxHash3)** của khoá, trong `UInt64Set` tự viết (địa chỉ mở, 8 byte / ô, cấp sẵn
  theo số dòng ước tính). Bản đầu dùng `HashSet<ulong>` (~20 byte / phần tử, lúc mở rộng giữ cả mảng cũ + mới) → file 2 GB
  26 triệu dòng khác nhau **hết RAM** (lúc Sandbox cũng đang chạy). Khoá nhiều cột nối bằng ký tự `\u0001` để ("a","bc") ≠
  ("ab","c"). Hết RAM vẫn có thể xảy ra với file cực lớn → báo lỗi rõ và gợi ý so theo cột / tách file trước, không sập.
- Trang CSV dùng chung `CsvSourcePanel` (file + dấu phân cách tự nhận / chọn tay + "Dòng 1 là tiêu đề") và danh sách cột
  có ô tích.

Kết quả: test 97/97 (máy dev + Sandbox). GUI Sandbox 4 trang: tách theo cột city → 3 file đúng; bỏ trùng cả dòng (bỏ 2) và
theo cột (còn 3 + tiêu đề); chọn cột city, id + đổi sang ";" đúng thứ tự; , → Tab ra .tsv. Ảnh chụp lộ 1 lỗi bố cục (trang
Chọn cột: tuỳ chọn dấu phân cách bị cắt ở cột nút) → chuyển xuống hàng riêng.

## Đợt 5 (2026-09-30): So sánh 2 file, Theo dõi log, Thay thế hàng loạt, Tìm file trùng, Mẫu (preset)

Quyết định thiết kế:
- **So sánh (`TextDiff`)**: mỗi dòng chỉ giữ mã băm 64-bit; cắt phần đầu / cuối giống nhau rồi mới chạy Myers (chép từ
  ImageCompare.Engine.RowAligner) cho phần giữa. Vết Myers tăng theo D² nên giới hạn 4.000 bước sửa; quá thì so như tập hợp
  nhiều phần tử (dòng chỉ có ở A / ở B, không xét thứ tự) thay vì treo. Cụm hiển thị được dựng ngay khi duyệt kịch bản,
  chỉ giữ ≤ N dòng giống gần nhất - đoạn giống dài không bao giờ nằm cả trong RAM; chỉ đọc lại nội dung các dòng cần hiện.
- **Theo dõi log (`LogTailer`)**: đọc định kỳ theo độ dài file (0,5 s), không dùng FileSystemWatcher (không đáng tin với file
  đang mở ghi liên tục). Dòng chưa có xuống dòng giữ lại tới lần sau; "\r" cuối lần đọc + "\n" đầu lần sau = 1 xuống dòng;
  file nhỏ lại → xoay vòng → đọc lại từ đầu; file bị xoá → báo, chờ.
- **Thay thế hàng loạt** tái dùng `FileRewriter` (thêm `Transform` theo dòng) nên được luôn: giữ encoding / xuống dòng, ghi đè
  giữ `.bak`, file không có chỗ thay không bị đụng. Chữ thường thì "$" trong chuỗi thay là chữ, không phải nhóm regex.
- **Tìm file trùng**: dung lượng → 64 KB đầu → toàn bộ (XxHash128), hầu hết file chỉ phải đọc 64 KB. Xoá = chuyển vào Thùng
  rác qua `SHFileOperation` + FOF_ALLOWUNDO (khôi phục được; app WinUI không có Microsoft.VisualBasic.FileIO), có hộp thoại
  xác nhận, chặn trường hợp tích hết cả nhóm.
- **Mẫu**: 1 thanh chung ở đầu cửa sổ gắn vào ViewModel của trang đang mở; tuỳ chọn chụp bằng reflection (thuộc tính public
  kiểu string / bool / int / double / enum, trừ trạng thái chạy) + thuộc tính lồng "Output"; đường dẫn nguồn được áp trước
  (đổi nguồn làm trang tự đặt lại đường dẫn kết quả). JSON hỏng → coi như chưa có mẫu.

Kết quả: test 120/120 (máy dev + Sandbox). GUI Sandbox: so sánh (sửa dòng 10, xoá dòng 40 → đúng 2 cụm, đúng số dòng A/B),
theo dõi log (lọc "loi" không dấu, dòng ghi dở hiện khi xong), thay thế (xem trước 3 chỗ / 2 file, file Shift-JIS vẫn là
Shift-JIS, `.bak`), file trùng (bản thừa vào Thùng rác), mẫu (lưu → đổi → nạp lại đúng). Lỗi tìm ra và đã sửa: nút "Xem
trước" không sáng khi nhập chữ cần tìm trước rồi mới thêm file (nút chỉ kiểm lại khi ô Tìm đổi).

## Cửa sổ Hướng dẫn (2026-09-30)

- Cùng bố cục với cửa sổ Hướng dẫn của ScreenCapture (danh mục trái, nội dung phải, tìm không dấu) - chép sang vì module
  không reference nhau. Mở bằng mục "Hướng dẫn (F1)" ở chân menu (SelectsOnInvoked=False → không chuyển trang), nút ? hoặc
  F1; luôn mở đúng mục của trang đang xem; chỉ 1 cửa sổ (mở lại thì đổi mục + đưa lên trước).
- Nội dung ở `Core\HelpContent.cs` (không phụ thuộc WinUI) để unit test được: **mọi `*Page.xaml` phải có đúng 1 mục** -
  thêm trang mới mà quên viết hướng dẫn là test đỏ; tìm kiếm dùng lại `TextMatcher.FoldDiacritics`.
- Sandbox: nút ? trên trang Tách theo cột mở đúng mục; tìm "thung rac" ra Tìm file trùng; mục chân menu khi đang mở sẵn đổi
  sang mục So sánh (vẫn 1 cửa sổ); F1 trên trang Tìm mở mục Tìm. Lỗi nhỏ đã sửa: danh mục không cuộn tới mục đang chọn
  (ScrollIntoView gọi trước khi danh sách bố cục xong).

## Bổ sung (2026-10-05): file đang làm dùng chung + gộp 8 trang thành 4
Người dùng: "có một số tính năng có thể gom chung, khi chuyển trang thì phải chọn lại file, cực quá".
- **File đang làm dùng chung** (`ViewModels/SharedFile.cs`): mọi chỗ chọn file (Chọn..., kéo-thả, gõ đường dẫn, nạp mẫu)
  ghi `SharedFile.Current` (chỉ file có thật); `MainWindow.ContentFrame_Navigated` đưa vào trang vừa mở qua
  `IUsesSharedFile.ApplySharedFile`. Người dùng chọn "luôn đổi theo file mới nhất". Ngoại lệ: trang đang chạy giữ file cũ;
  Theo dõi log đang theo dõi giữ file cũ; danh sách nhiều file (Nối, Đổi encoding, Thay thế) chỉ tự thêm khi trống (thêm
  đúng 1 file vào danh sách thì file đó thành file chung); So sánh 2 file điền ô A (trừ khi file đó đang ở ô B).
- **Gộp trang** (16 → 12): Thông tin file + Trích dòng → *Xem file* (`InfoPage`); Tìm + Lọc dòng → *Tìm / Lọc dòng*
  (`SearchPage`, ô từ khoá chung nhiều dòng - Tìm cũ chỉ 1 chữ; Enter = Tìm bắt ở `PreviewKeyDown` vì ô nhiều dòng nuốt
  Enter, Shift+Enter = thêm dòng); Đổi encoding + Đổi xuống dòng → *Đổi encoding / xuống dòng* (`EncodingPage` - khối Đầu
  ra vốn có ô xuống dòng + "Giữ như file nguồn", nên chỉ đổi mô tả / cột xem trước); Chọn / sắp cột + Đổi dấu phân cách →
  *CSV: Chọn cột / đổi dấu phân cách* (`ColumnsPage`; mọi cột đúng thứ tự gốc → `Columns = null` để giữ cả ô thừa như
  trang Đổi dấu phân cách cũ; Tab → đuôi .tsv). Giữ tên class trang cũ còn lại để mẫu + mục Hướng dẫn không đổi khoá.
- **Mẫu đã lưu** của 4 trang bị bỏ chuyển sang trang mới lúc mở app (`PresetMigration` trong `Core/Presets.cs`): Lọc dòng
  giữ nguyên tên tuỳ chọn; Tìm `Query` → `Terms`; Đổi xuống dòng `TargetIndex` 0/1 → `Output.NewlineIndex` 1/2 +
  `Output.EncodingIndex` = Giữ như nguồn; Đổi dấu phân cách `TargetIndex` 0..3 → `OutputDelimiterIndex` 1..4; trùng tên
  thì thêm "(tên trang cũ)".
- Kiểm: unit test 132/132 (thêm test chuyển mẫu, 2 mục tìm Hướng dẫn); build 0 warning. GUI chưa test.
