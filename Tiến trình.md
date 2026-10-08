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
- [ ] ScreenCapture Đợt 1 (chữ & hình vẽ) + Đợt 2 (hiệu ứng ảnh) — danh sách thử tay chi tiết ở mục ScreenCapture >
  "Người dùng thử tay Đợt 1 / Đợt 2"; đã OK: gõ chữ (Unikey / IME), Enter / Esc / nhấp đúp / F2, đổi định dạng khi
  đang gõ, gõ khi zoom, kéo góc chữ (2026-10-06)
- [x] **Commit** toàn bộ thay đổi ScreenCapture ngày 2026-10-06 (Đợt 1, Đợt 2, ô "Ảnh x / n", tab Định dạng tự về
  Trang chủ, nới ô Kiểu nét, kéo khung chữ) — đã commit (2026-10-07, trước Đợt 3a)

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
  vẽ**~~ · ~~**2. hiệu ứng ảnh**~~ — cả 2 xong + test GUI Sandbox 2026-10-06, còn người dùng thử tay ·
  **3. chụp & công cụ** — đề xuất làm 3 lượt (2026-10-06): (a) 1 màn hình, mẫu tên file, kèm con trỏ, việc tự làm sau
  khi chụp · (b) hút màu + bảng màu, in, lưu PDF · (c) lasso, cửa sổ con, kính lúp / thước…
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
- [x] Test GUI Hướng dẫn F1 của 6 module (Sandbox, bản Release): mở bằng nút / F1, bấm lần 2 không mở thêm, tìm không
  dấu ("tim") + từ vô nghĩa → "Không tìm thấy", đóng app → Hướng dẫn đóng theo, process thoát. `help6` 42/42 (2026-10-08)
- [x] **[Đề xuất · Có thể]** Chuyển Hướng dẫn của ScreenCapture / FileTools sang `SharedUI.Help.HelpWindow` (đang là 2
  bản chép riêng cùng bố cục) → bỏ `Views\HelpWindow.xaml(.cs)` của cả 2. Bản chung thêm `UpdateSections` (ScreenCapture:
  đổi phím tắt trong Cài đặt), `ShowSection` cuộn danh mục tới mục đang chọn (FileTools: F1 mở đúng trang), tuỳ chọn
  `searchSummary` (FileTools tìm cả tóm tắt trang: "healthcheck"). ScreenCapture dùng record `HelpSection` / `HelpItem`
  của SharedUI; FileTools giữ `Core\HelpContent` (không phụ thuộc WinUI, có unit test) và đổi sang record SharedUI lúc
  mở cửa sổ. Sửa thêm: đóng FileTools giờ đóng luôn cửa sổ Hướng dẫn (trước đó process còn chạy). Build 0 warning,
  FileTools.Tests 131/131; Sandbox `help_scft` 22/22 (FileTools: ? / chân menu / F1 mở đúng mục + cuộn tới, tìm không
  dấu + tóm tắt, 1 cửa sổ, đóng app đóng theo; ScreenCapture: nút cửa sổ chính / ? Editor / F1, phím tắt theo cài đặt,
  tìm "cat" / "mui ten", 1 cửa sổ). README SharedUI / ScreenCapture / FileTools (2026-10-08)
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
- [x] Test GUI FileTools sau khi gộp trang (Sandbox, bản Release): mẫu cũ của 4 trang bỏ tự chuyển (Query → Terms, trùng
  tên thêm "(Lọc dòng)", Đổi xuống dòng → Xuống dòng LF + Encoding giữ như nguồn, Đổi dấu phân cách → Tab), chuyển trang
  giữ file đang làm (Xem file → Tìm → Đổi encoding → CSV, đổi file ở trang khác thì trang kia đổi theo), Shift+Enter
  thêm dòng / Enter = Tìm (10 dòng khớp), Lọc ra file giữ tiêu đề, CSV ghi ra .tsv. `ft_merge` 19/19 (2026-10-08)

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
- [x] Test GUI trong Sandbox: menu Xoay, Ctrl+R / Ctrl+E, hộp thoại Đổi cỡ ảnh, Undo / Redo, lưu phiên rồi mở lại
  (`sct_orient`: Xoay phải / trái, Lật ngang, Undo, mở lại; `sct_resize` 13/13: menu + Ctrl+E mở hộp thoại, 50% giữ tỉ
  lệ → 500 × 260 + hình co theo, bỏ giữ tỉ lệ → 800 × 520, Ctrl+E lần 2 khi đang mở không lỗi, Undo / Redo, mở lại app
  giữ cỡ, Hủy không đổi gì) (2026-10-08)
