# Tiến trình AppSuite

Danh sách việc cần làm / đã làm của toàn bộ AppSuite. Có vấn đề mới thì thêm 1 dòng `- [ ]` vào đúng
mục; làm xong thì đổi thành `- [x]` và ghi ngày hoàn thành. Chi tiết thiết kế của từng module vẫn nằm
trong `PLAN.md` / `README.md` của module đó — file này chỉ để theo dõi tiến độ.

Cập nhật lần cuối: 2026-10-02

Mục ghi **[Đề xuất · Cao / Nên có / Có thể]** là tính năng còn thiếu tìm ra khi rà toàn bộ module (2026-10-02), chưa
được duyệt làm — người dùng chọn mục nào thì bỏ nhãn đề xuất và làm như việc bình thường.

## Đề xuất ưu tiên — thứ tự làm tiếp (rà 2026-10-02)

Tổng hợp từ các mục `- [ ]` bên dưới, xếp theo lợi ích / công sức. Chi tiết từng việc nằm ở mục của module.

**Bước 0 — người dùng thử tay** (Sandbox không tự kiểm được; nên làm trước khi phát hành bản mới):
- [x] Kéo-thả file từ Explorer vào ScreenCapture / CsvEditor — OK (2026-10-05)
- [x] Mở file `.xlsx` CsvEditor xuất ra bằng Excel thật — OK (2026-10-05)
- [ ] ImageCompare bản Release từ MainLauncher (Tìm chữ, *Lưu PNG*, *Xuất báo cáo HTML*)
- [ ] FileTools đợt 1–5 trên file thật + chạy từ MainLauncher
- [ ] Mcf.CrudDiagram chạy UI thật (xuất lại HTML để có header bảng cố định khi cuộn)

**Ưu tiên 1 — nhỏ, lợi ngay:** ✅ xong cả 3 (2026-10-02)
1. ~~Icon riêng (exe + taskbar) cho các module còn lại~~ — 12 project có icon, xem mục Chung
2. ~~Mcf.CrudDiagram: header bảng giữ nguyên khi cuộn~~
3. ~~Chạy 4 project test mới (ImageCompare, Mcf.*) trong Windows Sandbox~~ — 138/138

**Ưu tiên 2 — tính năng nên có, giải quyết vấn đề thật:**
1. ~~ImageCompare: so ảnh lệch bố cục dần~~ — làm phương án B *Soi 1 vùng* 2026-10-05, còn test GUI Sandbox
2. ~~ScreenCapture: xoay 90° / lật / đổi cỡ ảnh theo % hoặc px~~ — code xong 2026-10-05, còn test GUI Sandbox
3. ~~Hướng dẫn F1 cho 4 module HtmlGenerator, ModuleB/C~~ — xong 2026-10-05, còn test GUI Sandbox

**Ưu tiên 3 — có thì tốt, làm khi có nhu cầu:**
- ImageCompare: so sánh hàng loạt 2 thư mục ảnh
- ScreenCapture: còn thiếu so với PicPick, chia 3 đợt (rà 2026-10-05, chi tiết ở mục ScreenCapture): ~~**1. chữ & hình
  vẽ**~~ — code xong 2026-10-06, còn test GUI Sandbox · **2. hiệu ứng ảnh** (viền, đổ bóng, mép rách,
  độ sáng, làm xám, nền trong suốt, watermark) · **3. chụp & công cụ** (lasso, cửa sổ con, 1 màn hình, con trỏ, hút màu,
  kính lúp / thước, in, mẫu tên file, PDF / GIF)
- CsvEditor: freeze cột đầu, ẩn / hiện cột, tự giãn độ rộng cột
- Chung: nhớ kích thước / vị trí cửa sổ + file gần đây cho mọi module (helper chung trong `Common`); nhớ thư mục nguồn
  ModuleB / ModuleC / Mcf.DbDef; chuyển Hướng dẫn của ScreenCapture / FileTools sang bản dùng chung
- Kỹ thuật: giảm tiếp dung lượng publish (runtime chung), unit test còn thiếu (Rdbms.HtmlGenerator, ModuleB/C, ScreenCapture),
  `codegraph upgrade`

## Chung (repo, build, deploy)

- [x] Mọi module đăng ký đủ ở `AppSuite.sln`, `modules.json`, `Sync-Modules-Dev.ps1`,
  `Publish-AppSuite.ps1`, README gốc; mọi project cùng .NET 8 / WindowsAppSDK 2.2 (rà 2026-09-29; từ 2026-10-01 dùng component WinUI 2.2.1 + Runtime 2.2.0)
- [x] README gốc: sửa số project "9 project (MainLauncher + 8 module)" → 11 project (2026-09-29)
- [x] Có project unit test đầu tiên: `Tests\CsvEditor.Tests` (đã thêm vào `AppSuite.sln`) (2026-09-29)
- [x] Unit test cho engine ImageCompare (40) + parser Excel của Mcf.CrudDiagram (21), Mcf.DbDef (11), Mcf.Screen (29)
  — `Tests\*.Tests`, đã thêm vào `AppSuite.sln`, tất cả đạt trên máy dev; build sln 0 warning (2026-09-30)
