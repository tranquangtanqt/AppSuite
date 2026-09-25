# ImageCompare — PLAN

## Bối cảnh

Người dùng cần một công cụ so khớp 2 ảnh trong AppSuite, kết hợp nhiều kiểu (trả lời 2026-09-25):
- **Kiểu so sánh**: kết hợp — tìm chỗ khác nhau, xem trực quan, tìm ảnh con trong ảnh lớn, đánh giá % giống.
- **Nguồn ảnh**: mọi nguồn — chụp màn hình (ScreenCapture), file PNG/JPG, ảnh xuất từ tool khác.
- **Vị trí/kích thước**: lúc cùng kích thước, lúc lệch vài px hoặc khác kích thước (trang dài hơn 1 đoạn).
- **Kết quả**: vừa xem trên màn hình, vừa xuất ảnh/báo cáo gửi người khác.

Làm module độc lập mới (không nhét vào ScreenCapture) vì đây là công cụ riêng, dùng cả với ảnh không
chụp bằng ScreenCapture; đúng mô hình Application Hub (chạy riêng hoặc qua MainLauncher).

## Thiết kế

### Khung module
- Copy khung `Modules\ModuleA`; `ProjectReference` chỉ `Common` + `SharedUI`; `SkiaSharp` +
  `SkiaSharp.Views.WinUI` 2.88.8 (cùng bản ScreenCapture), `CommunityToolkit.Mvvm`, `AllowUnsafeBlocks`.
- Đăng ký: `MainLauncher/Config/modules.json`, `build\Publish-AppSuite.ps1`, `build\Sync-Modules-Dev.ps1`,
  `AppSuite.sln` (thêm tay 14 dòng — `dotnet sln add` tự tạo thư mục "Modules" và cấu hình "Any CPU"
  cho mọi project, lệch cách solution đang tổ chức).
- Không reference ScreenCapture (luật độc lập) → **chép** `ImageFileService` / `ClipboardService` /
  `AppLog`. Không chuyển vào SharedUI vì sẽ kéo SkiaSharp vào mọi module.
- Không DI container, ViewModel + service khởi tạo trong code-behind (giống các module hiện có).
- Định dạng pixel thống nhất trong Engine: BGRA premul (`ImageUtil.Normalize` khi nạp) → vòng lặp pixel
  đọc thẳng `uint`.

### Lõi so sánh (`Engine/`)
1. **Tự căn dịch chuyển** (`Aligner`): ảnh xám thu nhỏ (cạnh ngắn ~300 px); ước lượng dy, dx riêng bằng
   tương quan 1 chiều của "hồ sơ cạnh" theo dòng / cột (nội dung dịch bao nhiêu hồ sơ dịch bấy nhiêu); dò
   2 chiều ±2 ô quanh ước lượng và quanh (0, 0); tinh chỉnh ở mức gốc bằng MAD **đọc thẳng pixel** (ảnh xám
   cỡ gốc của ảnh 1400 × 30.000 sẽ tốn ~170 MB). (0, 0) khớp gần bằng thì giữ (0, 0).
2. **Căn theo dòng** (`RowAligner` + `RowDiffView`): băm từng dòng (bỏ 3 bit thấp mỗi kênh), diff Myers trên
   2 dãy mã băm → các dải; trong 1 cụm sửa đổi, số dòng bỏ / thêm bằng nhau ghép cặp (so pixel), phần dư là
   dải chỉ có ở A / B. Dải ghép cặp so pixel bằng chính `PixelDiff` với offset (dx, aY − bY). Vết Myers lưu
   D² → giới hạn 3000 bước (~36 MB); vượt → lui về tự căn dịch chuyển (ghi chú trong kết quả).
3. **So pixel** (`PixelDiff`): khác khi chênh lệch lớn nhất 3 kênh > ngưỡng; bỏ qua răng cưa = heuristic
   `antialiased` của **pixelmatch** (ISC) — pixel nằm trên viền mịn còn nguyên ở ảnh kia. Cách đơn giản
   "có lân cận cùng màu" bị loại vì bỏ sót chữ bị sửa thật (chữ đen trên nền trắng luôn có lân cận trắng /
   đen). Gom vùng: ô 8 × 8, nối ô cách ≤ 2 ô → chỗ khác cách nhau < ~16 px gộp 1 khung.