- [x] Thanh trạng thái không cập nhật sau Undo / Redo (Đổi cỡ ảnh → Undo: ảnh về 1000 × 520 nhưng vẫn ghi "ảnh giờ là
  500 × 260 px") → giờ ghi bước vừa làm: "Undo: Đổi cỡ ảnh - ảnh giờ là 1000 × 520 px." / "Redo: Thêm Mũi tên." (cỡ ảnh
  chỉ ghi khi ảnh đổi cỡ). Sandbox `sct_resize` (2026-10-08)
- [x] Hộp thoại Đổi cỡ ảnh: gõ rộng 99999 → px kẹp 16384 × 8520 nhưng ô % dừng ở 1000 (thật ~1638%) và nút Đổi cỡ vẫn
  bật → ô % tối đa theo đúng cạnh 16384 px của từng chiều (khớp px); thêm giới hạn **64 triệu pixel** (~256 MB / ảnh,
  vd 8000 × 8000) cho cả Đổi cỡ ảnh lẫn Ảnh mới: quá thì nút mờ + ghi "… triệu pixel - quá lớn". Sandbox `sct_resize`
  18/18 (2026-10-08)
- [x] Menu Xoay > **Về hướng ban đầu**: xoay / lật ngược mọi lần Xoay / Lật trước đó trong 1 bước Undo (tối đa 2 phép),
  hình vẽ đi theo ảnh, mờ khi ảnh chưa xoay; hướng ảnh (`ImageOrientation`) lưu qua phiên → mở lại app vẫn về được.
  Kiểm tra logic: mọi chuỗi ≤ 5 phép xoay / lật (3906) về đúng từng pixel; GUI Sandbox `sct_orient` 6/6 (người dùng đề
  xuất, 2026-10-07); GUI `sct_extra`: Ctrl+R → Lật dọc → Xoay 180° → về ban đầu → Undo → Redo đúng, lưu phiên OK

### Còn thiếu so với PicPick (rà 2026-10-05) — làm theo 3 đợt: Đợt 1, 2 xong (2026-10-06, chờ thử tay), Đợt 3 chưa làm

Đã tương đương: chụp toàn màn hình / cửa sổ / vùng / vùng cố định / cuộn dọc-ngang / lặp lần trước / hẹn giờ, phím tắt,
khay; Editor tab, hình vẽ, tô màu, stamps, Mosaic / làm mờ, cắt, khung ảnh, dán, vùng chọn, zoom, Undo, xoay / lật /
đổi cỡ, lưu / tự lưu / copy. (★ = mức nên làm)

