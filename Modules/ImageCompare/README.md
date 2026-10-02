# ImageCompare — So sánh ảnh

So khớp 2 hình ảnh (ảnh chụp màn hình, file PNG/JPG, ảnh xuất từ tool khác): tìm chỗ khác nhau, xem
trực quan, tìm ảnh con trong ảnh lớn, đánh giá độ giống — rồi xuất ảnh / báo cáo HTML gửi người khác.
Kèm **Tìm chữ**: đọc chữ trong 1 ảnh (OCR tiếng Việt / tiếng Anh) và tìm chữ trong đó; **So chữ**: đọc chữ cả 2
ảnh (tiếng Nhật / Việt / Anh) và liệt kê chữ / giá trị khác nhau — cho 2 ảnh cùng 1 màn hình chụp ở môi trường
khác (font, trình duyệt) mà so pixel tô đỏ gần hết.

## Chạy

- Độc lập: mở `Modules\ImageCompare\ImageCompare.csproj`, đặt làm Startup Project, F5 (hoặc
  `dotnet run --project Modules\ImageCompare\ImageCompare.csproj -p:Platform=x64`).
- Qua MainLauncher: entry `ImageCompare` trong `MainLauncher/Config/modules.json`.
- Dòng lệnh: `ImageCompare.exe [ảnh A] [ảnh B]` mở sẵn 2 ảnh.
- **Hướng dẫn**: nút `?` góc trên-phải hoặc `F1` — mở đúng phần của chế độ đang xem, tìm không dấu. Nội dung ở
  `Models/HelpContent.cs` (cửa sổ dùng chung `SharedUI.Help.HelpWindow`) — thêm / đổi tính năng thì cập nhật cả ở đó.

## Đưa ảnh vào

- 2 ô **A** (ảnh gốc) và **B** (ảnh mới) ở trên cùng: nút *Mở…* / *Dán*, hoặc kéo-thả file vào ô.
- Kéo-thả vào vùng xem: thả 2 file cùng lúc = A và B; thả 1 file ở chế độ *Cạnh nhau* = nửa trái → A,
  nửa phải → B; chế độ khác → A nếu còn trống, không thì B.
- `Ctrl+V`: dán ảnh trong clipboard (ảnh copy từ ScreenCapture / app khác, hoặc file ảnh copy trong
  Explorer) vào A nếu còn trống, không thì B (2 ô đều có ảnh → thay B). `Ctrl+Shift+V`: dán thẳng vào A.
- **Thay ảnh** ở 1 ô: *Mở…* / *Dán* ngay trong ô đó, hoặc kéo-thả file vào ô. **Bỏ ảnh**: nút ✕ trên ô
  (chỉ hiện khi ô có ảnh).
- Nút ⇄ đổi chỗ A và B.

## Chế độ xem

| Chế độ | Nội dung |
|---|---|
| **Khác biệt** | Ảnh B làm nhạt, pixel khác tô **đỏ**, mỗi vùng khác có khung + số. Bảng bên phải: kết luận, % pixel giống, SSIM, danh sách vùng (bấm → phóng tới vùng, khung vàng). |
| **Cạnh nhau** | A trái, B phải — zoom / cuộn đồng bộ. |
| **Chồng mờ** | B đặt chồng lên A, thanh trượt độ trong suốt A ↔ B. |
| **Thanh trượt** | Vạch chia kéo được: trái là A, phải là B. |
| **Tìm ảnh con** | Tìm ảnh nhỏ hơn (vd 1 nút, 1 icon cắt ra) trong ảnh lớn hơn; khung xanh + % khớp, danh sách chỗ tìm thấy (khớp nhất trước). Thanh *Độ khớp tối thiểu* (mặc định 90%). |
| **Tìm chữ** | Đọc chữ trong **1 ảnh** (chọn *Ảnh A* / *Ảnh B*; chỉ cần 1 ảnh). Ô tìm trống: liệt kê mọi dòng đọc được (khung xanh mảnh). Gõ chữ: các chỗ khớp tô **vàng** + số, danh sách bên phải (bấm → phóng tới). *Copy toàn bộ chữ*: chữ đọc được, mỗi dòng 1 dòng. |
| **So chữ** | Đọc chữ **cả 2 ảnh**, A trái / B phải, khung màu quanh chỗ khác: **đỏ** đổi chữ, **xanh** chỉ có ở A, **cam** chỉ có ở B, **tím** khác màu chữ (vd ô bị khoá chữ xám ↔ chữ đen). Danh sách bên phải dạng `「A」→「B」` (bấm → phóng tới chỗ đó trên cả 2 ảnh). |