- [x] Chạy 4 project test mới trong Windows Sandbox (publish self-contained như CsvEditor.Tests): ImageCompare 77,
  Mcf.CrudDiagram 21, Mcf.DbDef 11, Mcf.Screen 29 — 138/138 đạt, cả trên máy dev (2026-10-02)
- [x] Icon riêng cho 12 project (MainLauncher + 11 module): `Assets\<Tên>.ico` tạo bằng `build\New-AppIcon.ps1` (mỗi module
  1 màu + glyph), `Modules\Directory.Build.props` tự dùng làm icon exe + chép cạnh exe, cửa sổ gọi
  `SharedUI.Helpers.WindowIcon.Apply` (thay `ScreenCapture/Views/AppIcon`). Build sln 0 warning; Sandbox: taskbar + thanh
  tiêu đề của MainLauncher / ModuleA / FileTools / CsvEditor + cửa sổ Hướng dẫn đều có icon (2026-10-02)
- [ ] Unit test còn thiếu: Rdbms.HtmlGenerator (cần PostgreSQL/Oracle thật hoặc tách phần dựng HTML), ModuleB/C,
  ScreenCapture (logic chỉnh ảnh / phiên làm việc)
- [ ] Cập nhật CodeGraph 1.4.1 → 1.6.0 (`codegraph upgrade`)
- [x] Giảm dung lượng `Application\`: thay metapackage `Microsoft.WindowsAppSDK` bằng component
  `WinUI` + `Runtime` ở 13 project (bỏ onnxruntime/DirectML ~40 MB/exe); ghim metapackage cũ
  `ExcludeAssets="all"` ở ScreenCapture/ImageCompare (Skia) và CsvEditor (DataGrid). Publish Release
  1.298 MB → 735 MB, 12 exe mở được, 6 project test đạt (2026-10-01)
  - Kiểm chứng trên Windows Sandbox như máy người dùng sạch (chỉ cài .NET 8.0.30 + Windows App Runtime 2.2
    từ MSIX trong gói `Microsoft.WindowsAppSDK.Runtime`): 12/12 exe mở cửa sổ, không lỗi Event Log, font
    Việt/Nhật hiển thị đúng; MainLauncher → Module List → Start khởi động đủ 11/11 module (2026-10-01)
- [x] Central Package Management: `Directory.Packages.props` (version mọi package) + `Directory.Build.props`
  (Nullable/ImplicitUsings) + `Modules\Directory.Build.props` (khuôn module WinUI + Windows App SDK component);
  csproj module 68–105 → 14–57 dòng. Publish Release so với trước: 642/642 file trùng hash (chỉ
  `CsvEditor.deps.json` khác số version metapackage bị ExcludeAssets 1.0.0 → 1.1.5); build sln 0 warning
  (hết NETSDK1206), 318 test đạt (2026-10-01)
- [x] `build\Export-Module.ps1`: tách 1 module ra thư mục riêng (module + Common + SharedUI + props, .sln,
  build thử; `-Inline` ghi version vào csproj). Thử ImageCompare (CPM) + CsvEditor (-Inline): build 0 lỗi,
  chạy được. Đích dài > MAX_PATH làm app crash lúc khởi động → script cảnh báo (2026-10-01)
- [x] `build\Clean.ps1`: xoá bin\ obj\ cạnh .csproj (tuỳ chọn `-Application`, `-Targets`, `-WhatIf`), chặn khi có
  exe đang chạy từ thư mục sắp xoá; README thêm mục "Các script trong `build\`" (bảng 4 script + tham số
  Export-Module / Clean) (2026-10-01)
- [ ] Giảm tiếp: publish chung 1 thư mục runtime cho mọi module (dự kiến còn ~250–350 MB) + tuỳ chọn
  `-Standalone` cho bản lẻ 1 module
- [x] `Application\Modules\ModuleB\Data\DataFromExcel.db` (148 MB): không thuộc output publish — ModuleB tạo
  lúc chạy; publish lại sạch thì không còn. Chỉ cần nhớ không zip kèm thư mục đã từng chạy (2026-10-01)
- [x] Repo 9,8 GB do output build: xoá toàn bộ bin/obj; sửa `Sync-Modules-Dev.ps1` — build với RID tường minh
  + lấy `TargetDir` từ MSBuild (hết copy lồng `win-x64\`/`runtimes\`), xoá thư mục đích trước khi copy, dọn
  thư mục module không còn trong danh sách (ModuleD..H), ép module framework-dependent (pubxml template của
  ModuleA/B, ImageCompare đặt `SelfContained=true` cả lúc build). Output launcher Debug 2.882 → 612 MB (2026-10-01)
- [ ] Xoá `Properties\PublishProfiles\win-*.pubxml` (template, `SelfContained=true`) ở MainLauncher/ModuleA/
  ModuleB/ImageCompare? — chúng làm bản build VS F5 kèm .NET runtime; publish thật dùng `Publish-AppSuite.ps1`
- [x] Publish lại `Application\` sạch: 735 MB, không còn onnxruntime/DirectML (2026-10-01)
- [x] Build cả `AppSuite.sln` (x64, `--no-incremental`): 0 warning, 0 error (rà 2026-09-30)
- [ ] File này chưa có mục cho ModuleA/B, Mcf.DbDef, Mcf.Screen, Rdbms.HtmlGenerator — rà PLAN.md của
  chúng (2026-09-30) không thấy việc nào còn dở
- [x] Cửa sổ Hướng dẫn dùng chung `SharedUI.Help.HelpWindow` (dựng bằng code, module chỉ viết nội dung) + Hướng dẫn F1
  cho **CsvEditor** (6 danh mục, nút `?` cuối toolbar) và **ImageCompare** (9 danh mục, nút `?` góc trên-phải, mở đúng
  phần theo chế độ xem đang dùng). GUI Sandbox 14/14: nút + F1, tìm không dấu, đóng app đóng theo (2026-10-02)
- [x] **[Đề xuất · Nên có]** Hướng dẫn F1 cho các module còn lại: 4 module HtmlGenerator, ModuleB/C — dùng
  `SharedUI.Help.HelpWindow`. Mỗi module có `Views\HelpContent.cs` (viết theo code hiện tại, đã đối chiếu tên nút / ô
  tìm trong HTML sinh ra) + nút Hướng dẫn và phím F1; ModuleC gắn vào nút "Huong dan su dung" có sẵn. Build 6 module
  0 warning (2026-10-05). README / PLAN của ModuleB còn tả bản cũ (đọc .docx, không chỉ mục) - Hướng dẫn viết theo
  code thật
- [ ] Test GUI Hướng dẫn F1 của 6 module (Sandbox): mở bằng nút / F1, tìm kiếm trong cửa sổ, đóng app đóng theo
- [ ] **[Đề xuất · Có thể]** Chuyển Hướng dẫn của ScreenCapture / FileTools sang `SharedUI.Help.HelpWindow` (đang là 2
  bản chép riêng cùng bố cục)
- [ ] **[Đề xuất · Có thể]** Nhớ kích thước / vị trí cửa sổ và danh sách file / thư mục gần đây cho mọi module —
  đưa 1 helper dùng chung vào `Common` (hiện mỗi module tự lưu hoặc không lưu)

**Lưu ý vận hành**: chạy MainLauncher ở cấu hình nào (Debug/Release) thì phải chạy
`.\build\Sync-Modules-Dev.ps1 -Configuration <cấu hình đó>` để chép module vào cạnh launcher — thiếu
bước này thì Start module nào cũng lỗi "Executable not found" (đã gặp 2026-09-29).

## FileTools (module mới — xử lý file text/CSV/log lớn, `Modules/FileTools/PLAN.md`)

- [x] Đợt 1–2: khung + lõi đọc/ghi stream + **Nối file**, **Tách file** (dung lượng / số dòng / số phần),
  **Trích dòng** (đầu / X–Y / cuối); đăng ký sln / modules.json / build scripts; test 59/59; file 2 GB: trích
  đầu/cuối < 20 ms, tách + nối lại đúng từng byte, RAM 75 MB; GUI qua UI Automation đạt (2026-09-30)
- [ ] Người dùng thử đợt 1–2 trên file thật
- [x] Đợt 3: **Thông tin file**, **Tìm** (không phân biệt dấu), **Đổi encoding** + **Đổi xuống dòng** (hàng loạt,
  ghi đè giữ .bak, báo ký tự mất khi sang Shift-JIS), **Lọc dòng**; test 79/79; file 2 GB: thông tin 12,6 s, tìm 26 s,
  lọc 20 s; GUI qua UI Automation đạt cả 5 trang (2026-09-30)
- [ ] Người dùng thử đợt 3 trên file thật
- [x] Đợt 4 (CSV): **tách theo giá trị cột**, **bỏ dòng trùng** (cả dòng / theo cột), **chọn / sắp cột**, **đổi dấu
  phân cách**; test 97/97; Sandbox: unit test + GUI 4 trang đạt (sửa 1 lỗi bố cục trang Chọn cột). File 2 GB: bỏ trùng
  cả dòng 26 triệu dòng lúc đầu hết RAM (HashSet) → đổi sang `UInt64Set` 8 byte / ô cấp sẵn (2026-09-30)
- [ ] Người dùng thử đợt 4 trên file thật
- [x] Đợt 5: **So sánh 2 file**, **Theo dõi log**, **Thay thế hàng loạt** (xem trước rồi mới ghi), **Tìm file trùng**
  (chuyển bản thừa vào Thùng rác), **Mẫu (preset)**; test 120/120; Sandbox: GUI 5 phần đạt, sửa 1 lỗi (nút Xem trước
  không sáng khi thêm file sau) (2026-09-30)
- [ ] Người dùng thử đợt 5 trên file thật
- [x] Cửa sổ **Hướng dẫn** (F1 / nút ? / mục cuối menu, mở đúng trang đang xem, tìm không dấu); test đảm bảo mọi trang
  đều có hướng dẫn; kiểm tra GUI trong Sandbox đạt (2026-09-30)
- [x] Windows Sandbox (máy không cài .NET): unit test 79/79 + file 300 MB; GUI 8 trang đạt. Ảnh chụp 150% DPI lộ 4 lỗi
  bố cục, đã sửa: danh sách file trang hàng loạt bị ép về 0, trang không cuộn được (cắt nhật ký), tuỳ chọn trang Tìm
  tràn mép, ô từ khoá trang Lọc lệch (2026-09-30)
- [ ] Chạy FileTools từ MainLauncher (sau `Sync-Modules-Dev.ps1`)
- [x] Người dùng: "chuyển trang thì phải chọn lại file, cực quá" → **file đang làm dùng chung**: chọn file ở 1 trang,
  mở trang khác đã điền sẵn (luôn theo file mới nhất; danh sách nhiều file chỉ tự thêm khi trống; trang đang chạy giữ
  file cũ). **Gộp 8 trang thành 4** (16 → 12): Xem file (Thông tin + Trích dòng), Tìm / Lọc dòng, Đổi encoding /
  xuống dòng, CSV: Chọn cột / đổi dấu phân cách; mẫu đã lưu của trang cũ tự chuyển sang trang mới. Unit test 132/132,
  build 0 warning (2026-10-05)
- [ ] Test GUI FileTools sau khi gộp trang (Sandbox): chuyển trang giữ file, 4 trang gộp, Enter / Shift+Enter ở ô từ
  khoá, nạp mẫu cũ đã chuyển

## MainLauncher

- [x] Card module bị cắt mất hàng nút Start/Stop/Restart khi mô tả dài → card cao cố định, tên 1 dòng,
  mô tả tối đa 2 dòng + tooltip (2026-09-29)
- [x] Thu gọn khoảng trống thừa trong card (Height 228 → 186) (2026-09-29)
- [x] Bỏ hàng tile liên kết tài liệu (WinUI / Windows App SDK / Gallery / Toolkit) ở header Dashboard (2026-09-29)
- [x] Mở cửa sổ ở trạng thái maximize (2026-09-29)
- [x] README MainLauncher cập nhật theo các thay đổi trên (2026-09-29)

## ScreenCapture

- [x] Ảnh mới (giống New của PicPick): `Ctrl+N` / tab Tệp / nút ở cửa sổ chính → hộp thoại mẫu kích thước (clipboard,
  ảnh đang mở, màn hình, cỡ phổ biến), rộng / cao + đổi chiều, màu nền (mặc định Đen, nhớ lần trước) → tab mới trong
  Editor. GUI Sandbox 17/17 (2026-10-02)
- [x] Zoom ảnh trong Editor: cụm nút góc phải thanh trạng thái, Ctrl + lăn chuột, Ctrl + `+`/`-`/`0`,
  mỗi tab nhớ mức zoom riêng — kiểm tra GUI trong Windows Sandbox đạt (2026-09-29)
- [x] Cửa sổ Hướng dẫn liệt kê mọi tính năng (16 danh mục, tìm kiếm không dấu, phím tắt theo cài đặt);
  mở bằng nút ở cửa sổ chính, nút `?` / tab Tệp của Editor, menu khay, F1 — kiểm tra GUI đạt (2026-09-29)
- [x] Esc không huỷ được màn chọn vùng (Vùng chọn / trỏ-chọn cửa sổ / Chụp cuộn) — chỉ Vùng cố định có
  bắt phím; sửa cho mọi chế độ + dự phòng khi overlay không nhận được bàn phím — kiểm tra trong Sandbox đạt (2026-09-29)
- [x] Phím tắt đóng tab đang mở trong Editor: `Ctrl+W` / `Ctrl+F4` (ảnh chưa lưu thì hỏi như nút ×,
  giữ phím lặp không mở 2 hộp thoại) — kiểm tra GUI trong Sandbox 11/11 (2026-09-29)
- [ ] Zoom sâu trên ảnh lớn: hiện kẹp theo bộ nhớ (ảnh 1920×1080 tối đa ≈ 400%); muốn hơn thì phải đổi
  canvas sang chỉ vẽ phần đang hiển thị
- [x] Cắt khôi phục được **qua phiên làm việc**: tắt mở lại app vẫn kéo khung ra lấy lại phần đã cắt
  (lưu ảnh gốc vào phiên) — kiểm tra GUI trong Sandbox đạt (2026-09-29)
- [ ] Trỏ-chọn cửa sổ con (nút, vùng nội dung) như PicPick — hiện mới chọn cửa sổ cấp cao nhất
- [x] Mở ảnh có sẵn vào Editor: nút *Mở* ở tab Tệp, `Ctrl+O`, nút *Mở ảnh* ở cửa sổ chính, kéo-thả file; PNG / JPG / BMP /
  GIF / WEBP, nhiều file → nhiều tab, xoay theo EXIF, tab coi như đã lưu. Kèm sửa: tiêu đề cửa sổ chính mất khi hẹp (hàng
  nút giờ xuống dòng). GUI Sandbox 15/15 (2026-10-02)
- [x] Người dùng thử **kéo-thả** file ảnh vào Editor / cửa sổ chính trên máy thật (Sandbox không kiểm tự động được: agent
  chạy quyền admin nên Windows chặn thả từ Explorer) — OK (2026-10-05)
- [x] Lưu / Lưu thành: *Lưu* (`Ctrl+S`) ghi đè file đang gắn với tab (đã lưu / mở từ file / tự lưu, nhớ qua phiên), chưa
  có thì hỏi; *Lưu thành…* (`Ctrl+Shift+S`) PNG / JPG / BMP, chất lượng JPG trong Cài đặt > Lưu ảnh; ghi an toàn qua file
  tạm, lỗi IO báo không văng. Kèm sửa: gõ `x.jpg` ra `x.jpg.png`; lưu Cài đặt làm mất màu nền Ảnh mới. GUI Sandbox 14/14
  (2026-10-02)
- [x] Bug: dán ảnh to (Ctrl+V) rồi không vẽ thêm được — bấm đâu cũng chọn ảnh dán. Ảnh dán / Highlight / Mosaic-Blur giờ
  chỉ chọn bằng *Move* (handle góc của shape đang chọn vẫn kéo được); công cụ vẽ bấm bên trong là vẽ chồng lên. GUI
  Sandbox: 2 chữ nhật vẽ trên ảnh dán, Move + Delete xoá ảnh dán (2026-10-02)
- [x] Bug: taskbar / Alt+Tab không có icon (chỉ khay có). Thêm `Assets\ScreenCapture.ico` (vẽ như icon khay) làm
  `ApplicationIcon` của exe + `AppWindow.SetIcon` cho cửa sổ chính / Editor / Cài đặt / Hướng dẫn. Sandbox: taskbar,
  thanh tiêu đề, icon exe đều hiện (2026-10-02)
- [x] Các module khác cũng chưa có icon riêng (exe + taskbar) — đã làm cho mọi project, xem mục Chung (2026-10-02)
- [x] **[Đề xuất · Nên có]** Xoay 90° / lật ngang - dọc; đổi kích thước ảnh theo % hoặc px (co giãn nội dung — khác
  kéo khung ảnh hiện có). Nút *Xoay* (nhóm Cắt & Sửa): xoay phải / trái 90° (`Ctrl+R` / `Ctrl+Shift+R`), 180°, lật
  ngang / dọc, *Đổi cỡ ảnh…* (`Ctrl+E`, ô % và px đồng bộ, giữ tỉ lệ). Shape được thay bằng bản sao đã biến đổi (vẫn
  sửa được, 1 bước Undo); chữ / stamp giữ chiều đứng, stamp mũi tên đổi hướng, ảnh dán xoay theo; co giãn → nét, cỡ
  chữ, stamp co theo. Phần đã Cắt không khôi phục được sau khi xoay / đổi cỡ. Kiểm tra logic 24/24, build 0 warning
  (2026-10-05)
- [ ] Test GUI trong Sandbox: menu Xoay, Ctrl+R / Ctrl+E, hộp thoại Đổi cỡ ảnh, Undo / Redo, lưu phiên rồi mở lại

### Còn thiếu so với PicPick (rà 2026-10-05) — làm theo 3 đợt, chờ người dùng chọn đợt / tính năng

Đã tương đương: chụp toàn màn hình / cửa sổ / vùng / vùng cố định / cuộn dọc-ngang / lặp lần trước / hẹn giờ, phím tắt,
khay; Editor tab, hình vẽ, tô màu, stamps, Mosaic / làm mờ, cắt, khung ảnh, dán, vùng chọn, zoom, Undo, xoay / lật /
đổi cỡ, lưu / tự lưu / copy. (★ = mức nên làm)

**Đợt 1 — Chữ & hình vẽ** (dùng hằng ngày khi viết tài liệu / báo lỗi):
- [x] ★★★ Chữ: chọn font, đậm / nghiêng, nền ô chữ, viền chữ; gõ / sửa chữ ngay trên ảnh (hiện nhập qua hộp thoại).
  Ô gõ đặt đè đúng chỗ chữ (Enter xuống dòng, Esc / Ctrl+Enter / bấm ra ngoài = xong, nhấp đúp hoặc F2 / Enter để
  sửa), chữ nhiều dòng; kéo handle góc = đổi cỡ chữ; chữ Nhật / Trung / Hàn tự lấy phông Windows có ký tự đó (trước
  ra ô vuông) (2026-10-06)
- [x] ★★ Hình: tô nền cho chữ nhật / elip, nét đứt, bo góc, độ trong suốt, nhiều kiểu đầu mũi tên — tab contextual
  *Định dạng* (chỉ hiện nhóm hợp với loại hình; áp cho hình đang chọn + nhớ riêng cho từng công cụ); nhóm Màu & Cỡ nét
  chuyển theo sang tab này; đầu mũi tên: không / tam giác / chữ V / chấm tròn ở cả 2 đầu, to theo Size (2026-10-06)
- [x] ★★ Khung chú thích (callout / bong bóng lời nói) — công cụ *Chú thích*: kéo khung hoặc bấm 1 cái, gõ chữ luôn
  (tự xuống dòng, khung tự cao thêm), kéo chấm ở đầu đuôi để chỉ chỗ khác; viền + chữ Color1, nền Color2, bo góc /
  nét đứt / độ đục; xoay / lật ảnh, Cắt, lưu phiên đều giữ (2026-10-06)
- [x] Test GUI Đợt 1 trong Sandbox (bản Release): bấm Text lên ảnh → gõ (Enter xuống dòng, dán chữ Nhật) → Esc; đậm +
  cỡ 40 ở tab Định dạng; nhấp đúp sửa chữ nhiều dòng và chữ của phiên cũ; khung chú thích (kéo khung, gõ chữ, kéo đuôi);
  chữ nhật tô nền / nét đứt / độ đục 50% / bo góc 12; mũi tên 2 đầu; Undo / Redo; đóng → session.json → mở lại: 12/12.
  Test bắt được và đã sửa 2 lỗi: chữ nhiều dòng mất dòng 2 khi sửa lại; ô gõ chữ tự đóng do vùng ảnh giành focus
  (2026-10-06)
- [ ] Người dùng thử tay Đợt 1 (font khác, viền chữ, co giãn chữ bằng handle, xoay ảnh có khung chú thích)

**Đợt 2 — Hiệu ứng ảnh:**
- [ ] ★★★ Viền ảnh (border), đổ bóng, mép rách (torn edge)
- [ ] ★★ Độ sáng / tương phản, làm xám, đảo màu, sepia, làm nét
- [ ] ★★ Nền trong suốt cho Ảnh mới (cần sửa Cắt / đổi khung đang tô nền trắng)
- [ ] ★ Watermark chữ / ảnh

**Đợt 3 — Chụp, công cụ phụ, lưu & chia sẻ:**
- [ ] ★★ Chụp tự do (lasso - vẽ đường viền bất kỳ)
- [ ] ★★ Chụp 1 màn hình (màn đang có chuột) khi dùng nhiều màn hình — "Toàn màn hình" hiện gộp mọi màn hình
- [ ] ★★ Hút màu trên màn hình + bảng màu
- [ ] ★★ In ảnh
- [ ] ★★ Mẫu tên file khi tự lưu (vd `{date}_{window}`)
- [ ] ★ Tuỳ chọn chụp kèm con trỏ chuột
- [ ] ★ Kính lúp, thước đo pixel, đường chữ thập, thước đo góc, bảng trắng vẽ lên màn hình
- [ ] ★ Lưu PDF / GIF; gửi email / Office / mở bằng chương trình khác; chọn việc tự làm sau khi chụp (Editor / lưu /
  copy / in)
- (Trỏ-chọn cửa sổ con ★★ — đã có mục riêng ở trên)

## ImageCompare

- [x] Module So sánh ảnh + Tìm chữ (OCR Tesseract, tiếng Việt) — kiểm tra engine 30/30 + GUI trong Sandbox (2026-09-25)
- [x] Commit toàn bộ module + đăng ký vào sln / modules.json / build scripts
- [ ] Chạy thử GUI hộp thoại *Lưu PNG* và *Xuất báo cáo HTML*
- [ ] Chạy bản Release trên máy thật từ MainLauncher (kể cả Tìm chữ):
  `.\build\Publish-AppSuite.ps1 -Targets ImageCompare,MainLauncher`
- [x] Chế độ **So chữ** (A ↔ B qua OCR: đổi chữ / chỉ A / chỉ B / khác màu chữ / gần giống; tiếng Nhật = Windows OCR,
  dự phòng Tesseract jpn; Việt / Anh = Tesseract vie) + cảnh báo ở Khác biệt khi 1 vùng phủ ≥ 60% ảnh. Unit test 61/61
  và GUI trong Sandbox (số Khác biệt không đổi so với bản cũ); nhánh Windows OCR chạy app trên máy dev: 130 đoạn giống,
  54 chỗ khác, 6 khác màu chữ thật — xem PLAN.md (2026-10-01)
- [x] So chữ hiện chữ sai (người dùng báo: "230川7" → "230107"…): thêm bước đọc lại riêng từng chỗ nghi khác
  (TextDiffVerifier) + kẹp ngưỡng đen trắng giữ chữ xám. Ảnh thật (Windows OCR): 54 → 26 chỗ khác, 130 → 158 giống,
  khác màu chữ 6 → 9; unit test 68/68 trong Sandbox, Khác biệt vẫn cùng số (2026-10-01)
- [x] So chữ ghép nhầm nhãn + giá trị combobox (`受注区分受注`, `使用インキ耐光24H`) và cắt `ページ数` thành 2 mục: tách
  đoạn tại viền ô + nhập mảnh lẻ vào cặp. Ảnh thật: 184 đoạn giống (158 trước), unit test 70/70 (2026-10-02)
- [x] So chữ: chữ xám của ô bị khoá báo "đổi chữ" (`三 → 受注`, `商印そ → 商印その他`, `耐 → 耐光2`…) → đọc lại vùng nới
  bằng khung phía kia + so màu khi đọc lại giống. Ảnh thật: khác màu 9 → 15, đổi chữ 8 → 1; Sandbox (Tesseract) 71 → 56
  chỗ khác; unit test 72/72 cả 2 nơi (2026-10-02)
- [x] So chữ: mục chỉ-1-phía mà OCR bỏ sót phía kia (`枚` xám đọc thành `物`, `胴ｻｲｽﾞ`) → đọc lại vùng dự đoán cắt sát
  + so hình nét chữ (không OCR). Ảnh thật: khác màu 15 → 19, chỉ còn khung trình duyệt; Sandbox (Tesseract) 56 → 45;
  unit test 74/74 cả 2 nơi (2026-10-02)
- [ ] So chữ: còn vài chỗ font bitmap của ảnh cũ đọc sai (`角途区分`, `使用インキ耐、`) và thanh tiêu đề / URL — cân nhắc
  tự đề xuất vùng bỏ qua cho khung trình duyệt; thử thêm trên vài cặp màn hình khác (Việt / Anh chưa có ảnh thật)
- [x] So chữ: xuất kết quả - nút Copy (tách Tab, dán Excel) / Lưu CSV (BOM) / Xuất báo cáo HTML (ảnh cắt A | B); chuột
  phải 1 mục trong danh sách (mọi chế độ): Copy mục này / Copy cả danh sách. Unit test 77/77; GUI trong Sandbox: menu,
  clipboard, hộp thoại lưu CSV + HTML đều chạy (2026-10-02)
- [ ] Chạy bản Release So chữ trên máy chưa có gói OCR tiếng Nhật của Windows nhưng có mạng: thử hướng dẫn cài trong README
- [x] **[Đề xuất · Nên có]** Khác biệt cho 2 ảnh lệch bố cục dần (đã phân tích 2026-10-02: B giãn 7 → 46 px từ trên
  xuống, dịch tay cả ảnh không giúp): người dùng chọn *Soi 1 vùng* — mục căn mới "Soi 1 vùng (khoanh trên ảnh)": kéo
  chuột khoanh vùng → vùng tự tìm chỗ khớp ở B, từng ô (nhãn / ô nhập) căn riêng, chỉ so vùng đó (`RegionAligner`).
  Unit test 85/85, build 0 warning. Ảnh thật IE ↔ Edge: khối 受注数量 74,6% → 89% giống; phần đỏ còn lại là khác cách
  vẽ chữ / viền ô (so pixel không bỏ được — dùng So chữ) (2026-10-05)
- [ ] Test GUI *Soi 1 vùng* trong Sandbox: chọn mục căn, kéo khoanh, khoanh lại, Lưu PNG / báo cáo HTML
- [x] Bug (người dùng báo): **Tìm chữ không thấy chữ Nhật** (tìm 確定状況 → "Không thấy", danh sách dòng toàn ký tự rác) -
  Tìm chữ luôn đọc OCR tiếng Việt / Anh. Thêm ô ngôn ngữ ở Tìm chữ (dùng chung với So chữ, cùng bộ đọc + cache: Tiếng
  Nhật = Windows OCR `ja`, dự phòng Tesseract `jpn`), bảng kết quả ghi "Đọc bằng …". Kèm **tìm gần đúng (≈)**: không khớp
  chính xác thì cho sai / thiếu / thừa 1 ký tự (OCR đọc 受注数量 → 受淺数量, 受注金額合計 → 受注金合計). Ảnh của người
  dùng: 9 / 9 nhãn thử đều tìm thấy (5 chính xác, 4 gần đúng; trước: 0). Unit test 90/90, build 0 warning (2026-10-05)
- [ ] Người dùng thử lại Tìm chữ trên máy thật với ảnh tiếng Nhật (Sandbox không có gói OCR tiếng Nhật của Windows)
- [x] Bug (người dùng báo, ảnh chụp): hàng tuỳ chọn tràn → thanh cuộn ngang **nổi đè lên** các ô (che mép dưới ô tìm / ô
  chọn) - lộ ra khi Tìm chữ thêm ô ngôn ngữ. Khi tràn tự chừa lề dưới 14 px cho thanh cuộn (`Options_SizeChanged`, không
  tràn thì giữ chiều cao cũ); thu gọn hàng Tìm chữ (ô tìm 220 → 190, ô ngôn ngữ, nút "Copy chữ"). Build 0 warning; chưa
  xem lại trên GUI (2026-10-05)
- [ ] **[Đề xuất · Có thể]** Phương án A *Căn từng vùng* cả trang (tự chia ô như Soi 1 vùng nhưng không cần khoanh)
- [ ] **[Đề xuất · Nên có]** So sánh hàng loạt 2 thư mục ảnh (ghép theo tên file) → bảng tổng hợp giống / khác + báo
  cáo HTML — cho kiểm thử hồi quy nhiều màn hình 1 lần
- Giới hạn đã biết: OCR không chạy trên ARM64 (không có bản Tesseract native)

## Mcf.CrudDiagram.HtmlGenerator (ModuleH — `Note/ModuleH.txt`)

- [x] Dòng không có dữ liệu thì không vẽ
- [ ] Chạy UI thật (chọn thư mục → Đọc Excel → Xuất HTML → Mở HTML): index đủ 71 logic, tìm `MAM_BP`
  ra đúng các logic dùng bảng đó — PLAN.md ghi "chưa tự kiểm chứng bằng UI thật, cần user xác nhận"
- [x] Header bảng giữ nguyên khi scroll: `.table-scroll` cuộn cả 2 chiều, cao tối đa `100vh - 32px` (sticky chỉ dính theo
  khung cuộn gần nhất); `th` sticky + viền dưới bằng `box-shadow` (border-collapse làm mất viền khi dính); nhảy tới khối ID
  chừa 44px cho header. Kiểm bằng Edge: cuộn tới dòng 33 header vẫn ở trên, `#blk-...` không bị che. Cần xuất lại HTML
  để trang cũ có CSS mới (2026-10-02)