**Đợt 1 — Chữ & hình vẽ** (dùng hằng ngày khi viết tài liệu / báo lỗi):
- [x] ★★★ Chữ: chọn font, đậm / nghiêng, nền ô chữ, viền chữ; gõ / sửa chữ ngay trên ảnh (hiện nhập qua hộp thoại).
  Ô gõ đặt đè đúng chỗ chữ (Enter xuống dòng, Esc / Ctrl+Enter / bấm ra ngoài = xong, nhấp đúp hoặc F2 / Enter để
  sửa), chữ nhiều dòng; kéo handle góc = đổi khung chữ (Ctrl + kéo = đổi cỡ chữ, xem mục thử tay bên dưới); chữ Nhật /
  Trung / Hàn tự lấy phông Windows có ký tự đó (trước ra ô vuông) (2026-10-06)
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
- Người dùng thử tay Đợt 1 (máy thật, chạy từ MainLauncher):
  - [x] Gõ tiếng Việt (Unikey / EVKey Telex) và tiếng Nhật (IME, chọn chữ Hán) trong ô gõ chữ trên ảnh — OK (2026-10-06)
  - [x] Enter xuống dòng; Esc / Ctrl+Enter / bấm ra ngoài = xong; nhấp đúp / F2 sửa chữ (cả nhiều dòng); đang gõ bấm
    sang ô Phông / màu → chữ được ghi và định dạng áp lên chữ đó — OK (2026-10-06)
  - [x] Gõ chữ khi zoom 50% / 200% (ô gõ đúng chỗ, đúng cỡ); kéo handle góc đổi cỡ chữ — OK (2026-10-06)
  - [x] Người dùng muốn kéo góc chữ = đổi KHUNG chữ như PicPick → kéo góc: khung cố định bề rộng, chữ tự xuống dòng,
    cỡ chữ giữ nguyên, khung không thấp hơn chữ (nền ô chữ phủ cả khung); giữ Ctrl khi kéo = đổi cỡ chữ (cách cũ).
    Lưu phiên / xoay ảnh / ô gõ chữ (tự xuống dòng) theo khung. GUI Sandbox: thu khung "old text" còn 50 px → 2 dòng cỡ
    20; Ctrl + kéo → cỡ 40.8; bộ Đợt 1 vẫn 12/12 (2026-10-06)
  - [ ] **[Đề xuất · Có thể]** Định dạng riêng từng phần chữ trong 1 khung (đậm / màu 1 từ) - hiện phông, cỡ, màu áp cho
    cả khung (người dùng ghi nhận khi thử, 2026-10-06)
  - [ ] Tab Định dạng: vài phông (Meiryo, Yu Gothic, Times), đậm / nghiêng, nền ô chữ, viền chữ + màu viền; chữ nhật /
    elip tô nền, bo góc, nét đứt / chấm, độ đục; các kiểu đầu mũi tên, mũi tên 2 đầu, Size lớn; chữ trên tab không bị
    cắt; vẽ xong → Định dạng, bấm chỗ trống → Trang chủ
  - [ ] Khung chú thích: bấm 1 cái / kéo khung, chữ dài tự cao khung, kéo đuôi, co giãn khung, xoay / lật ảnh có khung
  - [ ] Ô "Ảnh x / n" khi chuyển / đóng tab; tắt mở lại app → hình (phông, khung chú thích, mũi tên 2 đầu) còn nguyên
- [x] Tab Định dạng: ô Kiểu nét bị cắt chữ ("Nét lì") ở DPI 150% → nới ô Kiểu nét / Đầu mũi tên, kiểm tra ảnh chụp Sandbox
  150% hiện đủ chữ (người dùng báo, 2026-10-06)
- [x] Đang gõ / sửa chữ trên ảnh (Text, Khung chú thích) → tự mở tab Định dạng với phông / cỡ / màu của chữ đó; gõ xong
  bấm chỗ trống → về Trang chủ, Esc → giữ chọn chữ ở tab Định dạng (người dùng báo, 2026-10-07) — người dùng thử tay Sandbox OK (2026-10-07)
- [x] Tab Định dạng: ô Cỡ chữ (NumberBox Compact) bật khung ▲▼ nổi đè lên ô phông / nút B I → đổi thành ô xổ xuống gõ được
  như Word (8…128, gõ số khác 6–400 rồi Enter) (người dùng báo, 2026-10-07); GUI `sct_extra` 13/13: đổi cỡ khi đang gõ chữ, bấm chuột chọn 36 trong danh sách, chọn lại chữ hiện đúng cỡ
- [x] Ô Cỡ chữ: gõ 50 + Enter thì ô trắng (ComboBox gõ được hiện lại chữ của SelectedItem, 50 không có trong danh sách)
  → cỡ lẻ được chèn tạm vào danh sách, áp cỡ sau khi ComboBox xong lượt gõ. GUI Sandbox `sct_fontsize` 7/7: gõ 50 / abc /
  33.5, chọn 8, cỡ tạm bỏ khỏi danh sách, lưu phiên FontSize 72 (người dùng báo, 2026-10-07)
- [x] README ScreenCapture cập nhật theo Đợt 1 + 2 + các sửa 2026-10-07 (trước đó còn ghi Text "nhập qua dialog"): gõ chữ
  trên ảnh, Chú thích, tab Định dạng, menu Xoay (cả Về hướng ban đầu), Hiệu ứng, nền trong suốt, "Ảnh x / n", phím tắt
  Ctrl+R / Ctrl+E / F2 (2026-10-07)
- [x] Vẽ xong hình → tab Định dạng; bấm chỗ trống / Esc để bỏ chọn → tự về Trang chủ để chọn công cụ khác (người dùng
  góp ý). Tự mở Định dạng khi chưa chọn hình (đặt định dạng trước khi vẽ) thì giữ nguyên. Test GUI Sandbox 10/10, bộ
  test Đợt 1 vẫn 12/12 (2026-10-06)
