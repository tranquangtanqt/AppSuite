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

## Bổ sung: chế độ So chữ + cảnh báo lệch bố cục — 2026-10-01

### Bối cảnh
So 2 ảnh chụp cùng 1 màn hình nghiệp vụ tiếng Nhật ở 2 môi trường (server cũ chế độ IE ↔ localhost Edge): font và
độ rộng ô khác → bố cục xê dịch cục bộ, tự căn 1 độ lệch chung (2, 10) không bù được → chế độ Khác biệt ra **1 vùng
phủ 99% ảnh** (pixel giống 74,6%, SSIM 0,29) — đúng kỹ thuật nhưng không chỉ ra chỗ khác thật. Câu hỏi thật: chữ /
giá trị trên màn hình có khác không. Yêu cầu: không đổi kết quả của các chế độ có sẵn; hỗ trợ tiếng Nhật, Việt, Anh;
có kiểm tra "khác màu chữ"; ảnh thật của người dùng chỉ dùng trong Windows Sandbox, không commit.

### Quyết định
- **Cảnh báo (Khác biệt)**: 1 vùng phủ ≥ 60% ảnh A → thêm 1 dòng nhắc thử *So chữ*. Chỉ thêm chữ — mọi con số giữ
  nguyên (kiểm chứng bằng bản cũ / mới trên cùng cặp ảnh).
- **Chế độ So chữ mới** (tab thứ 7), không sửa engine cũ — file mới `FormPreprocess`, `FormTextReader`, `TextDiff`,
  `Services/FormReaders`.
- **OCR tiếng Nhật — đo trước khi chọn** (2 ảnh, 155 nhãn / giá trị làm đáp án; "nhất quán" = % đoạn của A đọc ra
  giống hệt ở B — quan trọng hơn đúng tuyệt đối khi so):

  | Cấu hình | Đúng A / B | Nhất quán |
  |---|---|---|
  | Tesseract jpn fast, cả trang (như Tìm chữ) | 41% / 50% | — |
  | Windows OCR ja, cả trang | 36–40% / 43–61% | — |
  | Tesseract jpn fast + xử lý trước, từng cụm | 62% / 74% | 42% |
  | Tesseract jpn best + xử lý trước, từng cụm | 68% / 77% (chậm 2,5×, 13,7 MB) | 48% |
  | **Windows OCR ja + xử lý trước, cả trang** | **75% / 85%**, ~0,5 s | **66%** |

  → **Windows OCR "ja"** (có sẵn trên Windows tiếng Nhật, không thêm dữ liệu); máy không có gói OCR tiếng Nhật →
  **Tesseract jpn fast** làm dự phòng (+2,4 MB `Ocr\tessdata\jpn.traineddata`); tiếng Việt / Anh: **Tesseract vie**
  (Windows OCR không có tiếng Việt). Windows OCR cần WinRT → `WindowsFormReader` ở `Services/`, Engine vẫn test được.
- **Xử lý trước** (`FormPreprocess`) là yếu tố quyết định: phóng ×3 nội suy song tuyến → đen trắng Otsu → xoá đoạn
  mực ngang ≥ 40 px / dọc ≥ 16 px (viền ô). Trước đó viền ô làm OCR chia dòng sai, số trong ô thành rác ("23ロ1ロ7").
  Đã thử và bỏ: **ngưỡng cục bộ** (để giữ chữ xám) — đúng không hơn, ghép được ít đoạn giống hơn (87–121 so với 130),
  kết hợp Otsu + cục bộ = như Otsu; **phóng không nội suy** (cho font bitmap của ảnh cũ) — kém hẳn (93/104 so với
  116/131). Windows OCR đọc cả trang đã xử lý; Tesseract đọc **từng cụm chữ** (PSM SingleLine) — đọc cả trang chỉ 9%
  nhất quán.