Ở chế độ **Khác biệt**, nếu 1 vùng khác phủ ≥ 60% ảnh (2 ảnh lệch bố cục — khác font / trình duyệt), bảng kết quả
nhắc thử *So chữ*; số liệu so pixel không đổi.

### So chữ

- Chọn ngôn ngữ chữ trên màn hình: **Tiếng Nhật** (mặc định) hoặc **Tiếng Việt / English**.
- Ghép chữ theo vị trí, chịu được bố cục xê dịch dần (font / độ rộng ô khác nhau) và OCR cắt đoạn khác nhau ở 2 ảnh;
  toàn / nửa độ rộng (`ｵｰﾊﾞｰ` = `オーバー`) và các ký tự OCR hay nhầm (`0`/`ロ`/`O`, `-`/`ー`, `,`/`.`…) coi là giống.
- **Gần giống** (ẩn mặc định, tick *Hiện gần giống*): lệch ít ký tự mà chữ số giống hệt — thường do OCR đọc lệch
  nhãn. Chữ số khác (giá trị, ngày, số tiền) luôn báo là *đổi chữ*, dù chỉ lệch 1 số.
- *Vùng bỏ qua* vẽ ở chế độ Khác biệt cũng áp dụng ở đây (vd bỏ thanh tiêu đề / URL trình duyệt).
- **Kiểm tra lại từng chỗ**: sau khi so, mỗi chỗ nghi khác được cắt riêng ở cả A và B rồi đọc lại vài lần (mức phóng,
  ngưỡng đen trắng, cả Windows OCR lẫn Tesseract) — có cách đọc trùng nhau thì là chữ giống (OCR cả trang đọc sai 1
  phía), bỏ khỏi danh sách; còn khác thì hiện cặp cách đọc sát nhau nhất thay cho chữ rác. Bảng kết quả ghi số chỗ đã
  bỏ theo cách này. Thanh tiến độ: *Đang đọc chữ…* → *Đang kiểm tra lại từng chỗ…*.
  Đọc lại ra giống nhưng màu nét khác → *khác màu chữ*; chỗ OCR bỏ sót hẳn 1 phía thì so hình nét chữ (không OCR).
- **Xuất kết quả**: nút *Copy* (cả danh sách, tách cột bằng Tab — dán thẳng vào Excel), *Lưu CSV* (UTF-8 có BOM,
  mở bằng Excel), *Xuất báo cáo HTML* (1 file: bảng các chỗ khác kèm ảnh cắt A | B từng chỗ). Cột: #, loại, chữ A, chữ B,
  ghi chú màu, vị trí A / B (mục chỉ có ở 1 phía: vị trí phía kia là chỗ dự đoán, ghi `≈`). Chỉ xuất các mục đang hiện
  (theo *Hiện gần giống*). **Chuột phải** 1 mục trong danh sách (mọi chế độ): *Copy mục này* / *Copy cả danh sách*.
- **Chữ đọc từ ảnh có thể sai** — danh sách là "các chỗ cần soi lại", không phải kết luận cuối cùng. Thử trên 2 ảnh
  màn hình nghiệp vụ tiếng Nhật (cùng dữ liệu, IE ↔ Edge): Windows OCR ghép được 158 đoạn giống, báo 26 chỗ khác
  (gồm 9 chỗ khác màu chữ thật — ô bị khoá chữ xám ↔ chữ đen; 7 chỗ là thanh tiêu đề / URL trình duyệt); còn vài chỗ
  font bitmap của ảnh cũ bị đọc sai.
- **OCR tiếng Nhật** dùng Windows OCR (nhanh, chính xác hơn). Máy chưa có gói OCR tiếng Nhật của Windows thì tự dùng
  Tesseract (kém hơn rõ — nhiều mục báo nhầm hơn; bảng kết quả ghi rõ đang dùng gì). **Cài gói OCR tiếng Nhật**:
  Settings → Time & language → Language & region → thêm / mở *日本語 (Japanese)* → *Language options* → cài
  *Optical character recognition* (cần mạng). Hoặc PowerShell (Administrator):
  `Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"`. Windows tiếng Nhật thường có sẵn.
- Tốc độ: 1 màn hình ~0,5 s mỗi ảnh (Windows OCR), ~0,5–1 s (Tesseract) + kiểm tra lại ~1,5–2 s. Mỗi ảnh chỉ đọc cả
  trang 1 lần cho mỗi ngôn ngữ.

### Tìm chữ