4. **SSIM** (`Similarity`): ô 8 × 8 trên ảnh xám thu nhỏ (cạnh dài ≤ ~1024).
5. **Tìm ảnh con** (`TemplateMatcher`): NCC (ảnh tích phân cho tổng / bình phương cửa sổ); hệ số thu nhỏ f
   chọn để số phép tính ≲ 3·10⁸; lọc qua **kim tự tháp** f → f/3 → … → 1 (2000 → 400 ứng viên). Phiên bản
   đầu chỉ tinh chỉnh 200 đỉnh thô ở mức gốc → trong ảnh 30.000 px toàn dòng bảng na ná nhau, chỗ đúng
   không lọt top (test FAIL) → thêm mức trung gian. Kết quả xếp theo độ khớp giảm dần; tự tìm ảnh nhỏ hơn
   trong ảnh lớn hơn.
6. **Vùng bỏ qua**: `DiffOptions.IgnoreRects` (toạ độ A), `PixelDiff` bỏ pixel trong vùng; tắt khi căn theo
   dòng (toạ độ ảnh ghép ≠ toạ độ A).
7. **`IDiffView`** (thêm ở giai đoạn 3): căn theo dòng cho ra ảnh ghép chứ không phải 1 độ lệch → tách
   interface chung (Stats, Regions, Bounds, Draw, Render, SourceRects) cài bởi `DiffPainter` và
   `RowDiffView`; màn hình, Copy / PNG và `HtmlReport` chỉ dùng interface. `Draw(canvas, pixelSize)` dùng
   chung cho màn hình (zoom) và xuất 1:1 → 2 nơi luôn giống nhau.

### Giao diện
- 1 cửa sổ: 2 ô ảnh; `SelectorBar` 5 chế độ + tuỳ chọn của chế độ (bọc `ScrollViewer` ngang — màn hẹp
  vẫn tới được mọi control); canvas `SKXamlCanvas` tự quản zoom / pan (các ô Cạnh nhau dùng chung → đồng bộ);
  bảng kết quả bên phải (Khác biệt / Tìm ảnh con) — nút xuất đặt ở đây (lúc đầu ở hàng tuỳ chọn → chật).
- So sánh chạy nền, huỷ khi đổi ảnh / tuỳ chọn; kéo thanh ngưỡng có debounce 200 ms. Kết quả / bitmap cũ
  **không Dispose** tường minh (lượt nền hoặc xuất báo cáo có thể vẫn đang đọc con trỏ pixel) — để GC.
- Đổi chế độ giữ zoom / vị trí nếu khung nội dung không đổi (soi cùng 1 chỗ ở Khác biệt ↔ Chồng mờ ↔ Thanh
  trượt); đổi Cạnh nhau ↔ 1 ô hoặc khung đổi (ảnh ghép, ảnh được tìm) thì vừa cửa sổ lại.
- Slider: Minimum / Maximum gán trong code và **chặn ValueChanged lúc gán** (đặt Minimum = 50 khi Value = 0
  làm WinUI đẩy Value lên 50 và ghi đè mặc định 90% trong ViewModel — đã gặp).

### Xuất
- Copy / PNG: `IDiffView.Render()`. HTML: 1 file tự chứa (base64), bảng vùng kèm ảnh cắt A | B (tối đa 100
  vùng, qua `SourceRects` → đúng cả với ảnh ghép theo dòng).

## Kiểm chứng