- **Ghép đoạn (`TextDiff`)**:
  - Tách dòng OCR thành đoạn ở khoảng trống > 0,8 chiều cao chữ; hiển thị nối ký tự CJK không dấu cách.
  - **Khoá so**: NFKC (toàn / nửa độ rộng), bỏ khoảng trắng + vết viền (`| [ ] _`), gộp ký tự OCR hay nhầm
    (`0 O ロ 口 〇`, `1 l I`, `- ー 一`, `カ/力`, `エ/工`, `ニ/二`, `,`/`.`… — `,`/`.` thêm sau khi thử: Tesseract đọc
    "1,360,000" thành "1.360.000").
  - **Độ lệch cục bộ**: đoạn có khoá duy nhất ở cả 2 ảnh làm mốc; vị trí dự đoán ở B = trung bình có trọng số 4 mốc
    gần nhất (B trôi tới ~50 px ở cuối trang — 1 độ lệch chung không đủ).
  - Thứ tự ghép: mốc → giống hệt gần chỗ dự đoán → 1 đoạn ↔ 2–4 đoạn liền nhau ở ảnh kia (OCR cắt khác) → gần giống
    (Levenshtein ≥ 0,5) → **cùng đúng chỗ + gần nhất của nhau** (chữ khác hẳn, vd "2圓" ↔ "200": 1 mục đổi chữ thay
    vì 2 mục chỉ-A + chỉ-B; 63 → 57 mục) → còn lại chỉ A / chỉ B.
  - **Gần giống** (ẩn mặc định): lệch ≤ 1/5 độ dài mà **chữ số giống hệt** — nhãn bị OCR đọc lệch. Chữ số khác luôn
    là đổi chữ (giá trị là thứ cần bắt). Ban đầu là "lệch đúng 1 ký tự" — trên ảnh thật còn quá nhiều nhãn dài lệch
    2–3 ký tự bị báo đổi.
  - **Khác màu chữ**: cặp giống chữ → màu lõi nét (nền = màu hay gặp nhất trong khung; nét = lệch nền > 40; lấy 30%
    pixel lệch nhiều nhất — bỏ viền khử răng cưa làm chữ đen trông như xám); khác khi chênh độ sáng ≥ 45 hoặc RGB ≥ 80.
- **Giao diện**: 2 ô A | B (như Cạnh nhau, B vẽ theo độ lệch của lượt so chữ), khung màu theo loại (đỏ đổi, xanh chỉ
  A, cam chỉ B, tím màu, xám gần giống); chọn ngôn ngữ; *Hiện gần giống*; bấm mục → phóng ≤ 300% tới khung bao cả 2
  phía. OCR cache theo ảnh + ngôn ngữ; *Vùng bỏ qua* (vẽ ở Khác biệt) áp dụng luôn. Bảng tóm tắt viết ngắn — bản đầu
  ghi cách cài gói OCR ngay trong bảng, đẩy danh sách xuống chỉ còn 1 mục (cách cài chuyển sang README).

### Kiểm chứng
- **Unit test — Windows Sandbox** (exe xUnit self-contained): 61/61 PASS (40 cũ + 21 mới: khoá so, tách đoạn, bố cục
  trôi, đổi giá trị, gần giống vs đổi chữ số, chỉ A / chỉ B, cắt đoạn khác nhau, nhãn lặp, vùng bỏ qua, màu xám ↔ đen,
  xoá viền ô, tách cụm, Tesseract đọc form tiếng Nhật / tiếng Việt, đầu-cuối phát hiện giá trị bị đổi).
- **Giao diện — Windows Sandbox** (UI Automation, cặp ảnh thật của người dùng): bản cũ ↔ bản mới tab Khác biệt cùng
  số (1 vùng, 74,644%, SSIM 0,2859, lệch (2, 10), 11.862 px) + dòng cảnh báo "1 vùng phủ 99% ảnh"; tab So chữ tiếng
  Nhật (sandbox không có gói Windows OCR → Tesseract dự phòng, ghi rõ trên bảng): ~2 s, 123 chỗ khác / 61 giống;
  tiếng Việt / English chạy hết; bấm mục → phóng tới; quay lại Khác biệt số không đổi; không lỗi Event Log.