- [x] Thanh trạng thái Editor (góc phải, cạnh zoom): "Ảnh x / n" = tab ảnh đang mở là thứ mấy trên tổng số tab, đổi
  theo khi chuyển / đóng / thêm tab. Test GUI Sandbox 5/5 (mở phiên 3 tab, chọn tab, đóng tab khác / tab đang mở)
  (2026-10-06)

**Đợt 2 — Hiệu ứng ảnh:**
Nút *Hiệu ứng* (nhóm Cắt & Sửa) - chỉ áp lên ảnh nền, hình đã vẽ giữ nguyên (dời theo khi ảnh nới ra), mỗi lần 1 bước
Undo; mục có "…" mở hộp thoại tuỳ chọn + xem trước (kể cả hình đã vẽ), tuỳ chọn nhớ trong lúc app chạy.
- [x] ★★★ Viền ảnh (border), đổ bóng, mép rách (torn edge) — viền màu / độ dày; bóng mờ dưới-phải theo đúng hình ảnh
  (mép rách trước rồi đổ bóng thì bóng theo răng cưa), nền quanh bóng trong suốt; xé cạnh chọn, độ sâu (2026-10-06)
- [x] ★★ Độ sáng / tương phản, làm xám, đảo màu, sepia, làm nét (2026-10-06)
- [x] ★★ Nền trong suốt cho Ảnh mới (cần sửa Cắt / đổi khung đang tô nền trắng) — Ảnh mới có nền "Trong suốt"; ảnh có
  phần trong suốt thì nới khung / xoá vùng / Cut / dán nới ảnh tô trong suốt; Editor hiện ô caro (2026-10-06)
- [x] ★ Watermark chữ / ảnh — chữ (phông, cỡ, đậm, màu) hoặc ảnh (cỡ %), độ đục, giữa / 4 góc / lặp chéo kín ảnh;
  chỉ in lên phần ảnh, không ra nền trong suốt (2026-10-06)
- [x] Kiểm tra: console 18/18 (số liệu từng hiệu ứng, ma trận màu Skia dịch theo thang 0-1); GUI Sandbox 15/15 (viền →
  mép rách → đổ bóng → làm xám → watermark → Undo / Redo → session: cỡ 1063 × 583, góc bóng trong suốt, chữ dời đúng
  27 px). Test bắt được: SKXamlCanvas trong ContentDialog không vẽ → xem trước dùng Image + WriteableBitmap (2026-10-06)
- Người dùng thử tay Đợt 2:
  - [ ] Từng hiệu ứng trên ảnh chụp thật (ảnh lớn / 4K): xem trước hiện, kéo thanh trượt mượt; Undo / Redo
  - [ ] Mép rách + đổ bóng → lưu PNG (mở bằng Photos: nền trong suốt) và JPG (nền trắng)
  - [ ] Copy ảnh có bóng rồi dán vào Word / Excel / Teams / Outlook — nền trong suốt có ra đen / caro không
  - [ ] Watermark chữ (4 góc, lặp chéo) và ảnh logo PNG thật
  - [ ] Ảnh mới nền Trong suốt → vẽ, nới khung, Select → Cut: phần mới trong suốt chứ không trắng
  - [ ] F1: các mục mới (Text, Khung chú thích, Tab Định dạng, Hiệu ứng ảnh, Nền trong suốt)

**Đợt 3 — Chụp, công cụ phụ, lưu & chia sẻ** (chưa làm; đề xuất chia 3 lượt theo công sức, 2026-10-06 — chờ người
dùng chọn lượt / mục; nên commit Đợt 1 + 2 trước):

*Lượt 3a — việc nhỏ, dùng ngay:*
- [x] ★★ Chụp 1 màn hình (màn đang có chuột) khi dùng nhiều màn hình — kiểu chụp **Màn hình hiện tại**: thẻ ở cửa sổ
  chính (lưới thành 2 × 4, thêm thẻ *Chụp lại lần trước*), menu khay, phím tắt (mặc định chưa gán), Chụp lại lần gần
  nhất (2026-10-07)