- **Engine** — console harness (scratchpad, `Compile Include` `Engine\*.cs`), 21/21 PASS:
  giống hệt (0 vùng, 100%, SSIM 1); 3 chỗ sửa → đúng 3 vùng, khung ôm sát; B dịch (+7, −3) và rộng hơn →
  tự căn đúng, 0 vùng; dịch dọc 150 px → đúng; JPG q85 → SSIM 0,9997 nhưng 26 vùng nhiễu ở ngưỡng 8%, 0 vùng
  ở 20% (→ gợi ý tăng ngưỡng cho JPG trong UI + README); dịch cả ảnh 0,5 px → bỏ qua răng cưa chỉ giảm ~½
  nhiễu (giới hạn đã ghi README); ảnh 1400 × 30.000 ~0,9 s; ảnh khác biệt + HTML (231 KB); căn theo dòng:
  chèn 400 px → 1 dải "chỉ có ở B" đúng vị trí (tự căn dịch chuyển: 66 vùng rác), bỏ 300 px → 1 dải "chỉ có
  ở A", 2 ảnh khác hẳn → lui về tự căn; vùng bỏ qua; tìm ảnh con (đúng toạ độ, điểm 1.0), tự đổi chiều, icon
  lặp 3 lần, không có → 0, trong ảnh 30.000 px ~0,9 s.
- **Giao diện** — Windows Sandbox (150% DPI, harness agent + job, `mouse_event` ABSOLUTE, UI Automation):
  mở 2 ảnh qua dòng lệnh; Cạnh nhau / Chồng mờ / Thanh trượt; Khác biệt: tự căn ra (−5, −12) đúng độ dịch
  tạo, 3 vùng; bấm vùng → phóng 800% khung vàng; Copy → clipboard 1120 × 720; Alt+→ → Chỉnh tay (−4, −12);
  căn theo dòng trên trang dài chèn 240 px → "B thêm 240 dòng", 1 vùng (tự căn: 75 vùng); tìm ảnh con → nút
  Lưu (900, 600) 100%; vùng bỏ qua: khoanh chữ số → 3 → 2 vùng, bấm để xoá → 3.
- `dotnet build` module (0 cảnh báo). Chưa thử qua GUI: hộp thoại Lưu PNG / Xuất HTML (engine báo cáo đã
  test ở harness), bản Release publish qua `Publish-AppSuite.ps1` chạy từ MainLauncher.

## Bổ sung: chế độ Tìm chữ (OCR) — 2026-09-25

### Bối cảnh
Người dùng muốn tìm chữ trong hình ảnh: đọc chữ trong 1 ảnh (chỉ cần 1 ảnh, không bắt buộc đủ A và B), gõ chữ
cần tìm → khoanh các chỗ có chữ đó; chọn dùng **Tesseract** (Windows OCR có sẵn không hỗ trợ tiếng Việt — máy
dev chỉ có gói OCR tiếng Anh / tiếng Nhật).

### Quyết định
- **Gói NuGet `Tesseract` 5.2.0** (wrapper .NET + `tesseract50.dll` / `leptonica` native x86 / x64; không có
  ARM64 → chế độ Tìm chữ báo lỗi trên ARM64, các chế độ khác vẫn chạy). Target `DropOtherArchTesseract`
  bỏ bản native của kiến trúc kia (~5 MB).
