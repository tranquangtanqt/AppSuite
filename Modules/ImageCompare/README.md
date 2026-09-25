# ImageCompare — So sánh ảnh

So khớp 2 hình ảnh (ảnh chụp màn hình, file PNG/JPG, ảnh xuất từ tool khác): tìm chỗ khác nhau, xem
trực quan, tìm ảnh con trong ảnh lớn, đánh giá độ giống — rồi xuất ảnh / báo cáo HTML gửi người khác.
Kèm **Tìm chữ**: đọc chữ trong 1 ảnh (OCR tiếng Việt / tiếng Anh) và tìm chữ trong đó.

## Chạy

- Độc lập: mở `Modules\ImageCompare\ImageCompare.csproj`, đặt làm Startup Project, F5 (hoặc
  `dotnet run --project Modules\ImageCompare\ImageCompare.csproj -p:Platform=x64`).
- Qua MainLauncher: entry `ImageCompare` trong `MainLauncher/Config/modules.json`.
- Dòng lệnh: `ImageCompare.exe [ảnh A] [ảnh B]` mở sẵn 2 ảnh.

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
  `TextRecognizer` (OCR Tesseract: phóng ×2, cắt dải đọc song song, đảo màu nền tối) + `TextSearch`.
- `Ocr/` — dữ liệu OCR tiếng Việt `tessdata\vie.traineddata` (tessdata_fast) và VC++ runtime chép kèm
  (`vcruntime\win-x64|win-x86`, cần cho `tesseract50.dll` trên máy chưa cài VC++ Redistributable); build chép
  ra `tessdata\` và cạnh exe.
- `ViewModels/CompareViewModel` — trạng thái + chạy so sánh / tìm ở nền (huỷ khi đổi ảnh / tuỳ chọn).
- `MainWindow` — canvas `SKXamlCanvas`, zoom / cuộn, kéo-thả, hộp thoại, clipboard.
- `Services/` — `ImageFileService`, `ClipboardService`, `AppLog` (chép từ ScreenCapture — module không
  reference nhau). Log: `Logs\imagecompare-yyyy-MM-dd.log` cạnh exe, giữ 14 ngày.
- Chỉ reference `Common` + `SharedUI`; không DI container (giống các module khác).

Xem `PLAN.md` cho quyết định thiết kế và kết quả kiểm chứng.