- `Ctrl+F`: sang chế độ Tìm chữ và đặt con trỏ vào ô tìm. Ảnh vừa mở / dán / kéo-thả ở chế độ này được đọc luôn.
- Tìm được cụm nhiều từ; mặc định **không phân biệt hoa / thường và dấu** (`thanh toan` khớp `Thanh toán`) —
  bật *Phân biệt dấu* / *Phân biệt hoa/thường* khi cần. Khoảng trắng không tính khi so (OCR hay dính từ).
- Đọc tiếng Việt có dấu và tiếng Anh / code / đường dẫn; giao diện nền tối, chữ trắng trên nút / tiêu đề màu.
  Chữ đọc từ ảnh có thể sai vài ký tự → không thấy thì thử tìm đoạn ngắn hơn.
- Tốc độ: 1 màn hình ~0,5 s; trang cuộn dài 10.000 px ~3–4 s (hiện % tiến độ). Mỗi ảnh chỉ đọc 1 lần (đổi A ↔ B
  hay chuyển chế độ rồi quay lại không phải chờ).
- Dùng Tesseract (chạy offline, không gửi ảnh đi đâu); không chạy trên máy ARM64.

Mọi chế độ: `Ctrl` + lăn chuột = zoom quanh con trỏ, lăn = cuộn dọc, `Shift` + lăn = cuộn ngang, kéo chuột
(phải / giữa, hoặc trái ở chế độ không dùng chuột trái) = cuộn; *Vừa cửa sổ* (`Ctrl+0`), *100%*
(`Ctrl+1`). Từ 200% trở lên ảnh vẽ không nội suy để thấy rõ từng pixel. Thanh trạng thái hiện toạ độ + màu
pixel ở A và B dưới con trỏ.

## Tuỳ chọn (chế độ Khác biệt)

- **Căn chỉnh**
  - *Tự căn chỉnh* (mặc định): tự tìm độ lệch (dx, dy) — ảnh chụp lệch vài px, khác lề, trang cuộn 1 đoạn.
  - *Căn theo dòng (trang dài)*: trang dài mà B **thêm / bớt 1 đoạn ở giữa** so với A (tự căn dịch chuyển
    chỉ khớp được 1 phần). Kết quả là ảnh ghép: dải **cam** = chỉ có ở B (thêm vào), dải **xanh** = chỉ có ở
    A (bị bỏ), phần còn lại so từng pixel. 2 ảnh khác nhau quá nhiều thì tự lui về *Tự căn chỉnh*.
  - *Không căn*: trùng góc trên-trái.
  - *Chỉnh tay*: `Alt` + phím mũi tên dịch B 1 px (`Alt+Shift` + mũi tên: 10 px).
- **Ngưỡng** (0–50%, mặc định 8%): chênh lệch màu tối thiểu để tính là khác. Ảnh PNG chụp màn hình: 5–10%.
  **Ảnh JPG: ~20%** (nhiễu nén quanh chữ hay bị tính là khác — bảng kết quả sẽ nhắc).
- **Bỏ qua răng cưa**: không tính khác biệt chỉ do viền chữ / hình vẽ mịn lệch nhẹ. Ảnh bị dịch lẻ pixel
  (vẽ lại cả trang) thì vẫn còn nhiễu — tăng ngưỡng.
- **Vùng bỏ qua**: bật nút rồi kéo chuột trái trên ảnh để khoanh vùng không so (đồng hồ, ngày giờ, avatar…),
  bấm vào 1 vùng bỏ qua để xoá; nút 🗑 xoá hết. Không dùng được khi *Căn theo dòng*.

Phần 2 ảnh không chồng lên nhau (khác kích thước / bị dịch) tô **sọc**: xanh = chỉ có ở A, cam = chỉ có ở B.

## Xuất kết quả (bảng bên phải, chế độ Khác biệt)

- *Copy* / *Lưu PNG*: ảnh khác biệt 1:1 (có khung đánh số).
- *Xuất báo cáo HTML*: 1 file tự chứa (ảnh nhúng sẵn) — kết luận, thông tin 2 ảnh, tuỳ chọn, % giống,
  bảng từng vùng khác kèm ảnh cắt A | B, ảnh khác biệt, 2 ảnh gốc. Mở bằng trình duyệt ở máy nào cũng được.

## Kiến trúc