## ModuleC

- [ ] Nút "Huong dan su dung" vẫn là placeholder chưa gắn hành vi (PLAN.md ghi cố ý để sau)
- [ ] **[Đề xuất · Có thể]** Nhớ thư mục nguồn lần trước như Mcf.Screen / Mcf.CrudDiagram (ModuleB, ModuleC,
  Mcf.DbDef.HtmlGenerator chưa lưu)

## CsvEditor (ModuleF — `Note/ModuleF.txt`)

- [x] Các tính năng theo spec: mở/lưu CSV/TSV (tự nhận delimiter/encoding), sửa ô, thêm/xoá dòng/cột,
  đổi tên cột, Find/Replace, Filter, Sort nhiều cột, Statistics, Undo/Redo, context menu, validation
- [x] Unit test theo spec: Open, Save, Search, Sort, Filter, Statistics, Undo, Redo — `Tests\CsvEditor.Tests`
  (xUnit v3), 70/70 đạt trên máy dev và trong Windows Sandbox không cài .NET (2026-09-29)
- [x] Bug tìm ra nhờ test: tooltip Undo sau Xóa cột hiện sai tên cột, xoá cột cuối thì văng
  `ArgumentOutOfRangeException` — đã sửa `RemoveColumnCommand` (2026-09-29)
