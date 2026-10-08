# FileTools

Xử lý file text / CSV / log **lớn** (vài trăm MB tới vài GB) mà Excel / Notepad / CsvEditor không mở nổi. Mọi
thao tác đọc kiểu stream (RAM không tăng theo cỡ file), chạy nền, có tiến độ + nút **Huỷ**; huỷ giữa chừng không
để lại file dở. Chỉ xử lý file chữ, không xử lý file nhị phân.

## Chạy độc lập (không cần MainLauncher)

Mở `Modules\FileTools\FileTools.csproj` → Set as Startup Project → F5, hoặc:

```powershell
dotnet run --project Modules\FileTools\FileTools.csproj -p:Platform=x64
```

## Tính năng (đủ 5 đợt theo PLAN.md; 2026-10-05 gộp 8 trang thành 4)

| Trang | Làm gì |
|---|---|
| **Nối file** | Chọn thư mục + mẫu lọc (`*.txt;*.csv;*.log`), có / không thư mục con → danh sách file (sắp theo tên hiểu số `file2` trước `file10` / ngày sửa / dung lượng, hoặc tự đổi thứ tự) → nối thành 1 file. Tuỳ chọn: chèn tên file trước mỗi phần, dòng trống giữa 2 file, CSV giữ dòng tiêu đề 1 lần (file nào có tiêu đề khác thì giữ lại và báo trong nhật ký). File trước không kết thúc bằng xuống dòng thì tự thêm. |
| **Tách file** | Theo dung lượng mỗi phần (MB), số dòng mỗi phần, hoặc số phần (chia đều). Không bao giờ cắt ngang 1 dòng; file `.csv/.tsv` đọc theo bản ghi (ô trong ngoặc kép có xuống dòng không bị cắt) và lặp dòng tiêu đề ở mọi phần. Tên: `ten.part001.csv`... trong thư mục `ten_parts`. |
| **Xem file** (gộp Thông tin file + Trích dòng) | **Thông tin**: 1 lượt đọc: dung lượng, encoding, số dòng / dòng trống, kiểu xuống dòng (CRLF / LF / lẫn lộn), dòng cuối có xuống dòng không, dòng dài nhất. File dạng bảng (đuôi .csv/.tsv hoặc đoán được): dấu phân cách, số cột + tên cột, số bản ghi, các bản ghi lệch số cột so với dòng tiêu đề. Chọn file là tự phân tích. **Trích dòng**: N dòng đầu / dòng X–Y / N dòng cuối → file tạm `%TEMP%\AppSuite\FileTools\` rồi mở (CSV mở bằng CsvEditor nếu có cùng bộ deploy). Tuỳ chọn kèm dòng tiêu đề. N dòng cuối đọc ngược từ cuối file nên file vài GB vẫn tức thì. |
| **Tìm / Lọc dòng** (gộp Tìm + Lọc dòng) | Ô từ khoá chung (mỗi dòng 1 từ, chứa 1 trong / tất cả; regex dùng dòng đầu); mặc định không phân biệt hoa-thường và **không phân biệt dấu** ("thanh toan" khớp "Thanh toán", đ = d). **Tìm**: hiện tối đa N dòng khớp (vẫn đếm hết), dòng rất dài chỉ hiện đoạn quanh chỗ khớp; tuỳ chọn xuất mọi dòng khớp (kèm số dòng) ra file tạm. Enter = Tìm, Shift+Enter = thêm dòng từ khoá. **Lọc ra file**: giữ hoặc bỏ các dòng khớp → file mới; CSV lọc theo bản ghi, tuỳ chọn luôn giữ dòng tiêu đề. |
| **Đổi encoding / xuống dòng** (gộp 2 trang cũ) | Hàng loạt: thêm file / thư mục (mẫu lọc, thư mục con) / dán đường dẫn; cột "Encoding · xuống dòng hiện tại" xem trước. Encoding đích và kiểu xuống dòng (CRLF ↔ LF) chọn ở khối Đầu ra - cái nào không đổi thì "Giữ như nguồn"; file đã đúng thì bỏ qua. Đọc nguồn tự nhận hoặc ép (UTF-8 / Shift-JIS / UTF-16 / Windows-1258). Ký tự không có trong encoding đích (vd chữ Việt → Shift-JIS) được **đếm và báo** trong nhật ký. |
| **CSV: Tách theo cột** | Mỗi giá trị của 1 cột → 1 file `ten_<giá trị>.csv` (vd mỗi mã khách hàng / mỗi tháng), giữ thứ tự bản ghi, lặp dòng tiêu đề. Giá trị chỉ khác hoa-thường hoặc có ký tự cấm trong tên file vẫn ra file riêng; quá 5.000 giá trị thì dừng (thường là chọn nhầm cột). Mở tối đa 64 file cùng lúc, file ít dùng được đóng / mở lại ghi tiếp. |
| **CSV: Bỏ dòng trùng** | Giữ lần xuất hiện đầu, giữ thứ tự; so cả dòng hoặc theo các cột được tích, tuỳ chọn không phân biệt hoa-thường / bỏ khoảng trắng. Dùng được cả cho file text / log thường. Chỉ nhớ mã băm 64-bit của mỗi dòng (~11 byte). |
| **CSV: Chọn cột / đổi dấu phân cách** (gộp 2 trang cũ) | Tích cột cần giữ, đổi thứ tự bằng Lên / Xuống, và / hoặc đổi , ↔ Tab ↔ ; ↔ | khi ghi ra (Tab → đuôi .tsv); bọc mọi ô trong ngoặc kép. Để nguyên mọi cột như gốc = chỉ đổi dấu phân cách (giữ cả ô thừa của bản ghi dài hơn tiêu đề); ô chứa dấu phân cách mới / ngoặc kép / xuống dòng tự được bọc ngoặc kép. |
| **So sánh 2 file** | Dòng thêm / bớt / sửa (đỏ = chỉ có ở A, xanh = chỉ có ở B) kèm N dòng ngữ cảnh, số dòng ở cả 2 file; tuỳ chọn bỏ qua hoa-thường / khoảng trắng; 2 file khác encoding vẫn so theo chữ. Báo cáo HTML đầy đủ ở thư mục tạm. 2 file khác nhau quá nhiều (> 4.000 dòng sửa) → liệt kê dòng chỉ có ở 1 bên (không xét thứ tự) thay vì treo. |
| **Theo dõi log** | Như `tail -f`: N dòng cuối rồi tự thêm dòng mới (đọc mỗi 0,5 s, không cản chương trình đang ghi); dòng đang ghi dở chỉ hiện khi xong; file bị xoá nội dung / xoay vòng → đọc lại từ đầu; lọc theo từ khoá (không dấu), tự cuộn / tạm dừng cuộn, lưu các dòng đang hiện. |
| **Thay thế hàng loạt** | Chữ / regex (`$1`...), phân biệt hoa-thường, chỉ nguyên từ; **Xem trước** đếm số chỗ từng file (chưa ghi gì) rồi mới **Thay thế**. Giữ encoding + xuống dòng từng file; ghi ra thư mục khác hoặc ghi đè giữ `.bak`; file không có chỗ thay không bị đụng tới. |
| **Tìm file trùng** | File cùng nội dung (không cần cùng tên) trong 1 hoặc nhiều thư mục: gom theo dung lượng → băm 64 KB đầu → băm toàn bộ (XxHash128). Tích các bản thừa (nút "giữ bản đầu mỗi nhóm") rồi chuyển vào **Thùng rác** (khôi phục được) - luôn giữ ≥ 1 bản / nhóm. |
| **File đang làm dùng chung** | Chọn / kéo-thả / gõ đường dẫn file ở 1 trang thì mở trang khác file đó đã điền sẵn (luôn theo file chọn gần nhất; `ViewModels/SharedFile.cs`). Trang danh sách nhiều file (Nối file, Đổi encoding, Thay thế) chỉ tự thêm khi danh sách trống; So sánh 2 file điền ô A; trang đang chạy / Theo dõi log đang theo dõi giữ file cũ. |
| **Mẫu (preset)** | Thanh "Mẫu" ở đầu cửa sổ: lưu / nạp / xoá bộ tuỳ chọn của trang đang mở (`Data\Config\presets.json` cạnh exe).. Mẫu của 4 trang đã gộp tự chuyển sang trang mới lần đầu mở bản này (`PresetMigration`). |
| **Hướng dẫn** | Mục "Hướng dẫn (F1)" cuối menu, nút **?** trên cùng hoặc phím **F1** (mở thẳng phần của trang đang xem): từng trang dùng khi nào, các bước, ý nghĩa tuỳ chọn, lưu ý; tìm không dấu ("tach cot" ra Tách theo cột). Cửa sổ dùng chung `SharedUI.Help.HelpWindow`; nội dung ở `Core\HelpContent.cs` - thêm / đổi tính năng thì cập nhật cả README này. |

Các trang CSV đọc theo **bản ghi** (ô trong ngoặc kép có xuống dòng không bị cắt); dấu phân cách tự nhận hoặc chọn tay,
tuỳ chọn "Dòng 1 là tiêu đề".

Đổi encoding / xuống dòng ghi ra thư mục khác (mặc định `converted`) hoặc **ghi đè file gốc** - khi ghi đè, bản cũ
giữ thành `<tên>.bak`, file không có gì thay đổi thì không đụng tới. 1 file lỗi không dừng cả lô.

Chung cho mọi trang: kéo-thả file / thư mục vào trang; **Encoding đầu ra** (mặc định UTF-8; UTF-8 có BOM /
Shift-JIS / UTF-16 LE / giữ như nguồn) và **Xuống dòng** (giữ như nguồn / CRLF / LF); nhật ký thao tác; nút **Mở kết
quả** / **Mở thư mục**. Encoding file nguồn tự nhận: BOM → UTF-16 không BOM → UTF-8 → Shift-JIS.


Tốc độ đo trên file 2 GB (25,9 triệu dòng CSV tiếng Nhật / Việt, máy dev; số dao động theo tải máy): trích 1.000 dòng
đầu / cuối < 0,1 s; thông tin file 11–19 s; tìm không dấu 14–26 s; lọc dòng 10–20 s; tách 10 phần 14–25 s; nối lại
8–16 s; chọn cột + đổi dấu phân cách 32 s; bỏ trùng theo 1 cột 28 s. Các thao tác trên dùng ≤ 80 MB RAM; riêng bỏ trùng
**cả dòng** phải nhớ mã băm từng dòng: 25,9 triệu dòng khác nhau → 20 s, 362 MB.

## Kiến trúc

- `Core\` - logic thuần, không phụ thuộc WinUI: `EncodingSniffer`, `LineReader` (đọc dòng **giữ nguyên** CRLF/LF/CR,
  đếm đúng số byte đã đọc), `CsvRecordReader` + `Csv`, `OutputFile` (ghi vào `.partial` rồi mới đổi tên → huỷ / lỗi
  không để lại file dở; đếm ký tự không biểu diễn được), `FileListing` + `NaturalComparer`, `FileMerger`,
  `FileSplitter`, `LineExtractor`, `FileInspector`, `TextMatcher` (chữ / regex / bỏ dấu qua bảng tra 65K ký tự) dùng
  chung cho `TextSearcher` + `LineFilter`, `FileRewriter` (lõi chung của Đổi encoding + Đổi xuống dòng + Thay thế hàng loạt, qua
  `BatchReplacer`), `CsvLayout` / `CsvTransformer` / `CsvColumnSplitter` / `CsvDeduplicator` + `UInt64Set`, `TextDiff` (Myers
  trên mã băm dòng), `LogTailer`, `DuplicateFinder`, `PresetStore` + `PresetMapper`, `ProgressThrottle`.
- `ViewModels\` - `JobViewModel` (chạy nền, tiến độ, Huỷ, nhật ký, mở kết quả), `SourceFileViewModel` (trang 1 file),
  `BatchViewModel` (trang hàng loạt) + 1 ViewModel / trang.
- `Views\` - 1 Page / tính năng (`NavigationCacheMode=Required`: đổi trang không mất trạng thái / thao tác đang chạy),
  `JobPanel` + `OutputPanel` + `BatchFilesPanel` dùng chung.
- `Services\` - `Pickers` (hộp thoại gắn hwnd), `Shell` (mở kết quả), `AppLog` (`Logs\filetools-yyyy-MM-dd.log`, giữ 14 ngày).
- Chỉ reference `Common` + `SharedUI`; không đọc `modules.json`.

## Unit test

`Tests\FileTools.Tests` (xUnit v3) - 128 test: nhận encoding (UTF-8 / BOM / UTF-16 LE-BE / Shift-JIS / không BOM /
mẫu cắt giữa ký tự), đọc dòng (mọi kiểu xuống dòng, CRLF nằm vắt qua 2 buffer, dòng rất dài, số byte đã đọc), ghi an
toàn, CSV nhiều dòng trong ngoặc kép, nối (thứ tự tên có số, encoding lẫn lộn, tiêu đề CSV, huỷ), tách (ghép lại
đúng bản gốc, không vượt dung lượng, đúng N phần, CSV, huỷ xoá hết phần), trích (đầu / khoảng / cuối, UTF-16,
Shift-JIS, file rỗng / 1 dòng), so khớp (bỏ dấu giữ đúng vị trí, 1 trong / tất cả, regex lỗi), thông tin file (xuống
dòng lẫn lộn, CSV lệch cột), tìm (giới hạn kết quả, dòng rất dài, xuất kèm số dòng), lọc (giữ / bỏ, CSV nhiều dòng),
đổi encoding / xuống dòng (ra thư mục, ghi đè + .bak, không đổi thì bỏ qua, đếm ký tự mất khi sang Shift-JIS, 1 file
lỗi không dừng lô), CSV (chọn / sắp cột, đổi dấu phân cách, tách theo cột kể cả > 64 giá trị phải đóng / mở lại file, tên
file trùng hoa-thường, bỏ trùng cả dòng / theo cột), `UInt64Set`, so sánh (sửa / thêm / bớt, gộp cụm gần nhau, bỏ qua hoa-thường
/ khoảng trắng, khác encoding, quá khác → so như tập hợp, báo cáo HTML), thay thế (xem trước không ghi, giữ Shift-JIS +
xuống dòng + .bak, `$` chữ thường, nhóm regex, nguyên từ), theo dõi log (dòng ghi dở, CRLF vắt 2 lần đọc, xoay vòng, file
bị xoá), file trùng (khác sau 64 KB, file rỗng), mẫu (chụp / áp, bỏ giá trị lỗi, lưu / ghi đè / xoá, file JSON hỏng).

```powershell
dotnet test Tests\FileTools.Tests
# File ~2 GB thật (chạy tay, cần ~6 GB đĩa trống; đổi cỡ bằng FILETOOLS_LARGE_MB):
dotnet run --project Tests\FileTools.Tests -- -explicit only
```

## Vì sao độc lập được với MainLauncher?

- Không `ProjectReference` tới `MainLauncher.csproj`; không đọc `modules.json` hay cấu hình nào của launcher.
- Mở CsvEditor (nếu có) qua quy ước thư mục deploy `..\CsvEditor\CsvEditor.exe`, không qua launcher; không có thì
  mở bằng ứng dụng mặc định.