- `Engine/` — lõi so sánh, không phụ thuộc WinUI (chỉ SkiaSharp), test được bằng console:
  `Aligner` (tự căn dịch chuyển), `RowAligner` (căn theo dòng, diff Myers trên mã băm từng dòng),
  `PixelDiff` (so pixel, bỏ qua răng cưa kiểu pixelmatch, gom vùng), `Similarity` (SSIM),
  `TemplateMatcher` (tìm ảnh con, NCC thô → tinh), `ImageComparer` (điểm vào), `IDiffView` +
  `DiffPainter` / `RowDiffView` (vẽ / xuất kết quả — dùng chung cho màn hình và báo cáo), `HtmlReport`,
  `TextRecognizer` (OCR Tesseract: phóng ×2, cắt dải đọc song song, đảo màu nền tối) + `TextSearch`;
  So chữ: `FormPreprocess` (phóng ×3, Otsu, xoá viền ô, tách cụm chữ), `IFormTextReader` +
  `TesseractFormReader` (đọc từng cụm), `TextDiff` (tách đoạn, khoá so, ghép theo độ lệch cục bộ, màu chữ),
  `TextDiffVerifier` (đọc lại riêng từng chỗ nghi khác, song song), `TextDiffReport` (xuất Tab / CSV / HTML).
- `Services/FormReaders` — chọn bộ đọc cho So chữ; `WindowsFormReader` (Windows.Media.Ocr — cần WinRT nên nằm ở app,
  không ở Engine).
- `Ocr/` — dữ liệu OCR `tessdata\vie.traineddata` + `jpn.traineddata` (tessdata_fast; jpn chỉ dùng làm dự phòng của
  So chữ tiếng Nhật) và VC++ runtime chép kèm
  (`vcruntime\win-x64|win-x86`, cần cho `tesseract50.dll` trên máy chưa cài VC++ Redistributable); build chép
  ra `tessdata\` và cạnh exe.
- `ViewModels/CompareViewModel` — trạng thái + chạy so sánh / tìm ở nền (huỷ khi đổi ảnh / tuỳ chọn).
- `MainWindow` — canvas `SKXamlCanvas`, zoom / cuộn, kéo-thả, hộp thoại, clipboard.
- `Services/` — `ImageFileService`, `ClipboardService`, `AppLog` (chép từ ScreenCapture — module không
  reference nhau). Log: `Logs\imagecompare-yyyy-MM-dd.log` cạnh exe, giữ 14 ngày.
- Chỉ reference `Common` + `SharedUI`; không DI container (giống các module khác).

## Unit test

`Tests\ImageCompare.Tests` (xUnit v3) - 68 test cho `Engine/`, ảnh thử dựng bằng SkiaSharp ngay trong test (không cần
file mẫu): **so pixel** (giống hệt, đúng số vùng + khung ôm sát, ngưỡng màu, vùng bỏ qua, khác kích thước, huỷ),
**tự căn** (dịch ngang/dọc, B rộng hơn A, chỉnh tay), **căn theo dòng** (B thêm / bỏ 1 đoạn, lui về tự căn khi 2 ảnh
khác hẳn), **tìm ảnh con** (đúng vị trí, tự đổi chiều, nhiều bản sao, không có), **tìm chữ** (không dấu, hoa/thường,
bỏ khoảng trắng, đ → d), **OCR thật** (chữ tiếng Việt vẽ bằng Segoe UI trên nền sáng / tối), **báo cáo HTML**,
**so chữ** (khoá so toàn/nửa độ rộng + ký tự hay nhầm, tách đoạn, bố cục trôi dần, đổi giá trị, gần giống vs đổi
chữ số, chỉ A / chỉ B, OCR cắt đoạn khác nhau, nhãn lặp, vùng bỏ qua, màu chữ xám ↔ đen), **xử lý ảnh form** (xoá
viền ô, tách cụm, chữ xám nét mảnh không mất, nền sáng không thành mực), **kiểm tra lại** (bỏ chỗ đọc lại thì giống, giữ
đổi thật kể cả khi 2 phía cùng dính nhãn bên cạnh, hiện cặp đọc sát nhau nhất), **đọc form bằng Tesseract** (tiếng Nhật / tiếng Việt, đầu-cuối: phát hiện giá trị bị đổi).
Windows OCR (nhánh chính của So chữ tiếng Nhật) cần WinRT nên không có trong test — kiểm chứng bằng chạy app.

```powershell
dotnet test Tests\ImageCompare.Tests          # hoặc: dotnet run --project Tests\ImageCompare.Tests
```

Biên dịch kèm trực tiếp `Engine\` (như CsvEditor.Tests). Chỉ x64 vì OCR cần `tesseract50.dll` native.

Xem `PLAN.md` cho quyết định thiết kế và kết quả kiểm chứng.