- [x] Sort/Filter/Statistics đọc số theo culture máy — máy vi-VN/de-DE hiểu "2.5" thành 25 (sắp xếp,
  lọc, tổng sai). Sửa: `Services/CsvNumber` đọc dấu chấm thập phân trước, rồi dấu thập phân của máy,
  không đoán dấu phân nhóm; +15 test culture, 85/85 đạt trên máy dev và Sandbox (2026-09-29)
- [x] Dọn 5 warning `WMC1506`: bind thẳng vào thuộc tính có thông báo của ViewModel (`IsBusy`,
  `IsDirty`, `Issues.Count`), bỏ `Bindings.Update()` chạy lại mọi binding sau mỗi thay đổi (2026-09-29)
- [x] 2 thiếu sót lộ ra khi bỏ `Bindings.Update()` (đã sửa, có test): nút Undo/Redo không bật lại
  (thiếu `NotifyCanExecuteChanged`); mở file mới không tắt nút Clear Filter/Clear Sort (2026-09-29)
  — build 0 warning; GUI Sandbox 11/11, unit test 89/89
- [x] Mở file không có dòng tiêu đề: checkbox "Dòng đầu là tiêu đề" cạnh Open (người dùng chọn, không đoán); bỏ tick →
  cột `Cột 1`…, số cột theo dòng dài nhất, Save không ghi dòng tên cột; đổi khi đang mở → mở lại (hỏi nếu chưa lưu).
  Kèm sửa: mở lại sau Save As đọc nhầm file cũ. Unit test 93/93, GUI Sandbox 16/16 (2026-10-02)