- **Nhánh Windows OCR — chạy app trên máy dev** (sandbox không có gói OCR và không bật mạng để cài): 54 chỗ khác hiện
  (+3 gần giống ẩn), 130 đoạn giống / 181–177 đoạn, 6 khác màu chữ thật (ô bị khoá chữ #A0A0A0 ở A ↔ chữ đen ở B),
  đọc ~0,5 s / ảnh. Nhiễu còn lại chủ yếu do font bitmap của ảnh cũ bị đọc sai ("230川7") và thanh tiêu đề / URL
  (dùng Vùng bỏ qua).

### Sửa: chữ hiển thị sai trong danh sách So chữ — 2026-10-01 (chiều)
Người dùng chạy thử trên cặp ảnh thật: nhiều mục "đổi chữ" hiện chữ rác (`230川7` → `230107`, `厓途区分商印こ`,
`商品コード937間-029-圓02`) — OCR cả trang đọc sai font bitmap của ảnh cũ (chế độ IE), chữ thật giống hệt.
- **Đọc lại riêng từng chỗ nghi khác** (`Engine/TextDiffVerifier`): cắt vùng ở A và B (mục chỉ 1 phía: vùng dự đoán ở
  ảnh kia, cắt rộng hơn), đọc lại ở {×3, ×4} × {ngưỡng thường, 225} bằng bộ đọc chính + Tesseract jpn (font mà Windows
  OCR đọc sai thì Tesseract có khi đúng). Có cách đọc trùng ở 2 phía → bỏ mục. Chữ trùng phải na ná chữ của chính mục
  đó (giống ≥ 1/2) — test với bộ đọc giả cho thấy vùng cắt dính nhãn bên cạnh, 2 phía cùng đọc ra nhãn đó, sẽ xoá nhầm
  1 thay đổi thật. Còn khác: hiện cặp cách đọc sát nhau nhất trong các cách đọc na ná chữ gốc. Chạy song song (Tesseract
  có pool; Windows OCR khoá vì tài liệu không nói an toàn đa luồng): 5,0 s → 1,5 s.
- **Ngưỡng đen trắng kẹp [185, 200]** (`FormPreprocess`): chữ xám của ô bị khoá (#A0A0A0, nét 1 px) bị Otsu thuần xoá
  ở cả trang; vùng cắt nhỏ chỉ gồm nền trắng + nền trang xanh nhạt thì Otsu biến nền thành mực. Ngưỡng 225 khi đọc lại
  để nét xám mảnh không đứt. Phóng ×3 không nội suy đã thử - kém hơn.
- Kết quả trên cặp ảnh thật (Windows OCR): 158 đoạn giống (130 trước), **26 chỗ khác** (54 trước) gồm 9 khác màu chữ
  thật (6 trước) và 7 chỗ thanh tiêu đề / URL; đọc ~0,5 s / ảnh + kiểm tra lại ~1,5 s. Nhánh Tesseract (sandbox): 75 chỗ
  (123 trước), ~4 s tổng. Unit test 68/68 trong Sandbox; tab Khác biệt vẫn cùng số với bản cũ.

### Sửa: ghép nhầm nhãn + giá trị ô, ô bị cắt mảnh — 2026-10-02
Người dùng báo trên cặp ảnh thật: `受注区分` ↔ `受注区分受注` + 1 mục chỉ-A `三` (ghép nhầm), `ページ数` ra 2 mục, và
`使用インキ耐光24H` / `色見本指定その他` dính cả nhãn lẫn giá trị combobox. Nguyên nhân: Windows OCR đọc cả trang trên
ảnh đã xoá viền → nhãn sát combobox chỉ cách vài px, bị gộp 1 đoạn.
- **Tách đoạn tại viền ô** (`TextDiff.Segments` nhận ảnh gốc): trong khoảng trống giữa 2 từ có 1 cột mà ≥ 80% số dòng
  (chiều cao chữ + 2 px mỗi phía) đổi độ sáng ≥ 24 so với cột trái → viền / mép ô → tách. Phần nới 2 px loại nét dọc
  của ký tự mà khung từ OCR bỏ sót (đã gặp: `水性ﾆｽ版1` bị tách ở nửa trái chữ 版 khi chưa nới).
- **Nhập mảnh lẻ vào cặp** (bước 6 của `Compare`): đoạn chưa ghép nằm trong khung đoạn của 1 cặp chữ khác nhau ở ảnh
  kia và sát đoạn cùng cặp ở ảnh mình → nhập vào cặp (A `へ` + `ゾ数` ↔ B `へ叮ゾ数` → 1 mục gần giống, ẩn mặc định).
- Ảnh thật (Windows OCR): 158 → 184 đoạn giống, 28 → 26 mục (3 gần giống, ẩn); `受注区分`, `使用インキ`, `色見本指定`
  giờ giống, chỉ còn giá trị: `三 → 受注` (A: chữ trắng trên nền chọn xanh, đen trắng hoá làm mất chữ), `耐 → 耐光2`
  (A: chữ xám đọc thiếu). Unit test 70/70.

### Sửa: chữ xám của ô bị khoá báo "đổi chữ" thay vì "khác màu" — 2026-10-02
Người dùng báo tiếp các mục `商印そ → 商印その他`, `三 → 受注`, `宮挙壱 → 宮業売上`, `耐 → 耐光2`, `品仕丿 → 【コ`,
chỉ-B `鋼サイス。`. Soi ảnh cắt: trừ mục cuối, A đều là ô bị khoá chữ xám #A0A0A0 nét 1 px, B chữ đen - chữ giống nhau,
chỉ khác màu; OCR cả trang bắt được 1 phần chữ, khung đoạn hụt → đọc lại vùng cắt đó kiểu gì cũng thiếu chữ.
- **Vùng đọc lại nới bằng khung phía kia** (`TextDiffVerifier.Region`): nới đều 2 bên tới chiều rộng / cao phía kia.
- **Chữ chung "chứa trọn"** (`Common`): vùng nới hay dính nhãn bên cạnh (`区分商印その他` ↔ `商印その他`) → 1 cách đọc
  nằm trọn trong cách đọc phía kia (≥ 3 ký tự) cũng tính là giống, với 2 điều kiện: phần dư không có chữ số (`200` ↔
  `1200` là đổi thật) và phần dư nằm ở phía có vùng được nới (`商印その` ↔ `商印その他` mà chữ dư ở phía vốn rộng hơn là
  chữ thêm thật - có test).
- **Đọc lại giống thì vẫn so màu** trong khung gốc của mục → "khác màu chữ" thay vì bỏ mục. Màu nét sáng ≥ 200 không
  tính (khung nhỏ trên nút bị mờ đo trúng viền nổi trắng: `商品仕入` #A0A0A0 → #E4EBEE, đã gặp).
- `Closest`: có cặp cách đọc chỉ lệch kiểu OCR (cùng chữ số) thì ưu tiên → gần giống (`FSC認証製品` đọc ra `JF60…` ↔ `JFS0…`).
- Khoá so bỏ thêm ký tự viền nút `「」【】〔〕\` (`「その他金額」` ↔ `その他金額\`).
- Đã thử và bỏ: so khớp mờ (Levenshtein đoạn con) cho mục chỉ-1-phía - không cứu được `胴ｻｲｽﾞ` (A `胴サれ。`, B `鋼サイス。`,
  lệch 3/5) mà tăng rủi ro ẩn nhầm.
- Ảnh thật, Windows OCR: 26 mục → 26 nhưng **khác màu chữ 9 → 15** (thêm 商印その他, 受注, 過不足不可, 営業売上, 検査S1,
  耐光24H), đổi chữ 8 → 1 (chỉ còn URL thanh địa chỉ), 商品仕入 / その他金額 hết báo. Còn sai: chỉ-B `鋼サイス。`, chỉ-A
  `物`, và khung trình duyệt. Sandbox (Tesseract): 71 → 56 chỗ khác, khác màu 1 → 9; tab Khác biệt cùng số; unit test
  72/72 ở cả máy thật và Sandbox.

### Sửa: mục chỉ-1-phía mà OCR cả trang bỏ sót phía kia — 2026-10-02
Người dùng báo chỉ-A `物` và chỉ-B `鋼サイス。`. Thật ra: ô `枚` ở cả 2 ảnh (A xám đọc thành `物`, B đen OCR cả trang
không đọc ra), nhãn `胴ｻｲｽﾞ` ở cả 2 ảnh (A không đọc ra, B đọc sai). Kiểm tra cũ chỉ tìm chữ cả-trang của phía có
(≥ 2 ký tự) trong vùng dự đoán → không cứu được chữ đọc sai.
- `CheckOneSided`: vùng dự đoán đọc cả cắt sát lẫn cắt rộng; thêm điều kiện: đọc lại riêng chính mục ra đúng chữ mà vùng
  dự đoán cắt sát đọc ra (chỉ cắt sát - cắt rộng dễ dính ô bên cạnh). Giống thì so màu như mục 2 phía → "khác màu".
  Nhờ so màu, thêm 3 chỗ khác màu thật trước đây bị bỏ không so: `なし`, `A4縦`, `無線綴`.
- **So hình nét chữ** (`SameShape`, không OCR) khi OCR không chốt được: mặt nạ nét (lệch nền ≥ 40% độ lệch lớn nhất →
  chữ xám lẫn đen), vùng tự nới khi nét chạm mép (dự đoán `枚` lệch 5 px), dóng cột bằng quy hoạch động cho độ lệch trôi
  ±1 px mỗi cột (2 trình duyệt cùng font nhưng khoảng cách chữ lệch 1 px cộng dồn; `ｲ` `ｽ` dính nhau ở A nên tách theo
  cột trống - đã thử - không được), pixel lệch ≤ 30% số pixel nét. Lấy chữ hiển thị từ phía nét đậm hơn.
- Ảnh thật, Windows OCR: `枚` → khác màu, `胴ｻｲｽﾞ` hết báo; khác màu 15 → 19; còn lại chỉ khung trình duyệt (4 chỉ-A
  vẫn giữ - so hình không ẩn nhầm) + 1 URL đổi. Sandbox (Tesseract): 56 → 45 chỗ khác (chỉ-A 9 → 3); tab Khác biệt cùng
  số; unit test 74/74 cả 2 nơi.

### Bổ sung: xuất kết quả So chữ + menu chuột phải — 2026-10-02
- `Engine/TextDiffReport`: cột chung #, Loại, Chữ A, Chữ B, Ghi chú, Vị trí A, Vị trí B (mục 1 phía: vị trí phía kia
  = chỗ dự đoán, ghi `≈`). Tab (clipboard → dán Excel ra đúng cột), CSV RFC 4180 lưu kèm BOM (Excel mở đúng chữ Nhật /
  Việt), HTML 1 file dùng lại `HtmlReport.CropCell` (ảnh cắt A | B mỗi mục). Chỉ xuất mục đang hiện (*Hiện gần giống*).
- UI: 3 nút Copy / Lưu CSV / Xuất báo cáo HTML (thay chỗ nút xuất ảnh khác biệt khi ở So chữ); chuột phải 1 mục
  (`RightTapped` → `MenuFlyout`, mọi chế độ): Copy mục này / Copy cả danh sách — So chữ ra dòng Tab, chế độ khác `số⇥chữ`.
- Kiểm: unit test 77/77; GUI trong Sandbox (job tự động): menu hiện đúng 2 mục, clipboard đúng 1 dòng / 46 dòng, nút
  Copy = menu, hộp thoại lưu (gõ đường dẫn) → CSV có BOM, HTML 45 dòng bảng + ảnh cắt.

### Bổ sung: căn "Soi 1 vùng" cho 2 ảnh lệch bố cục dần — 2026-10-05
- Người dùng chọn phương án B (khoanh 1 vùng) thay vì A (tự căn từng vùng cả trang). `AlignMode.Focus` +
  `DiffOptions.FocusRect`; `Engine/RegionAligner`: (1) dò độ lệch cả vùng trong ±10% cạnh dài B (64–400 px) bằng MAD
  trên ảnh xám thu nhỏ (f chọn để vị trí × pixel ≲ 2·10⁸), hoà điểm → chỗ gần vị trí cũ nhất, tinh chỉnh ở mức gốc;
  (2) chia vùng thành dải ngang theo dòng trống của A rồi thành ô theo cột trống (gộp đoạn cách < 8 px), dải dò ±8
  dọc / ±6 ngang quanh dải kề, ô dò ±10 ngang / ±3 dọc quanh ô kề, lan từ tâm vùng ra; (3) ghép các ô của B thành ảnh
  "đã nắn" cỡ vùng, `PixelDiff` / SSIM so A với ảnh đó. `DiffPainter` vẽ ảnh nắn trong vùng, phủ tối ngoài vùng, viền
  xanh nét đứt; `SourceRects` (báo cáo HTML) và màu B dưới con trỏ lấy theo ô chứa điểm.
- Bản đầu dùng `TemplateMatcher` (NCC) + 1 độ lệch cho cả vùng → hỏng 2 chỗ: nội dung lặp đều (sọc / dòng bảng) bị
  gộp chỗ khớp nên chọn nhầm chỗ xa; trong vùng 3 dòng B vẫn lệch thêm 2 px / dòng → vẫn báo khác. Dòng trống ban đầu
  xét "chênh sáng cả dòng ≤ 12" → khối có khung viền không có dòng trống nào → đổi sang đếm pixel có mực (bỏ qua vài
  đường kẻ).
- Ảnh thật của khách (IE ↔ Edge, 1021 × 836): Tự căn 74,6% giống, 1 vùng phủ 99%. Soi khối 受注数量 590 × 100: 1 dải
  → 80%, theo ô → 89% (B lệch x −30…11, y 12…28 px qua 67 ô, khớp đo tay). Phần đỏ còn lại là khác cách vẽ (nét chữ,
  viền ô nhập 3D, ô nhập ở B rộng hơn) — so pixel không thể coi là giống; ngưỡng 35% chỉ lên 92%. Với cặp này *So chữ*
  vẫn là công cụ chính; Soi 1 vùng hợp với ảnh cùng cách vẽ mà bố cục trôi (thêm dòng, chữ dài hơn đẩy ô).
- Unit test `FocusTests` (8): trang giả B lệch thêm 2 px mỗi dòng — cả trang < 95% giống, khoanh 3 dòng ở đầu / cuối
  trang → 100%, đúng độ lệch từng dòng; 1 chỗ đổi thật trong vùng = đúng 1 vùng khác, chỗ đổi ngoài vùng bị bỏ; sọc lặp
  đều → chọn chỗ gần nhất; chưa khoanh / vùng ngoài ảnh → như Tự căn; ảnh xuất phủ tối ngoài vùng. Tổng 85/85.