- [x] ★★ Mẫu tên file khi tự lưu — Cài đặt > Lưu ảnh > *Mẫu tên file* (chọn mẫu / gõ tuỳ ý + xem trước), mặc định
  `{date}_{time}_{app}` → `2026-10-07_10-31-10_EXCEL.png` (theo người dùng); thẻ {date} {time} {app} {window} {mode} {size}
  {n}, `\` = thư mục con. Cửa sổ bị chụp: vùng = cửa sổ trên cùng chứa tâm vùng (liệt kê cùng lúc ảnh nền đứng yên).
  Console 16/16 (`FileNameTemplate`); GUI Sandbox `sct_3a` 10/10: ảnh = đúng 1 màn hình 1514×900, tên
  `…_powershell.png`, `{window}_{n}` 001 → 002 (cả Chụp lại lần trước), `{date}\{mode}_{n}` thư mục con, gõ mẫu tuỳ ý
  trong Cài đặt không trắng ô + lưu settings.json. F1 + README cập nhật (2026-10-07) — cần thử tay trên máy 2 màn hình
- [x] Mẫu `{date}\{date}_{time}_{app}.png` (người dùng đề xuất, 2026-10-07): thêm vào danh sách mẫu; đuôi ảnh gõ kèm ở
  cuối mẫu tự bỏ (trước đó ra `….png.png`). Console 18/18. Người dùng thử tay Vùng chọn trên Teams →
  `2026-10-07_14-51-14_ms-teams.png` OK
- [x] Bug (người dùng báo): xoá ảnh tự lưu ngoài app rồi Ctrl+W → không hỏi lưu (tab bị coi là đã lưu) →
  `NeedsSave` tính cả file đã lưu không còn trên ổ; hộp thoại ghi rõ "File đã lưu không còn trên ổ…", Lưu ghi lại đúng
  chỗ cũ (tạo lại thư mục ngày). Đóng tất cả cũng tính tab này là chưa lưu. GUI Sandbox `sct_missing` 5/5 (2026-10-07)
- [x] Người dùng thử tay máy 2 màn hình khác DPI (chính 1920×1200 @150%, phụ 1920×1080 @100% bên phải): Màn hình hiện
  tại ra đúng kích thước từng màn; `{app}` EXCEL / CalculatorApp / Code đúng. Bug: chụp màn có chuột (VS Code) mà cửa
  sổ active (Explorer) ở màn kia → tên `…_explorer.png` → Màn hình hiện tại chỉ lấy cửa sổ active nếu tâm nó nằm trên màn
  được chụp, không thì cửa sổ dưới con trỏ. Hồi quy Sandbox `sct_3a` 10/10 + `sct_missing` 5/5 (2026-10-07) — cần thử
  lại tay trường hợp Explorer ở màn kia
- [x] Thử tay lại (bản sau 15:48): Màn hình hiện tại khi cửa sổ active ở màn kia → `{app}` = app dưới con trỏ — người
  dùng xác nhận OK (2026-10-07)
- [x] ★ Tuỳ chọn chụp kèm con trỏ chuột — Cài đặt > Chung > *Chụp kèm con trỏ chuột* (mặc định tắt): vẽ con trỏ đúng
  hình + vị trí (trừ hotspot) bằng DrawIconEx sau BitBlt, ép alpha 255; áp cho Toàn màn hình / Màn hình hiện tại / Cửa
  sổ hiện tại / Chụp lại lần trước, không áp cho vùng chọn / chụp cuộn. GUI Sandbox `sct_cursor` 5/5: tắt → không có
  con trỏ; bật → mũi tên đúng chỗ, chỗ khác sạch, không lỗ trong suốt; cả Toàn màn hình. F1 + README (2026-10-07)
- [x] ★ Chọn việc tự làm sau khi chụp — Cài đặt > Chung > nhóm **Sau khi chụp**: Mở trong Editor (tắt được), Copy, Tự
  lưu (đồng bộ với ô ở trang Lưu ảnh), Hiện thông báo nhỏ (`CaptureToastWindow`: góc dưới-phải màn có con trỏ, ảnh thu
  nhỏ, Mở trong Editor / Mở thư mục, không giành focus, tự ẩn 6 giây, tắt hiệu ứng mờ dần, đóng trước lần chụp sau);
  bỏ hết 4 việc → không cho OK; lỗi lưu / copy khi không mở Editor → luôn hiện thông báo. Tắt Editor: tự lưu ảnh gốc
  thẳng ra file (`SavePngToFolder` tự tạo thư mục con). GUI Sandbox `sct_after` 18/18. F1 + README (2026-10-07) — cần
  thử tay
- [ ] Sau khi chụp: *Mở bằng app khác* / *In* (In đi cùng ★★ In ảnh)

*Lượt 3b — trung bình:*
- [ ] ★★ Hút màu trên màn hình (mã #RRGGBB) + bảng màu nhớ các màu đã hút
- [ ] ★★ In ảnh
- [ ] ★ Lưu PDF (GIF tuỳ nhu cầu); gửi email / Office / mở bằng chương trình khác

*Lượt 3c — khó hơn:*
- [ ] ★★ Chụp tự do (lasso - vẽ đường viền bất kỳ, phần ngoài thành trong suốt)
- [ ] ★★ Trỏ-chọn cửa sổ con (nút, ô, khung nội dung) — đã có mục riêng ở trên
- [ ] ★ Kính lúp, thước đo pixel, đường chữ thập, thước đo góc, bảng trắng vẽ lên màn hình

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
- [x] Test GUI *Soi 1 vùng* trong Sandbox (bản Release): chọn mục căn → nhắc kéo chuột, kéo khoanh → "Soi vùng 530 × 170
  tại (10, 161)" đúng chỗ kéo, bấm không kéo giữ vùng cũ, khoanh lại thay vùng, Lưu PNG + Xuất báo cáo HTML (có dòng
  "Soi vùng …"). `ic_focus` 9/9 (2026-10-08)
- [x] Soi 1 vùng / Tự căn khớp nhầm dòng ở **bảng nhiều dòng giống nhau** (ảnh thử: 10 dòng "Item i / value i", B mỗi
  dòng cao hơn 4px): Tự căn ra (0, 138); khoanh dòng 3–6 ra lệch y 30…42 thay vì −12…−24 (lệch đúng 1 dòng = 54px) →
  báo đỏ cả các chữ số. Nguyên nhân: ở bảng như vậy mọi cách ghép dòng đều sai lệch gần ngang nhau (~7 mức sáng), và
  chính dòng khác thật ("value 5 CHANGED") làm cách ghép đúng bị điểm xấu hơn. Sửa: Tự căn - nhiều đỉnh tương quan gần
  bằng nhau thì lấy độ dịch nhỏ nhất, giữ (0, 0) khi chỉ kém ≤ 3% (trước: +0.05 tuyệt đối); Soi 1 vùng - chấm điểm cả
  vùng theo 8 dải ngang, bỏ ¼ dải khớp tệ nhất. Unit test 100/100 (+8 `RepeatedRowsTests`: ảnh Skia + ảnh System.Drawing
  y như Sandbox trong `TestData\`), ảnh thật IE ↔ Edge trước / sau giống hệt (Tự căn (2, 2), 3 vùng soi không đổi);
  Sandbox `ic_focus` 12/12: Tự căn (0, 0), dòng 3–6 lệch y −24…−12, chỉ còn chữ CHANGED bị tô. README + F1 (2026-10-08)
- [x] Soi 1 vùng: sau mỗi lần khoanh, khung xem tự co giãn lại (78,5% → 81,6%) vì khung nội dung = A ∪ B đặt theo độ
  lệch ở tâm vùng soi (đổi theo vùng) → ảnh nhảy nhẹ dưới con trỏ ngay sau khi thả chuột. Sửa: kết quả mới cùng 2 ảnh và
  vẫn Soi 1 vùng thì giữ zoom / vị trí (đổi ảnh / cách căn vẫn vừa cửa sổ lại như cũ). Sandbox `ic_focus` 13/13: 2 lần
  khoanh, zoom 83,1% + gốc ảnh (217, 214) không đổi. README + F1 (2026-10-08)
- [x] Ảnh IE mode ↔ Edge chồng mờ trông lệch 2px dù DOM trùng (người dùng hỏi, 2026-10-07): đo trên 2 ảnh → thanh tiêu đề
  trùng (0, 0), nội dung trang lệch đều (2, 2) = viền 2px quanh trang của IE mode; riêng cột phải (受注金額, 厚物,
  リサイクル) còn lệch thật thêm ~4–5px ngang. Thêm ghi chú ℹ trong khung tóm tắt khi Tự căn ra lệch ≤ 4px + dòng F1.
  GUI Sandbox `ic_shift` 3/3: 2 ảnh mẫu → Tự căn (2, 2) + có ℹ; ảnh giống hệt → không có ℹ; Chồng mờ: khối trái khít
  (ảnh mẫu trong Sandbox `C:\sbx\files\A_IE-mode.png`, `B_Edge.png`)
- [x] So chữ báo nhầm chữ xám ô bị khoá (IE mode ↔ Edge: 用途区分 "他", FSC認証製品, 検査Ｓ１ - chữ y hệt, chỉ khác viền ô;
  OCR đọc mỗi phía 1 kiểu kể cả khi đọc lại) (người dùng hỏi, 2026-10-07) → tuỳ chọn **So nét chữ** (bật sẵn): mục 2 phía
  mà nét chữ trùng khít từng chữ (`TextDiffVerifier.SameShapeStrict`: tổng lệch ≤ 20%, mỗi khung ~1 chữ ≤ 35%) được đánh
  dấu `SameGlyphs` và ẩn; bỏ tick = như trước, hiện với dấu ≡; bật / tắt không đọc lại. Ảnh thật (Windows OCR, 2 lần):
  ẩn FSC認証製品 / 検査Ｓ１ / ページ数, giữ 7 chỗ khác thật (icon tiêu đề: khung lệch 200%). Unit test 92/92 (thêm "１→２"
  không ẩn, 1 chữ số đổi trong dòng dài vẫn báo); GUI Sandbox `ic_glyphs` 4/4 (Tesseract: 17 ↔ 29 chỗ);
  GUI máy thật (Windows OCR) 4/4: bật 6 chỗ (đều khác thật) ↔ tắt 8 (≡ FSC認証製品, 検査Ｓ１). README + F1 cập nhật
- [x] So nét chữ chưa áp cho mục chỉ 1 phía (vd "chỉ B: 他" khi OCR bỏ sót 1 phía - đã có `SameShape` thường, ngưỡng lỏng)
  → dò lại cặp IE ↔ Edge: Windows OCR không còn mục 1 phía nào là chữ (4 mục đều thanh địa chỉ - khác thật); Tesseract
  còn `厚物・薄物共通` (chỉ B, chữ y hệt): `SameShape` nới vùng dự đoán 3 px dính **vạch viền ô** sát trái ở A → khung nét
  lệch cỡ. Sửa: mục 1 phía mà OCR + `SameShape` không chốt được thì `SameShapeNear` dò khung cùng cỡ ±4 px quanh chỗ dự
  đoán (chỉ nới 1 px): trùng theo ngưỡng chặt + cùng màu → `SameGlyphs` (ẩn khi bật So nét chữ, tắt hiện ≡); trùng hình
  mà khác màu → khác màu chữ. Unit test 103/103 (+3: cùng chữ / khác chữ / khác màu cạnh vạch viền); cặp thật Windows OCR
  không đổi, Tesseract thêm 3 mục ≡ (`厚物・薄物共通`, `6` = chữ "C" xám ô combo, mũi tên combo #6D6D6D ↔ #949799 -
  dưới ngưỡng khác màu, như các mũi tên 2 phía vốn đã ≡). Sandbox `ic_glyphs` 4/4: 17 → 14 chỗ (tắt vẫn 29). README +
  F1 (2026-10-08)
- [x] Thanh tuỳ chọn So chữ ở cửa sổ hẹp (Sandbox 150%) bị tràn: nhãn "So nét chữ" bị cắt, phải cuộn ngang → tuỳ chọn
  của chế độ không đủ chỗ cạnh các tab thì tự xuống 1 dòng riêng dưới tab (`UpdateOptionsPlacement`, so bề rộng nội dung
  với chỗ cạnh tab - không bập bênh), đủ chỗ thì về lại; hẹp nữa vẫn cuộn ngang như cũ. Áp cho mọi chế độ. Sandbox
  `ic_legend` 6/6 (150% phóng to: So chữ + Khác biệt xuống dòng, hiện đủ, không cuộn; Thanh trượt về cạnh tab; cửa sổ
  800 px vẫn cuộn ngang; phóng to lại hiện đủ). README (2026-10-08)
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