- [x] `PLAN.md` lỗi thời ("Bỏ unit test", mục "Rủi ro chưa kiểm chứng", Sort đọc số theo culture) —
  cập nhật theo test 89/89 + GUI Sandbox 11/11 + `CsvNumber` (2026-09-30)
- [x] Hỏi *Lưu / Không lưu / Hủy* khi còn thay đổi chưa lưu lúc đóng cửa sổ, Open file khác, mở lại (đổi encoding /
  delimiter, đổi "Dòng đầu là tiêu đề"); Lưu khi chưa có file → Save As; huỷ Save As / lưu lỗi → dừng, không mất thay
  đổi. Lưu lỗi (file bị khoá…) báo hộp thoại thay vì văng app. GUI Sandbox (21 bước, cả 2 việc) đạt (2026-10-02)
- [x] Chế độ có tiêu đề, dòng dài hơn header: tự thêm cột `Cột N` cho phần dư (+ 1 cảnh báo; tên cột ghi vào dòng tiêu
  đề khi lưu) — trước đây field thừa bị cắt khi Save. Cảnh báo lệch cột tính lại sau sửa so với số field phổ biến nhất
  (không báo cả bảng khi chỉ 1 dòng dài). Unit test 96/96 (2026-10-02)
- [x] Kéo-thả file `.csv/.tsv/.txt` vào cửa sổ để mở; nút 🕘 *File gần đây* (10 file, `Data\Config\recent-files.json`,
  file đã xoá → báo + bỏ khỏi danh sách, *Xoá danh sách*); `Ctrl+O` = Open; mở lỗi (file khoá…) báo hộp thoại thay vì văng
  (2026-10-02)
- [x] *Export .xlsx* (Open XML SDK, ghi streaming + file tạm): xuất các dòng đang hiện theo lọc / sắp xếp; số thật ghi
  thành số, mã `00123` / 16 chữ số / `=…` giữ dạng chữ; tiêu đề đậm + cố định + nút lọc; báo khi vượt giới hạn Excel.
  *Go To* `Ctrl+G` (theo số dòng đang hiện). Copy / Paste dạng Tab bọc `"…"` như Excel (ô nhiều dòng dán sang Excel đúng
  1 ô). Unit test 120/120 (2026-10-02)
- [x] Thử tay trên máy thật: kéo-thả file từ Explorer vào CsvEditor (Sandbox không tự động hoá được: UIPI chặn thả từ
  Explorer vào app chạy quyền cao); mở file .xlsx vừa xuất bằng Excel thật (Sandbox không có Excel) — OK (2026-10-05)
- [ ] **[Đề xuất · Có thể]** Cố định (freeze) cột đầu khi cuộn ngang; ẩn / hiện cột; tự giãn độ rộng cột