- **Dữ liệu: chỉ `vie` của tessdata_fast (0,5 MB)**, trong `Ocr\tessdata\` → `tessdata\` cạnh exe. Đã đo
  (ảnh chữ Segoe UI kiểu chụp màn hình):
  - fast vs best: đúng như nhau, fast nhanh hơn 3–8 lần và nhẹ hơn (4,6 MB so với 28 MB cho vie + eng) →
    chọn fast.
  - `vie` vs `vie+eng`: chữ tiếng Anh / code / đường dẫn đúng ngang nhau (6/8 dòng khó, cùng lỗi), nhưng
    `vie+eng` sai dấu tiếng Việt nhiều hơn (10/13 so với 13/13) và chậm hơn ~1,5 lần → chỉ `vie`.
- **VC++ runtime chép kèm** (`Ocr\vcruntime\win-x64|win-x86\` → cạnh exe): `tesseract50.dll` cần
  msvcp140 / vcruntime140(_1); máy chưa cài VC++ Redistributable (vd Windows Sandbox) sẽ không đọc được chữ.
  Dạng app-local được Microsoft cho phép phân phối lại. Thư mục đặt tên `win-x64` vì `.gitignore` bỏ qua `x64/`.
- **Phóng ×2 trước khi đọc** (ảnh rộng ≤ 2400 px): chữ màn hình nhỏ đọc kém ở ×1, ×3 không hơn mà chậm hơn.
- **Ảnh dài**: cắt dải 600 px (chồng 80 px), đọc song song (tối đa 4 engine, pool giữ lại giữa các lần);
  dòng thuộc dải chứa tâm của nó → không mất / lặp dòng ở chỗ nối; huỷ được giữa các dải; báo % tiến độ.
- **Chữ sáng trên nền tối**: dải nền tối (độ sáng trung bình < 110) đảo màu cả dải (không đảo thì Tesseract
  đọc được nhưng mất dấu: "Dang xuat khdi thiét bi"). Dòng tin cậy thấp (< 50) được đọc lại riêng với màu đảo
  (PSM SingleLine), giữ bản tin cậy hơn — sửa được tiêu đề / nút chữ trắng trên nền màu trong trang nền sáng
  (trước: ký tự rác, tin cậy 0; sau: "Hệ thống quản lý đơn hàng", 97).
- **Không lọc từng từ theo độ tin cậy**: từ đúng đôi khi chỉ được 12–14 điểm ("Khách hàng H") — lọc ở 15 làm
  mất 2/12 dòng. Chỉ bỏ cả dòng khi trung bình < 15 sau khi đã thử đọc lại.
- **Tìm**: trong từng dòng (cụm nhiều từ được, khung = hợp các từ chứa nó); mặc định **không phân biệt hoa /
  thường và không phân biệt dấu** (OCR hay sai 1 dấu, người tìm hay gõ không dấu); **bỏ qua khoảng trắng**
  (OCR hay dính từ: "Khách hàngC"). Tìm chạy ngay trên luồng UI mỗi lần gõ (vài trăm dòng — tức thì).
- Kết quả OCR cache theo từng ảnh (`ConditionalWeakTable`) → đổi A ↔ B, sang chế độ khác rồi quay lại không đọc lại.
- Ô chọn ảnh A / B: ô được chọn còn trống thì đọc ảnh ở ô kia; ở chế độ Tìm chữ, ảnh vừa đưa vào được chọn luôn.
- Ctrl+V / Ctrl+Shift+V khi đang gõ trong ô Tìm → để ô đó dán chữ (không dán ảnh).
- Bấm kết quả phóng tối đa 300% (không phải 800% như Khác biệt — 1 từ ngắn phóng 800% thì vỡ hạt, mất ngữ cảnh).
- Kèm theo: canvas tự "vừa cửa sổ" lại khi cửa sổ đổi cỡ nếu người dùng chưa tự zoom / cuộn (trước đây mở ảnh rồi
  phóng to cửa sổ thì ảnh bị cắt).

### Kiểm chứng
- **Engine** — harness thêm 9 test (30/30 PASS): bảng 13 dòng tiếng Việt: "Đã giao" có dấu 13/13, "trang thai"
  không dấu 13/13, cụm "khách hàng c" ra khung đúng dòng, phân biệt hoa / thường, toàn bộ chữ; giao diện tối
  (4/4 dòng đúng dấu sau khi đảo màu); ảnh 1400 × 30.000: 740/740 dòng, "#1500" đúng 1 chỗ đúng toạ độ, ~10–13 s;
  huỷ sau 300 ms → dừng trong ~1 s.
- **Giao diện** — Windows Sandbox (không có VC++ runtime trong System32 → xác nhận runtime chép kèm chạy
  được): mở 1 ảnh → Tìm chữ đọc 14 dòng (~0,6 s) gồm tiêu đề chữ trắng và nút "Lưu"; "khach hang" → 12 chỗ tô
  vàng; Ctrl+F → sang Tìm chữ, con trỏ ở ô tìm; Ctrl+V trong ô tìm dán chữ, ô B vẫn trống; bấm kết quả → phóng
  tới; bỏ ảnh → "Chưa có ảnh"; dán ảnh 1100 × 10.000 → "Đang đọc chữ… 24%", xong 3,5 s / 619 dòng, "#1030" →
  4 chỗ. Publish Release như `Publish-AppSuite.ps1` → có `tessdata\`, `x64\` native, VC++ runtime (không có `x86\`).
