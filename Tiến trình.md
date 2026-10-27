# Tiến trình AppSuite

Danh sách việc cần làm / đã làm của toàn bộ AppSuite. Có vấn đề mới thì thêm 1 dòng `- [ ]` vào đúng
mục; làm xong thì đổi thành `- [x]` và ghi ngày hoàn thành. Chi tiết thiết kế của từng module vẫn nằm
trong `PLAN.md` / `README.md` của module đó — file này chỉ để theo dõi tiến độ.

Cập nhật lần cuối: 2026-10-12

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

**Ưu tiên 2 — tính năng nên có, giải quyết vấn đề thật:** ✅ xong cả 3 (test GUI 2026-10-08)
1. ~~ImageCompare: so ảnh lệch bố cục dần~~ — làm phương án B *Soi 1 vùng* 2026-10-05, test GUI Sandbox xong 2026-10-08
2. ~~ScreenCapture: xoay 90° / lật / đổi cỡ ảnh theo % hoặc px~~ — code xong 2026-10-05, test GUI Sandbox xong 2026-10-08
3. ~~Hướng dẫn F1 cho 4 module HtmlGenerator, ModuleB/C~~ — xong 2026-10-05, test GUI Sandbox xong 2026-10-08

**Ưu tiên 3 — có thì tốt, làm khi có nhu cầu:**
- ImageCompare: so sánh hàng loạt 2 thư mục ảnh
- ScreenCapture: còn thiếu so với PicPick, chia 3 đợt (rà 2026-10-05, chi tiết ở mục ScreenCapture): ~~**1. chữ & hình
  vẽ**~~ · ~~**2. hiệu ứng ảnh**~~ — cả 2 xong + test GUI Sandbox 2026-10-06, còn người dùng thử tay ·
  **3. chụp & công cụ** — đề xuất làm 3 lượt (2026-10-06): ~~(a) 1 màn hình, mẫu tên file, kèm con trỏ, việc tự làm sau
  khi chụp~~ (xong 2026-10-07) · (b) hút màu + bảng màu, in, lưu PDF · (c) lasso, cửa sổ con, kính lúp / thước…
- CsvEditor: freeze cột đầu, ẩn / hiện cột, tự giãn độ rộng cột
- Chung: nhớ kích thước / vị trí cửa sổ + file gần đây cho mọi module (helper chung trong `Common`); nhớ thư mục nguồn
  ModuleB / ModuleC / Mcf.DbDef; ~~chuyển Hướng dẫn của ScreenCapture / FileTools sang bản dùng chung~~ (2026-10-08)
- Kỹ thuật: giảm tiếp dung lượng publish (runtime chung), unit test còn thiếu (Rdbms.HtmlGenerator, ModuleB/C, ScreenCapture),
  `codegraph upgrade`

## Chung (repo, build, deploy)

- [x] **Rà mọi hộp thoại bị cắt** (2026-10-22, sau lỗi Cài đặt Rdbms): ContentDialog của WinUI tắt cuộn dọc → nội dung dài hơn
  cửa sổ bị cắt, không có thanh cuộn. Bọc ScrollViewer cho 12 hộp thoại XAML (CsvEditor: Encoding, Lọc, Tìm / thay, Sắp xếp,
  Thống kê; ModuleB: Danh sách file khớp, Cài đặt; ModuleC: Tìm tên bảng; ScreenCapture: Ảnh mới, Đổi cỡ; Rdbms: Thiết lập
  kết nối - bỏ vùng cuộn 360 px trong từng tab để chỉ còn 1 thanh cuộn, So sánh) và hộp thoại tạo trong code có nội dung có
  thể dài (thông báo lỗi `ex.Message`, danh sách file - CsvEditor, FileTools, ModuleB, ScreenCapture, Rdbms, MeRon,
  MainLauncher) qua `SharedUI.Helpers.DialogContent.Scrollable`. Quy ước ghi ở SharedUI\README. Build solution 0 lỗi; unit test
  CsvEditor 120, FileTools 132, MeRon 139, Rdbms 57, ScreenCapture 59 đều đạt; GUI Sandbox (cửa sổ ~600 px): Thiết lập kết nối,
  Cài đặt Rdbms cuộn tới ô cuối, hộp thoại lỗi đọc DB đủ chữ, Ảnh mới vừa khung
- [ ] Thử tay các hộp thoại CsvEditor (Thống kê / Lọc / Sắp xếp với file nhiều cột), ModuleB, ModuleC ở cửa sổ thấp - chưa mở
  được tự động trong Sandbox (cần hộp thoại chọn file / dữ liệu)
- [x] **Vá bảo mật package kéo theo** (2026-10-23): `Directory.Packages.props` bật `CentralPackageTransitivePinningEnabled` +
  ghim SQLitePCLRaw 2.1.6 → 2.1.14 (SQLite bên trong có lỗ hổng High, GHSA-2m69-gcr7-jv3q - MeRon, Rdbms, Mcf.*, ModuleB),
  System.Drawing.Common 4.7.0 → 4.7.3 (Critical), System.Text.RegularExpressions 4.3.0 → 4.3.1, System.Security.Cryptography.Pkcs
  → 8.0.2 (EPPlus / driver database kéo theo). Kéo theo: Microsoft.Extensions.Configuration* của 4 module dùng EPPlus 3.1 → 8.0.
  `dotnet list package --vulnerable --include-transitive` sạch cả 25 project. Build 0 lỗi; unit test MeRon 137, Mcf.DbDef 11,
  Mcf.Screen 29, Mcf.CrudDiagram 21, Rdbms 57, CsvEditor 120, FileTools 131, ImageCompare 106, ScreenCapture 59 đạt; Sandbox
  (Release, framework-dependent): MeRon bản cũ tạo database → MeRon bản mới mở đúng file đó; 6 module dùng SQLite mở cửa sổ,
  không lỗi Event Log. App điện thoại me-ron không ảnh hưởng (Flutter sqflite, định dạng file SQLite không đổi)
- [ ] **Lên .NET 10** trước 2026-11-10 (.NET 8 và .NET 9 cùng hết hỗ trợ ngày này) - cần Visual Studio 2026 (VS 2022 17.14 không
  mở được dự án net10.0); chờ người dùng cài VS 2026 Community

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
- [ ] Unit test còn thiếu: Rdbms.HtmlGenerator (cần PostgreSQL/Oracle thật hoặc tách phần dựng HTML), ModuleB/C
- [x] Unit test ScreenCapture: `Tests\ScreenCapture.Tests` 38 test - tên tab gọn, mẫu tên file, Undo / Redo + cắt ảnh,
  xoay / lật / co giãn + về hướng ban đầu, hiệu ứng, phiên tạm (thư mục tạm riêng, không đụng phiên thật). Thêm vào
  AppSuite.sln (2026-10-16)
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

## MeRon (module mới — bản máy tính của app điện thoại me-ron, `Modules/MeRon/PLAN.md`)

Quyết định 2026-10-12: module AppSuite (không build Flutter cho Windows), import TSV = thay toàn bộ, làm 4 đợt.

- [x] Đợt 1: khung module (sln / modules.json / build scripts / icon), SQLite cùng schema điện thoại, **Nhập dữ liệu**
  (thư mục backup hoặc thư mục cha → bản mới nhất, kiểm tra cả 8 file trước, thay toàn bộ trong 1 transaction, bản sao
  an toàn `Backups\`), **Khách hàng / Món ăn / Danh mục món ăn / Địa chỉ**, Hướng dẫn F1; unit test 19/19, backup thật
  nạp không lỗi; GUI Sandbox 36/36 (sửa: số hiện `2,617` theo ngôn ngữ Windows → luôn `vi-VN`; ô tìm che số mục khi
  cửa sổ hẹp; nhãn Địa chỉ lệch cỡ chữ; thư mục không được nhớ khi bấm Kiểm tra lại) (2026-10-12)
- [ ] Người dùng thử tay Đợt 1
- [x] Đợt 2: **Đặt hàng** (lọc trạng thái / khách / khoảng ngày + chọn nhanh, sắp xếp, thẻ đơn), **Thêm / sửa đơn** (món
  thường + tự do, khoá dòng đổi giá, giảm giá, đã thanh toán trước, cảnh báo trùng đơn, huỷ / xoá), **Chi tiết** (gộp theo
  khách, Thanh toán toàn bộ, Chụp ảnh → clipboard + PNG); sửa có chủ đích 4 lỗi của điện thoại (lọc ngày bỏ sót, xoá đơn
  để lại dòng món, thanh toán cả đơn huỷ, lưu lại đơn huỷ bị bỏ huỷ); unit test 32/32; GUI Sandbox 36/36 + Đợt 1 37/37
  (2026-10-12)
- [ ] Người dùng thử tay Đợt 2
- [x] Đợt 3: **Mua hàng** (phiếu nhập: lọc ngày, thêm / sửa / xoá, kiểm tra từng ô) + 4 **báo cáo** (Theo đơn hàng, Theo
  món ăn, Tổng hợp + chênh lệch bán − mua, Khách hàng tiềm năng) bằng truy vấn SQL tổng hợp (36 ms trên dữ liệu thật),
  bấm khách → Chi tiết đơn; khoảng ngày dùng chung + chọn nhanh; không tính đơn Huỷ ở mọi báo cáo (điện thoại lệch 2 chỗ);
  sửa lỗi tab trạng thái không reset khi vào lại trang (SelectorBar trong trang cache) cho cả Đặt hàng; lỗi form tự tắt
  khi sửa; unit test 38/38; GUI Sandbox 32/32 + Đợt 1 37/37 + Đợt 2 36/36 (2026-10-12)
- [ ] Người dùng thử tay Đợt 3
- [x] Đợt 4: **Xuất TSV** đúng định dạng điện thoại (xuất lại bộ backup thật giống từng byte file điện thoại; bảng trống =
  dòng tiêu đề, `\r` → dấu cách để điện thoại nhập được; ghi `.partial` rồi đổi tên), **Xoá toàn bộ dữ liệu** (xác nhận 2
  lần, bản sao `truoc-xoa` trước); rà soát tài liệu (README viết lại theo menu + quy trình dùng, PLAN, F1 16 mục, README
  gốc, modules.json); unit test 43/43; GUI Sandbox 15/15 + Đợt 1–3 chạy lại đạt (2026-10-12)
- [ ] Người dùng thử tay Đợt 4 + nhập thử bộ xuất từ máy tính trên điện thoại thật
- [x] Lấy backup thẳng từ link folder Google Drive công khai, không cần đăng nhập (`GoogleDriveBackupSource`: dán link
  vào ô thư mục → folder con mới nhất → tải 8 file về `%TEMP%\AppSuite\MeRon\GoogleDrive` → kiểm tra / nhập như cũ; nút
  Huỷ, nhớ link, báo lỗi rõ khi link sai / chưa chia sẻ / mất mạng); unit test 57/57 + tải thật từ folder Drive của
  điện thoại đạt; GUI Sandbox 17/17 (Drive giả `MERON_DRIVE_BASE_URL` vì Sandbox không ra Internet) (2026-10-13)
- [ ] Người dùng thử tay: dán link folder Drive → Kiểm tra lại → Nhập
- [x] Đăng nhập Google (OAuth + Drive API, Client ID loại Desktop trong project `me-ron`) để chuyển folder sang "Hạn chế":
  trang Nhập có khung "Hoặc lấy từ Google Drive (đăng nhập)" - liệt kê các bản backup app điện thoại / MeRon đã tải lên (mới
  nhất trước, nơi lưu, đủ bảng chưa), bấm 1 bản → tải về + kiểm tra như thư mục; folder không cần công khai
  (`Core\Cloud\DriveBackupBrowser.cs`, `Core\Import\DriveApiBackupSource.cs`). Unit test 110/110 (2026-10-15)
- [x] Thử với Drive thật (người dùng, 2026-10-15): đăng nhập chỉ thấy 1 bản (20261014_164527) - folder chung có bản của nhiều
  tài khoản (phanphuong…, Saku Ra…), quyền drive.file chỉ thấy file app tạo bằng chính tài khoản đang đăng nhập
- [x] Thư mục cha / link folder Drive: liệt kê HẾT các bản (mới nhất trước, mọi tài khoản) dưới ô thư mục, bấm để chọn bản
  khác bản mới nhất; nhãn "Đang xem". Unit test 111/111; GUI Sandbox trang Nhập 13/13 (2026-10-15)
- [x] Dán link folder Google Drive vào ô thư mục là tự đọc (không cần Enter; chỉ khi ô có con trỏ - mở trang không tự tải lại).
  Người dùng dán link mà không thấy danh sách vì app chưa đọc (chưa Enter). Folder điện thoại công khai đọc được đủ 39 bản.
  GUI Sandbox: dán link 4/4, phím Enter 3/3, nhập qua link 17/17 (2026-10-15)
- [ ] Đăng nhập thấy được bản của các tài khoản khác: cần quyền đọc cả Drive (drive.readonly, loại "restricted") - chờ người
  dùng quyết; chưa có thì giữ folder điện thoại công khai và dùng link
- [x] Rê chuột vào vùng trống hay hiện tooltip "F1": phím F1 gắn ở NavigationView → `KeyboardAcceleratorPlacementMode="Hidden"`;
  GUI Sandbox 4/4 (bản cũ tái hiện lỗi ở 2/3 điểm rê chuột, F1 vẫn mở Hướng dẫn) (2026-10-13)
- [x] Form đơn: ô chọn món đổi ComboBox → ô gõ lọc kiểu autocomplete (không dấu, các từ không cần liền nhau "bao xiu",
  tên bắt đầu bằng chữ gõ lên trước, Enter chọn món đầu), bắt buộc chọn 1 món trong danh sách (gõ món không có → lỗi đỏ
  dưới dòng, Lưu báo + đưa con trỏ về ô); Thêm món = dòng trống + con trỏ vào ô món (trước: mặc định món đầu); món tự
  do giữ nguyên gõ tay; unit test 58/58; GUI Sandbox 22/22 (chạy 4 lần) + Đợt 2 36/36 (2026-10-13)
- [ ] Người dùng thử tay ô chọn món mới
- [x] Giữ điều kiện tìm kiếm khi chuyển trang bằng menu (vd Đặt hàng → Mua hàng → Đặt hàng): trước đây vào lại từ menu
  là về mặc định; nay mặc định chỉ ở lần đầu mở trong phiên, tắt app mở lại mới về mặc định. Áp dụng: Đặt hàng, Mua
  hàng, 4 báo cáo, ô tìm ở Khách hàng / Món ăn / Danh mục / Địa chỉ (cache trang); Theo món ăn giữ món đang lọc khi nạp
  lại danh sách món (2026-10-13)
- [x] Sửa lỗi "Không đọc được đơn hàng: Unspecified error" sau khi vào Chi tiết rồi quay lại và đổi tab / lưu đơn (lúc
  có lúc không): ViewModel dùng chung cả phiên nhưng trang Đặt hàng / Theo đơn hàng không cache → trang cũ đã gỡ vẫn
  nghe danh sách đơn, ném COMException khi danh sách đổi → cache 2 trang này (1 trang duy nhất), Frame CacheSize 20
  (2026-10-13)
- [x] Sửa lỗi do cách sửa trên: cache trang Đặt hàng / Theo đơn hàng làm SelectorBar tự áp lại tab cũ (lúc có lúc không) →
  quay lại từ Chi tiết / form thì bộ lọc trạng thái tự về "Tất cả". Bỏ cache 2 trang này; chống COMException tận gốc: rời
  trang thì gỡ x:Bind (Bindings.StopTracking) + ItemsSource danh sách. Bộ lọc vẫn giữ (ViewModel cả phiên). Unit test 58/58;
  GUI Sandbox Đặt hàng 36/36 (chạy 3 lần), giữ bộ lọc 16/16, báo cáo 32/32 (2026-10-13)
- [x] Sao chép đơn (khách quen đặt lặp lại): chuột phải thẻ đơn (Đặt hàng / Chi tiết / Theo món ăn, cả Shift+F10) hoặc
  nút "Sao chép đơn" trong form sửa → form Thêm đặt hàng điền sẵn khách, món + số lượng, món tự do, giảm giá, ghi chú;
  giao hôm nay (giữ giờ giao gốc), Đã lên đơn, chưa thanh toán, giá hiện tại (báo ở đầu form khi giá đổi; món không còn →
  món tự do); chưa lưu đến khi bấm Lưu, Lưu về thẳng danh sách. Unit test 61/61; GUI Sandbox `meron_copy.ps1` 21/21 +
  Đặt hàng 36/36 (2026-10-13)
- [x] Sao chép đơn: giữ nguyên ngày giờ giao của đơn gốc (trước: giao hôm nay); đơn gốc không có ngày giao → hôm nay
  (2026-10-14)
- [x] Sửa lỗi ô chọn khách (lọc khách ở Đặt hàng / Theo đơn hàng, form đơn): gõ tên rồi bấm chuột vào gợi ý thì tên lên ô
  nhưng danh sách không đóng, phải bấm lần 2 → đóng danh sách + bỏ gợi ý cũ sau khi chọn. GUI Sandbox `meron_suggest.ps1`
  (gõ phím thật + bấm chuột) 11/11; Đặt hàng 36/36, Sao chép 19/19, giữ bộ lọc 16/16, báo cáo 32/32 (2026-10-14)
- [x] Sửa lỗi rê chuột vào dòng món trong form đơn hiện 1 tooltip trống (tooltip giải thích dòng bị khoá trả chuỗi rỗng
  với dòng thường → nay không có tooltip; dòng khoá vẫn giải thích). GUI Sandbox `meron_tooltip.ps1` 3/3 (2026-10-14)
- [x] Form đơn + form phiếu nhập: Tổng (và Thành tiền, Tổng số lượng) đổi ngay lúc đang gõ Số lượng / Giảm giá / Đơn giá món
  tự do / Giá tiền (trước: chỉ đổi khi rời ô); số tiền canh phải, số lượng canh giữa; ô món tự do thẳng hàng với ô món, ô
  tên món tự do / tên hàng / Ghi chú cùng cỡ chữ với các ô khác. GUI Sandbox `meron_live.ps1` 7/7 (chạy 3 lần); Đặt hàng
  36/36, Sao chép 19/19, ô chọn khách 11/11, tooltip 4/4, giữ bộ lọc 16/16, báo cáo 32/32 (2026-10-14)
- [x] Phím tắt (mục "Phím tắt" mới trong F1 + README): Ctrl+F ô tìm / lọc khách; Ctrl+N thêm ở mọi trang danh sách (thêm
  Khách hàng, Món ăn, Danh mục, Địa chỉ); Ctrl+1…4 tab trạng thái (Đặt hàng, Theo đơn hàng); Ctrl+D khoảng ngày nhanh (Đặt
  hàng, Mua hàng, 4 trang Thống kê); form: Ctrl+Shift+N lưu + mở đơn mới cùng ngày giao, Ctrl+M thêm món / thêm hàng, ↑ / ↓
  trong ô Số lượng, Ctrl+Shift+C sao chép đơn, Esc quay lại (đang mở gợi ý / lịch / menu thì Esc đóng cái đó trước). Không
  hiện tooltip phím tắt, bỏ qua khi đang mở hộp thoại. GUI Sandbox `meron_keys.ps1` (phím thật) 35/35; live 7/7, Đặt
  hàng 36/36, Sao chép 19/19, ô chọn khách 11/11, tooltip 4/4, giữ bộ lọc 16/16, báo cáo 32/32 (2026-10-14)
- [x] Ô ngày mới `DateBox` thay CalendarDatePicker (Từ / Đến ngày mọi trang, Ngày giao, Ngày thanh toán, Ngày nhập): gõ thẳng
  dd/MM/yyyy + gõ tắt (14/10, 14, 141026), gõ sai trả lại ngày cũ, ↑ / ↓ ±1 ngày, Alt+↓ / F4 mở lịch; lịch tiếng Việt (Tháng
  Mười 2026, T2…CN, tuần từ thứ Hai) + nút Hôm nay + Xoá ngày (ô không bắt buộc). Unit test 86/86 (thêm 25 DateInput); GUI
  Sandbox `meron_date.ps1` 18/18; phím tắt 35/35, live 7/7, Đặt hàng 36/36, Sao chép 19/19, ô chọn khách 11/11, tooltip
  4/4, giữ bộ lọc 16/16, báo cáo 32/32 (2026-10-14)
- [x] Trang mới "Khách lâu không đặt" (Thống kê): khách đã đặt ≥ 2 đơn mà đơn gần nhất (ngày giao, bỏ đơn Huỷ) cách hôm nay
  ≥ 30 ngày (chọn nhanh 14 / 30 / 60 / 90, gõ số); lần cuối, thói quen (TB ngày giữa các lần đặt, cần ≥ 3 ngày đặt; nhãn cam
  khi nghỉ ≥ 2 lần thói quen), số đơn + tiền, 3 món hay đặt; sắp xếp, tìm (Ctrl+F); nút Xem đơn, Đặt lại (chép đơn gần nhất,
  giao hôm nay), Tin nhắn (chép câu hỏi thăm cho Zalo), chép SĐT. Unit test 92/92; GUI Sandbox `meron_lapsed.ps1` 12/12 (dữ
  liệu thật: 46 khách); các bộ khác đạt hết (2026-10-14)
- [ ] Để sau: Khách lâu không đặt - ghi "đã liên hệ" (lưu riêng, không mất khi nhập backup); Chụp ảnh danh sách
- [x] Đăng nhập Google + Xuất lên Google Drive (trang Xuất / Xoá dữ liệu): OAuth Desktop app + PKCE, loopback 127.0.0.1,
  quyền drive.file, refresh token trong Windows Credential Manager; xuất TSV rồi tải lên thư mục yyyyMMdd_HHmmss trong folder
  đã chọn hoặc folder riêng "MeRon backup". File client_secret_….json không commit (.gitignore). Unit test 106/106 (14 mới,
  có đăng nhập giả lập qua cổng loopback); GUI Sandbox `meron_google.ps1` 9/9 (không mạng: tới bước chờ trình duyệt + huỷ),
  export 15/15, các bộ khác đạt (2026-10-14)
- [x] Sửa lỗi "Google Drive báo lỗi (HTTP 403): Request had insufficient authentication scopes": trang đồng ý của Google cho
  bỏ tick ô quyền Drive → đăng nhập kiểm tra quyền được cấp, thiếu thì thu hồi + báo cách tick; phiên cũ thiếu quyền khi upload
  thì tự đăng xuất + báo đăng nhập lại. Trang trình duyệt chỉ báo sau khi đã đổi mã + kiểm tra quyền (trước: báo "Đã đăng
  nhập" dù app từ chối); log ghi các quyền Google cấp. Unit test 107/107; GUI Sandbox Google 9/9 (2026-10-14)
- [ ] Người dùng thử tay: Đăng nhập Google thật, Xuất lên Google Drive (để trống ô Folder, rồi thử dán link folder điện
  thoại), Đăng xuất / mở lại app vẫn nhớ đăng nhập
- [ ] Người dùng thử tay Sao chép đơn
- [x] Giao diện mới (mẫu thử trong Sandbox, người dùng chọn): màu chủ đạo cam `#E8590C` thay màu nhấn Windows; thẻ đơn bo
  góc + vạch màu trạng thái bên trái + nhãn tròn, Đã thanh toán nền xanh nhạt (mức "xanh đậm vừa"), Đã lên đơn nền trắng, Huỷ
  xám mờ; Đặt hàng: tiêu đề to + nhãn "N đơn · Tổng X"; Theo đơn hàng: 4 ô số liệu có icon (thêm TB mỗi đơn), ô trạng thái
  có thanh % số đơn, bảng món có thanh so với món bán nhiều nhất. Cập nhật F1, README. Unit test 107/107 (2026-10-15)
- [x] Giao diện mới đợt 2 (mẫu thử trong Sandbox, người dùng duyệt): Mua hàng (thẻ phiếu vạch xanh dương = chi, nhãn "N phiếu ·
  Tổng X" xanh dương, trống thì chữ xám); Theo món ăn (3 ô: Tổng số món / Tổng thành tiền / Số món khác nhau, bảng có thanh);
  Tổng hợp (3 ô Bán cam / Mua xanh dương / Chênh lệch xanh lá khi lãi - đỏ khi lỗ, 2 bảng có thanh); Khách hàng (ô tròn chữ
  cái đầu bỏ qua số phòng, địa chỉ thành nhãn), Món ăn (giá cam, danh mục nhãn xanh dương), Danh mục món ăn, Địa chỉ (mã nhãn
  cam) - bảng trong khung bo góc, số mục nhãn tròn. Cửa sổ F1 giữ nguyên (người dùng chọn). Cập nhật F1, README. Ảnh trước /
  sau ở `Pictures\MeRon giao dien mau 2026-10-15\5_dot2_cac_trang_con_lai` (2026-10-15)
- [x] Giao diện mới đợt 3 (mẫu thử trong Sandbox, người dùng duyệt): Khách hàng tiềm năng (hạng 1–3 vàng / bạc / đồng, ô tròn
  chữ cái đầu, thanh tỷ lệ tiền); Khách lâu không đặt (ô số ngày to - cam khi nghỉ quá thói quen); Nhập / Xuất (khung có
  icon, Xoá toàn bộ khung đỏ, bảng xem trước bo góc); form đơn 2 khung + nhãn trạng thái tròn; form phiếu nhập 1 khung, Tổng
  xanh dương. Ảnh trước / sau ở `Pictures\MeRon giao dien mau 2026-10-15\6_dot3_khach_nhap_xuat_form`. Unit test 112/112; GUI Sandbox 18 bộ
  đạt hết (Đặt hàng 36/36, báo cáo 33/33, Khách lâu không đặt 12/12, Sao chép 19/19, cảnh báo chưa xuất 12/12, tổng quát 37/37...) (2026-10-15)
- [x] Cảnh báo trước khi nhập đè mất phần sửa trên máy tính: trigger SQLite trên 8 bảng đếm dòng thêm / sửa / xoá (tắt trong
  lúc nhập); trang Nhập hiện khung vàng "Có thay đổi trên máy tính chưa xuất" (số dòng, từ lúc nào) + nút Mở trang Xuất; hộp
  xác nhận đổi tiêu đề + nút "Vẫn nhập, bỏ thay đổi" (chọn sẵn Huỷ); xuất TSV / Drive, nhập, xoá toàn bộ → về 0. Database
  cũ bắt đầu đếm từ lần mở app đầu tiên sau khi cập nhật. Unit test 112/112; GUI Sandbox cảnh báo 12/12, xuất 15/15, báo
  cáo 33/33, Đặt hàng 36/36, tổng quát 37/37, trang Nhập 13/13 (2026-10-15)
- [ ] Thêm đơn (nút Thêm, Ctrl+N, Ctrl+Shift+N, Sao chép đơn): mở form là con trỏ ở sẵn ô Khách hàng, ô trống thì hiện luôn
  danh sách khách để chọn; sửa đơn thì không. Cập nhật F1, README (2026-10-16)
- [ ] Form đơn: chọn món (bấm gợi ý / Enter) → con trỏ sang ô Số lượng cùng dòng; Thêm món tự do → con trỏ ở ô Tên, Enter
  → ô Đơn giá; vào ô số thì số cũ bôi đen sẵn để gõ đè. Cập nhật F1, README (2026-10-16)
- [ ] Trang Cài đặt (cuối menu): ẩn / hiện từng mục menu trái bằng ô tích theo nhóm, nhóm ẩn hết thì ẩn tiêu đề, Hiện tất
  cả; Đặt hàng luôn hiện; lưu settings.json cạnh database (nhập backup không mất). Cập nhật F1, README (2026-10-16)
- [x] Thẻ đơn: số Tổng nhỏ lại (17 → 14, bằng dòng món), giữ màu cam + đậm (2026-10-16)
- [ ] Trang **Thông tin dữ liệu** (nhóm Dữ liệu): tổng quan (file, số dòng 8 bảng, khoảng ngày, dữ liệu thừa), theo năm →
  bấm xem 12 tháng, **Dọn dữ liệu cũ** (mốc trước năm nay / 1 năm / 2 năm / ngày tự chọn, xem trước, tuỳ chọn xoá khách /
  món không còn dùng; tự xuất TSV "Truoc khi don" + bản sao .db trước khi xoá; nhắc xuất sang điện thoại). Unit test
  6 mới (122/122); GUI Sandbox với backup thật 27/27 (dọn trước 01/08/2026: 924 đơn + 30 khách), tổng quát 37/37, Cài
  đặt 21/21. Cập nhật F1, README (2026-10-16) - chờ thử tay
- [ ] Thông tin dữ liệu, đợt 2: **đơn chưa thanh toán luôn giữ** khi dọn (dòng "Giữ lại N đơn" + mở Chi tiết lọc sẵn; khách
  / món của chúng cũng giữ); **Khôi phục phần đã dọn** (don-dep.json trong bộ "Truoc khi don", chỉ thêm dòng còn thiếu,
  id bị chiếm → id mới, trùng tên → nối); "Truoc khi don" giữ hết, không tự xoá; khung **Đường dẫn liên quan** (file,
  Backups, Xuất, Truoc khi don, nhập gần nhất / link Drive, tài khoản + folder Drive, nút Mở). Unit test 125/125 (khôi
  phục: đủ, 2 lần không trùng, trùng tên → nối, id bị chiếm → id mới); GUI Sandbox backup thật: đợt 2 24/24 (trước
  01/08/2026: giữ 3 đơn chưa TT 65.000 đ, xoá 921 → khôi phục đủ 2.617 đơn / 268 khách), đợt 1 27/27. Cập nhật F1,
  README (2026-10-16) - chờ thử tay
- [x] Chi tiết: tên khách chỉ hiện 1 lần ở đầu khung, các thẻ đơn trong khung bỏ tên (ngày giao lên đầu thẻ) (2026-10-16)
- [ ] Trang **Biểu đồ** (Thống kê), chỉ dữ liệu đơn hàng: 4 ô số chính so với kỳ trước, doanh thu theo ngày / tuần / tháng
  (tự chọn), số đơn theo thứ, khách mới / quay lại (cột chồng + chú thích), 10 món bán chạy; rê chuột xem chi tiết, công
  tắc Bảng số liệu; mặc định 30 ngày qua. Màu đã chạy validate_palette (sáng + tối). Unit test 135/135 (10 mới); GUI
  Sandbox backup thật 11/11 (tổng đơn các tuần = 2.616), tổng quát 37/37, Cài đặt 21/21. Cập nhật F1, README
  (2026-10-16) - chờ thử tay, xem nền tối
- [x] Ô chọn khách (form đơn, lọc Đặt hàng / Theo đơn hàng) và ô chọn món: **Tab** = chọn gợi ý đầu như Enter (trước: Tab
  chỉ nhận khi gõ đủ tên, còn lại khách trả về cũ / món báo lỗi); **↑ / ↓** tô gợi ý rồi **Enter / Tab** chọn đúng gợi ý
  đang tô (khách trùng tên cũng đúng người). Ô món: Tab chọn xong con trỏ sang Số lượng, Shift+Tab chọn rồi lùi; gõ sai +
  Tab vẫn báo lỗi. GUI Sandbox gõ phím thật 24/25 (1 ca do test đọc thứ tự UIA khi danh sách mở lên trên - app chọn
  đúng). Cập nhật F1, README (2026-10-19)
- [x] Danh sách gợi ý (ô khách / ô món) mở **lên trên** (dòng món ở thấp, cửa sổ nhỏ): WinUI đảo thứ tự và có lúc cuộn
  khuất gợi ý đầu → nay luôn cuộn tới gợi ý đầu (nằm sát ô) - đúng cái Enter / Tab sẽ chọn (`SuggestionList.cs`) (2026-10-19)
- [x] Nhập đơn liên tục bằng bàn phím: chọn khách (Enter / Tab / bấm gợi ý) → con trỏ sang nút **Thêm món**; Enter / Space
  thêm dòng → gõ món, Tab / Enter → Số lượng → gõ số, **Enter / Tab → nút Thêm món** để thêm món tiếp (Shift+Tab lùi như
  thường). GUI Sandbox gõ phím thật 25/25 (4 dòng 3,2,4,5 + lưu được), bộ Tab / ↑↓ 24/25 (ca còn lại do test đọc thứ tự
  UIA khi danh sách mở lên trên). Cảnh báo trùng đơn bằng phím: Enter / Esc = Huỷ, Tab / ← → Vẫn lưu + Enter = lưu (GUI
  7/7). F1 thêm mục "Nhập đơn chỉ bằng bàn phím" + "Chọn trong danh sách gợi ý" (Phím tắt). Cập nhật F1, README (2026-10-19)
- [x] Form thêm đơn: 2 nút mới ở cuối trang + phím tắt - **Lưu và thêm mới** (Ctrl+Shift+N, có từ trước: đơn mới trống,
  cùng ngày giao) và **Lưu và chép cho khách khác** (Ctrl+Shift+K: đơn mới giống đơn vừa lưu - món, số lượng, giảm giá,
  ngày giao, ghi chú, đã thanh toán trước - bỏ trống khách, con trỏ ở ô Khách hàng). Form sửa không hiện 2 nút (phím vẫn
  dùng được). GUI Sandbox 18/19 (1 ca do dữ liệu test: gõ "thuy" ra lại đúng khách cũ). Cập nhật F1, README (2026-10-19)
- [x] Form thêm đơn: dòng **Lần trước** dưới ô Khách hàng - chọn khách xong hiện đơn gần nhất (theo ngày giao, không tính
  Huỷ): "Lần trước (12/10): 2 Bánh bao, 1 Chè · 34.000 đ" (tối đa 4 món, +N món); **Chép vào đơn / Ctrl+L**: chép món như
  Sao chép đơn (giá hiện tại, món không còn → món tự do, báo ở đầu form), giảm giá / ghi chú chỉ khi ô trống, đã có món thì
  hỏi Thay; xong con trỏ ở nút Thêm món. Form sửa không hiện. Unit test 137/137 (2 mới); GUI Sandbox 11/12 (1 ca do test
  không đọc ô món tự do - ảnh chụp xác nhận đủ 2 dòng, lưu xong Lần trước hiện đúng). Cập nhật F1, README (2026-10-19)
- [x] Form đơn - ô chọn món: bấm 1 lần vào gợi ý (vd "Cháo dưỡng nhan") mà danh sách không đóng, phải bấm lần 2 (người dùng
  báo 2026-10-23, có ảnh). Sửa như ô khách hôm 2026-10-14: `ProductSuggest.CloseList` - đóng thêm 1 lần sau 1 nhịp và bỏ
  gợi ý cũ (gõ tiếp thì nạp lại) cho cả bấm chuột, Enter và Tab. Sandbox CHƯA tái hiện được lỗi trên bản cũ (gõ phím ảo, gõ
  Unicode kiểu Unikey "Cháo", bấm gợi ý thứ 2, dòng 2 trùng món đều đóng ngay); bản mới đạt 13/13 + 11/11 (bấm chuột, Tab,
  gõ lại → gợi ý hiện lại). Unit test 137/137
- [ ] Thử tay: thêm đơn, gõ món như thường ngày rồi bấm 1 lần vào gợi ý → danh sách phải đóng ngay (nếu còn lỗi: ghi lại
  gõ chữ gì, có dùng Unikey / EVKey không, chuột hay touchpad)
- [x] **Công nợ** ⭐ (menu Bán hàng, 2026-10-23): đơn Đã lên đơn đã tới ngày giao gom theo khách - tổng nợ, số đơn, đơn cũ
  nhất bao nhiêu ngày (cam khi ≥ 30), dòng tổng số khách · đơn · tổng nợ; sắp xếp nợ nhiều / nợ lâu, tìm, tuỳ chọn tính cả đơn
  giao sau hôm nay. Mỗi khách: **Ảnh hoá đơn** (1 bấm → Chi tiết các đơn đang nợ, có tiêu đề, tự chụp ảnh vào clipboard +
  PNG), **Thanh toán hết** (hỏi lại, 1 lần ghi), tin nhắn nhắc thanh toán, chép SĐT. Cập nhật F1, README, MOBILE_TODO 33. Unit
  test 141/141 (4 mới); GUI Sandbox 18/18 trên bản sao database thật (113 khách / 330 đơn / 12.996.000 đ; ảnh hoá đơn 9 đơn đúng)
- [x] Thanh toán toàn bộ (Chi tiết) và Thanh toán hết (Công nợ) ghi thêm **ngày thanh toán = lúc bấm** (trước chỉ đổi trạng
  thái, ngày để trống - như điện thoại); đơn đã thanh toán từ trước giữ ngày cũ (người dùng chọn 2026-10-23). Unit test 141/141
- [x] Người dùng xem trang Công nợ (ảnh 2026-10-23): giao diện ổn; góp ý → nút **"Ảnh hoá đơn" đổi thành "Chi tiết"** (chỉ mở
  màn Chi tiết các đơn đang nợ, vẫn có dòng tiêu đề trong ảnh, không tự chụp - Chụp ảnh ở đó) vì tự chụp mỗi lần bấm sinh nhiều
  ảnh; **tự dọn ảnh chụp cũ**: Cài đặt → khung Ảnh chụp "Tự xoá ảnh cũ hơn N ngày" (mặc định 30, 0 = giữ hết, lưu
  settings.json), mỗi lần mở app đưa ảnh MeRon (chi_tiet_don_hang_… / hoa_don_cong_no_… trong Pictures\MeRon) cũ hơn N ngày vào
  Thùng rác, ảnh khác giữ nguyên; nút Dọn ngay, Mở thư mục ảnh, đếm số ảnh (`Core\CaptureFiles.cs`). Cập nhật F1, README,
  MOBILE_TODO. Unit test 145/145 (4 mới); GUI Sandbox 16/16
- [ ] Thử tay: gửi thử 1 ảnh (Công nợ → Chi tiết → Chụp ảnh) qua Zalo
- [x] Khoảng ngày Từ – Đến: đổi Từ ngày mà Từ > Đến → **Đến ngày = Từ ngày**; đổi Đến ngày mà Đến < Từ → **Từ ngày lùi
  theo = Đến ngày** (người dùng chọn) (Đặt hàng, Mua hàng, 4 báo cáo, Biểu đồ; ô trống thì không ép; chỉ tải lại 1 lần).
  Cập nhật F1, README. Unit test 146/146 (1 mới); GUI Sandbox 16/16 (gõ ngày + phím ↑ / ↓ thật, trang Đặt hàng + Mua hàng)
  (2026-10-27)
- [x] Ô **Giá bán** (Thêm / sửa món ăn) bỏ khung nút ▲▼ bật ra cạnh ô khi bấm vào (NumberBox Compact → Hidden, như mọi ô số
  khác - rà cả module chỉ còn chỗ này); ↑ / ↓ vẫn ±1.000. GUI Sandbox 5/5 (2026-10-27)
- [ ] Biểu đồ đợt sau (khi dùng Mua hàng): bán − mua theo tháng, lãi theo món (cần giá vốn)
- [ ] Để sau: Nhập dữ liệu khi thư mục chỉ có 1 vài file .tsv (hiện báo lỗi thiếu file, không nhập; thư mục cha chọn bản
  mới nhất dù thiếu file) → cân nhắc chế độ "chỉ thay các bảng có file" hoặc chỉ chọn bản đủ 8 file (2026-10-13)
- [ ] App điện thoại me-ron: 25 điểm (lỗi sai số liệu, toàn vẹn dữ liệu, nhập/xuất TSV, tìm kiếm, bảo mật Drive + chức năng
  bản máy tính đã có: Sao chép đơn / Đặt lại, ô chọn món gõ để lọc, Khách lâu không đặt, giữ bộ lọc, nhập cả bộ backup
  từ Google Drive một lần, cảnh báo thay đổi chưa xuất trước khi nhập, chọn bản backup bất kỳ, số liệu thêm ở Thống kê) ghi
  ở `Modules/MeRon/MOBILE_TODO.md` (2026-10-13, thêm mục F 2026-10-14, mục G + điểm 23–25 2026-10-15, điểm 26–32 2026-10-19: Lần trước + Chép vào đơn, Lưu và thêm mới / Lưu và chép cho khách khác, con trỏ đúng chỗ trong form đơn, tên khách 1 lần ở Chi tiết, ẩn / hiện menu, Thông tin dữ liệu + Dọn / Khôi phục, Biểu đồ)

## MainLauncher

- [x] Card module bị cắt mất hàng nút Start/Stop/Restart khi mô tả dài → card cao cố định, tên 1 dòng,
  mô tả tối đa 2 dòng + tooltip (2026-09-29)
- [x] Thu gọn khoảng trống thừa trong card (Height 228 → 186) (2026-09-29)
- [x] Bỏ hàng tile liên kết tài liệu (WinUI / Windows App SDK / Gallery / Toolkit) ở header Dashboard (2026-09-29)
- [x] Mở cửa sổ ở trạng thái maximize (2026-09-29)
- [x] README MainLauncher cập nhật theo các thay đổi trên (2026-09-29)
- [x] Bấm **Logs** ở menu trái → app đơ rồi tắt (người dùng báo 2026-10-22; Event Log: 0xc000027b trong Microsoft.UI.Xaml.dll).
  Nguyên nhân: `LogsViewModel` cần `InMemoryLoggerProvider` qua DI nhưng provider chỉ được `AddProvider` cho logging, chưa
  đăng ký → `InvalidOperationException: Unable to resolve service...` lúc dựng LogsPage. Sửa: `services.AddSingleton(InMemoryLog)`;
  tạo `LogsViewModel` ngay lúc khởi động (trang Logs có cả dòng từ lúc mở app, trước đây chỉ từ lần đầu mở trang). Thêm:
  lỗi khi chuyển trang / lỗi chưa bắt ghi vào `Logs\launcher-*.log` (trước đây chỉ thấy app tắt). GUI Sandbox 6/6 (bản cũ
  tái hiện: trang không mở được, log có đúng lỗi trên) (2026-10-22)
- [x] **Ẩn / hiện module ở Dashboard và Module List** (người dùng hỏi 2026-10-22): nút "Ẩn / hiện" cạnh tiêu đề Modules
  (Dashboard) và cạnh Reload (Module List) - ô tích từng module + Hiện tất cả, dòng "Đang hiện 10 / 12" - cùng 1 lựa chọn,
  Settings có danh sách đủ để bật lại; lưu `Config\launcher-settings.json` (không sửa modules.json); module ẩn mà đang chạy
  vẫn hiện tạm để bấm Stop; thẻ thống kê vẫn đủ. README cập nhật. GUI Sandbox 20/20 (ẩn 2 ở cả 2 trang, mở lại app vẫn ẩn,
  ẩn lúc đang chạy → hiện tạm ở cả 2 trang rồi ẩn khi Stop, Settings, Hiện tất cả, ẩn hết → dòng nhắc) (2026-10-22)
- [x] **Hướng dẫn (F1)**: mục "Hướng dẫn (F1)" ở menu trái + phím F1 mở cửa sổ Hướng dẫn dùng chung (7 mục: Bắt đầu nhanh,
  Dashboard, Module List, Start / Stop / Restart, Ẩn / hiện module, Logs, Settings và cấu hình - gồm các trường của
  modules.json). README cập nhật. GUI Sandbox 8/8 (mở từ menu không đổi trang, 1 cửa sổ, tìm không dấu, F1) (2026-10-22)
- [x] 3 lỗi trong `_note.txt` (2026-10-22):
  - Mở MainLauncher khi đang chạy → không mở bản thứ 2, cửa sổ đang có hiện lên trước (kể cả đang thu nhỏ) + thông báo
    "AppSuite đang chạy" (named mutex + event).
  - Đóng MainLauncher mà còn module chạy → hỏi "Đóng hết / Để chạy tiếp / Huỷ" + "Nhớ lựa chọn"; Settings: Hỏi / Luôn đóng
    hết / Luôn để chạy tiếp (`launcher-settings.json`).
  - Mở lại MainLauncher → nhận module đang chạy (đúng đường dẫn .exe): thẻ Running, Stop được, Start mờ.
  Cập nhật F1, README. GUI Sandbox 24/24 (2026-10-22)
- [x] Giao diện tiếng Việt có dấu: menu (Tổng quan / Danh sách module / Nhật ký / Cài đặt), thẻ thống kê, nút Chạy / Dừng /
  Chạy lại, trạng thái (Đang chạy / Đã dừng / Lỗi...), "x/y module đang chạy", trang Cài đặt, mô tả 12 module trong
  modules.json; nội dung dòng nhật ký giữ tiếng Anh. Sửa kèm: nút Làm mới bị cắt khi cửa sổ hẹp (thẻ thống kê rộng cố định).
  Cập nhật F1, README. GUI Sandbox 28/28 (đi qua mọi trang, không còn chữ tiếng Anh / không dấu) (2026-10-22)
- [x] Giao diện gọn hơn (duyệt bằng mockup + ảnh trước / sau, Pictures\AppSuite_UI\MainLauncher_2026-10-22): header 170 → 100,
  thẻ thống kê thấp ~ một nửa + viền, menu trái 320 → 240, thẻ module 186 → 168, các trang cùng lề / cỡ tiêu đề. Sửa kèm:
  gợi ý phím "F1" nổi ở mép trên cửa sổ (`KeyboardAcceleratorPlacementMode="Hidden"`); màu biểu tượng module đổi mỗi lần mở
  app (`string.GetHashCode()` ngẫu nhiên theo tiến trình → băm FNV-1a cố định). GUI Sandbox 28/28 (2026-10-22)
- [x] **Kéo thả đổi thứ tự module** ở Tổng quan và Danh sách module (chung 1 thứ tự), lưu `launcher-settings.json`
  (`ModuleOrder`, không sửa modules.json); module ẩn giữ chỗ; module mới đứng cuối; Cài đặt → "Sắp xếp lại theo
  modules.json". Cập nhật F1, README. GUI Sandbox 11/11 (kéo chuột thật) + giao diện 28/28 (2026-10-22)
- [x] **Khởi động cùng Windows** (Cài đặt, 2026-10-23): công tắc ghi `HKCU\...\CurrentVersion\Run` ("AppSuite MainLauncher" =
  exe + `--startup`, chỉ tài khoản hiện tại, không cần admin); trạng thái đọc từ registry mỗi lần mở trang - báo khi bị tắt
  ở Task Manager hoặc đang trỏ MainLauncher ở thư mục khác, bật lại là sửa. Ô "thu nhỏ xuống thanh tác vụ" (mặc định bật,
  launcher-settings.json). Module không tự chạy theo (dùng AutoStart). Cập nhật F1, README. GUI Sandbox 21/21 (bật / tắt →
  registry, giả lập Task Manager tắt + đường dẫn cũ, chạy `--startup` thu nhỏ / bỏ tích thì mở to)
- [ ] Thử tay: bật công tắc ở bản `Application\` thật, đăng xuất / đăng nhập lại Windows → MainLauncher tự mở, nằm ở khay
- [x] **Bấm X thu xuống khay hệ thống** (Cài đặt, mặc định bật, 2026-10-23): bấm X ẩn cửa sổ, module chạy tiếp, icon AppSuite ở
  khay (bong bóng "AppSuite vẫn đang chạy" lần đầu); bấm icon mở lại; chuột phải: Mở AppSuite / "N module đang chạy" /
  Thoát AppSuite (thoát hẳn, còn module thì hỏi như cũ). Tắt công tắc → icon mất, X là thoát. Khởi động cùng Windows + bật
  khay → nằm sẵn ở khay. `Services\TrayIcon` (Win32, như ScreenCapture). Cập nhật F1, README. GUI Sandbox 22/22
- [ ] Windows 11 mặc định để icon mới trong nút ^ - xem có cần nhắc người dùng kéo icon ra thanh tác vụ không (thử tay)
- [ ] ModuleA / ModuleB (module mẫu) vẫn hiện chữ không dấu ("Ung dung quan ly A - chay doc lap voi MainLauncher")
- [ ] **[Đề xuất · Nên làm]** Stop chờ module tự đóng 5 giây rồi buộc tắt (`ProcessManager.StopProcess`: `CloseMainWindow` +
  `WaitForExit(5000)` + `Kill`) - module đang hỏi "Lưu không?" bị tắt mất phần chưa lưu; chờ 5 giây trên luồng UI nên launcher
  đứng hình lúc đó. Restart còn buộc tắt ngay (`StopProcess(force: true)`), không chờ. Hướng sửa: chờ không chặn UI, quá giờ
  thì hỏi "Module chưa đóng - Buộc tắt / Chờ tiếp" thay vì tự Kill; Restart đóng nhẹ như Stop trước. (F1 đã ghi rõ cách chạy
  hiện tại để người dùng lưu trước)
- [ ] Bản đang dùng ở `Application\` là bản cũ - publish lại MainLauncher (`build\Publish-AppSuite.ps1 -Targets MainLauncher`)

## ScreenCapture

- [x] Popup **Danh sách ảnh** khi nhiều ảnh bị đẩy lên trên (che nút / tab) hoặc bị cắt mất nút "Mở lại tab vừa đóng"; ít ảnh
  (lọc) thì đúng chỗ (người dùng báo 2026-10-22). Nguyên nhân: danh sách `MaxHeight="520"` cố định + ô lọc + nút ≈ 640 px,
  cửa sổ thấp không đủ chỗ dưới nút → WinUI dời popup. Sửa: `FitTabListToWindow` (lúc Opening) đặt chiều cao tối đa theo chỗ
  trống thật dưới nút (160-520). GUI Sandbox 40 ảnh: bản cũ 3/6 (tái hiện: mất nút Mở lại tab), bản sửa 6/6 (phóng to, có lọc,
  cửa sổ thấp); unit test 59/59 (2026-10-22)
- [x] **[Đề xuất · Nên làm]** Kéo tab đổi chỗ (người dùng hỏi 2026-10-16) - **xong 2026-10-19**, xem dòng tổng kết dưới. Hiện tắt hẳn: `DocumentTabs` đặt
  `CanReorderTabs="False" CanDragTabs="False"` (PLAN.md: "chưa có kéo đổi thứ tự tab"). Giải pháp: bật 2 cờ (TabView tự
  làm phần kéo vì TabItems là TabViewItem trực tiếp) + `TabItemsChanged` / `TabDragCompleted` → `UpdateTabPosition()`
  ("Ảnh x / n", chữ đậm) + `TrySaveSession()` (phiên tạm ghi theo thứ tự tab → mở lại giữ thứ tự mới). Cần kiểm: kéo-thả
  file ảnh vào Editor (`AllowDrop` cả cửa sổ) chỉ nhận khi gói kéo có file (StorageItems), không nuốt thao tác kéo tab;
  không cho kéo tab ra ngoài thành cửa sổ riêng (`TabDroppedOutside` bỏ qua); Danh sách ảnh / Ctrl+1…9 theo thứ tự mới.
  Công sức: nhỏ.
- [x] **[Đề xuất · Nên làm]** (xong 2026-10-19) Đóng ảnh đã **tự lưu** thì hỏi xoá luôn file đã lưu (ảnh chỉ dùng 1 lần) - người dùng hỏi
  2026-10-16. Hiện: tự lưu xong tab coi là đã lưu → đóng không hỏi gì, file nằm lại trong thư mục tự lưu. Giải pháp:
  - Cờ mới `AutoSavedPath` trên tab (ghi vào phiên tạm): chỉ file do **tự lưu sau khi chụp** tạo ra mới được đề nghị xoá
    - ảnh mở từ file có sẵn (Mở ảnh / kéo-thả) và file người dùng tự Lưu thành **không bao giờ** bị xoá. Lưu đè (Ctrl+S)
    vào chính file tự lưu vẫn giữ cờ; file đã bị đổi tên / xoá bên ngoài thì bỏ qua.
  - Cài đặt > Lưu ảnh, ô mới "Khi đóng ảnh đã tự lưu": **Giữ file** (mặc định, như bây giờ) / **Hỏi có xoá file không**
    / **Luôn xoá file**. Xoá = chuyển vào **Thùng rác** (lấy lại được), không xoá hẳn.
  - Hỏi: hộp thoại "Đóng và xoá file / Đóng, giữ file / Huỷ" + ô "Không hỏi lại" (đổi cài đặt). Ảnh còn sửa chưa lưu thì
    gộp vào hộp hỏi Lưu hiện có. Đóng tất cả: 1 hộp hỏi cho cả loạt ("Xoá N file đã tự lưu?"). Tắt Editor bằng nút X
    (nhớ tab cho lần sau) thì **không** xoá gì. Thêm lệnh "Đóng và xoá file" ở menu chuột phải của tab (mục dưới).
  - Công sức: vừa (cờ + phiên tạm + cài đặt + 2 hộp thoại + F1 / README + unit test phần chọn file được xoá).
- [x] **[Đề xuất · Nên làm]** (xong 2026-10-19, chưa làm đường dẫn ở thanh trạng thái) Mở thư mục chứa ảnh của tab - người dùng hỏi 2026-10-16. Tab có `FilePath` (tự lưu / Lưu /
  mở từ file) nên biết thư mục. Giải pháp làm cả 2:
  - **Menu chuột phải trên tab** (ContextFlyout của TabViewItem; Shift+F10 / phím menu cũng mở): Mở thư mục chứa ảnh
    (Explorer chọn sẵn file - `explorer /select,`), Copy đường dẫn file, Lưu thành…, Đóng, Đóng các tab khác, Đóng các
    tab bên phải, Đóng và xoá file (chỉ ảnh đã tự lưu, xem mục trên). Ảnh chưa lưu → "Mở thư mục" mờ, gợi ý mở thư mục
    tự lưu; file đã bị xoá / đổi tên → mở thư mục cha nếu còn.
  - **Nút "Mở thư mục" ở tab Tệp** của ribbon (cạnh Lưu / Lưu thành) cho ảnh đang mở; tooltip ghi đường dẫn. Có thể thêm
    đường dẫn bấm được ở thanh trạng thái.
  - Công sức: nhỏ - vừa.
- [x] **Mở lại tab vừa đóng** (2026-10-19, `Models\ClosedTabStack.cs`): Ctrl+Shift+T / menu chuột phải của tab / nút cuối
  Danh sách ảnh / menu khay "Mở lại ảnh vừa đóng (N)" - tab đóng gần nhất về đúng vị trí cũ, còn nguyên ảnh, hình vẽ,
  Undo, zoom (giữ chính EditorViewModel); bấm tiếp lùi dần; tối đa 10 tab / ~300 MB; danh sách thuộc launcher nên Editor
  đóng rồi mở lại vẫn dùng được; tắt app là mất. Unit test 45/45 (4 mới); GUI Sandbox 18/18 (kể cả mở lại vào cửa sổ Editor
  mới, đóng / mở lại lần 2). Menu khay chưa test GUI (menu Win32). Cập nhật F1, README
  - Giới hạn đưa vào **Cài đặt > Phiên làm việc** (người dùng yêu cầu): "Mở lại tab vừa đóng: giữ tối đa N tab (0–50, mặc định
    10, 0 = tắt), M MB (50–4000, mặc định 300)"; đổi là cắt ngay. Unit test 46/46; GUI 7/8 (1 ca do test không đọc câu
    "đang tắt" - app đúng). Cập nhật F1, README (2026-10-19)- [x] **Tab: kéo đổi chỗ + menu chuột phải + Mở thư mục + đóng ảnh đã tự lưu** (2026-10-19, `Views\EditorWindow.Tabs.cs`):
  kéo tab đổi thứ tự (ghi phiên tạm theo thứ tự mới); menu chuột phải tab (Mở thư mục chứa ảnh, Copy đường dẫn, Lưu
  thành…, Đóng / các tab khác / bên phải, Đóng và xoá file); nút Mở thư mục ở tab Tệp; Cài đặt > Lưu ảnh > Khi đóng ảnh
  đã tự lưu: Giữ (mặc định) / Hỏi (+ Không hỏi lại) / Luôn xoá - vào Thùng rác, chỉ file do tự lưu tạo ra (cờ
  `AutoSavedPath` trong phiên tạm), đóng nhiều tab hỏi 1 lần. Unit test 41/41 (3 mới); GUI Sandbox 25/27 + chạy lại kéo
  tab / Explorer: kéo tab qua (lần 1 chuột giả lập kéo nhanh quá không bắt đầu kéo), Explorer mở đúng thư mục nhưng test
  chưa đọc được file đang chọn sẵn. Thêm GUI 14/14: Luôn xoá (không hỏi, vào Thùng rác), tắt Editor bằng X không xoá gì
  (phiên tạm nhớ cờ tự lưu), Đóng tất cả (hỏi lưu → 1 hộp "Xoá 2 file đã tự lưu?" → xoá, ảnh mở từ file còn nguyên).
  Cập nhật F1, README. Người dùng thử tay 2026-10-19: kéo tab OK, Mở thư mục chọn sẵn file OK - còn chờ thử Shift+F10
- [ ] Thanh tab nhiều ảnh: tab đang mở nền màu chủ đạo + chữ trắng đậm; tên tab gọn (hôm nay chỉ giờ, ngày khác
  "15/10 16:22", đầy đủ ở tooltip); lăn chuột trên thanh tab = cuộn; Danh sách ảnh (nút cuối thanh tab / bấm "Ảnh x / n" /
  Ctrl+Shift+A: ảnh thu nhỏ, lọc theo tên / giờ); Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+PgDn / PgUp, Ctrl+1…8, Ctrl+9. Cập
  nhật F1, README (2026-10-16)
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

*Ngoài lộ trình (người dùng đề xuất, 2026-10-08):*
- [x] **Tìm chữ** giống ImageCompare — nút *Tìm chữ* (nhóm Chữ, tab Trang chủ) / `Ctrl+F` mở khung bên phải ảnh: ô ngôn
  ngữ (Tiếng Nhật = Windows OCR, dự phòng Tesseract; Tiếng Việt / English = Tesseract cả trang; nhớ trong settings.json
  `OcrLanguage`), đọc ảnh nền 1 lần / tab (đọc lại khi ảnh đổi), tìm không dấu / hoa thường + gần đúng ≈, chỗ khớp tô vàng
  đè lên ảnh, danh sách bấm để cuộn tới, `Enter` / `Shift+Enter` / `Esc`; **Copy chữ** cả ảnh; **Tô Highlight (N)** →
  hình Highlight thật (1 bước Undo); tab Vùng chọn có **Copy chữ** (đã đọc ảnh thì lấy chữ trong vùng, chưa thì đọc riêng
  vùng). OCR tách khỏi ImageCompare thành thư viện chung **`Common.Ocr`** (+ `Common.Ocr.targets` chép native Tesseract đúng
  kiến trúc + tessdata + VC++ runtime — để trong thư viện thì bản publish thiếu VC++ runtime và thừa native x86). Kèm sửa:
  dòng OCR tiếng Nhật không chen dấu cách (`OcrText.JoinLine`; Copy chữ của ImageCompare trước ra "受 注 番 号").
  Build solution 0 warning; ImageCompare.Tests 104/104; Sandbox `sct_ocr` 17/17 (Tesseract vie: đọc đúng 6/6 dòng, tìm
  'giao' 2, 'khach hang' 1, 'Delivred' ≈, Enter, Copy chữ, Highlight 3 → phiên lưu 3 hình, Copy chữ vùng 2 cách, nhớ /
  lưu ngôn ngữ); máy thật Windows OCR tiếng Nhật: 140 dòng 0,45 s, 受注番号 / 受注数量 khớp, 用途区分 ≈ (OCR đọc 厓途区分).
  Bản publish ScreenCapture 62 → 72 MB. F1 + README (2026-10-08) — cần thử tay trên ảnh thật
- [x] Bug (người dùng báo): Tìm chữ tiếng Nhật gõ "日" không ra kết quả - "Không đọc được chữ: Operation aborted. Another
  RecognizeAsync operation is already running!". Gõ qua IME làm ô tìm đổi nhiều lần → mỗi lần huỷ + đọc lại ảnh, mà huỷ
  chỉ thôi chờ, lệnh đọc của Windows OCR vẫn chạy → lần đọc mới gọi cùng OcrEngine bị từ chối. Sửa ở `Common.Ocr`
  (`WindowsFormReader.Recognize`: mọi lần đọc trên 1 engine xếp hàng qua khoá, luôn chờ lệnh đọc xong rồi mới xét huỷ -
  ImageCompare dùng chung cũng được sửa) + ScreenCapture: đang đọc đúng ảnh + ngôn ngữ thì gõ tiếp chờ lần đang chạy,
  không đọc lại từ đầu. Unit test mới `WindowsOcrConcurrencyTests` (cần OCR tiếng Nhật của Windows): code cũ tái hiện
  đúng lỗi, code mới đạt; ImageCompare.Tests 106/106, ScreenCapture.Tests 46/46 (2026-10-21)
- [ ] Thử tay: Tìm chữ tiếng Nhật trên ảnh thật, gõ qua IME (日, 予定...) - ra kết quả, không còn lỗi Operation aborted
- [x] **Lấy chữ từ ảnh nhanh hơn** (người dùng hỏi 2026-10-21): (1) **Chụp lấy chữ** - phím tắt mới `Ctrl+Alt+T` (đổi
  được ở Cài đặt > Phím tắt; settings.json cũ tự bổ sung) + menu khay "Chụp lấy chữ": chọn vùng trên màn hình như Vùng chọn
  → OCR (ngôn ngữ theo khung Tìm chữ) → copy chữ vào clipboard, không mở Editor / không tự lưu; luôn hiện thông báo nhỏ
  ("Đã copy chữ: N dòng" + 2 dòng đầu / không đọc được + ngôn ngữ đang chọn / lỗi) với nút Mở trong Editor. (2) Editor:
  **chuột phải vào trong vùng chọn** → menu Copy chữ / Copy ảnh vùng / Cut / Cắt ảnh / Xoá nội dung vùng / Bỏ chọn (kèm phím
  tắt); **`Ctrl+Shift+C`** = Copy chữ trong vùng chọn, không có vùng = chữ cả ảnh (trước đây Ctrl+Shift+C = copy ảnh như
  Ctrl+C). Không thêm thẻ ở cửa sổ chính (lưới 8 thẻ). Cập nhật F1, README. Unit test 50/50 (4 mới: phím mặc định đủ /
  không trùng / Ctrl+Alt+T / settings cũ được bổ sung); GUI Sandbox (Tesseract vie) 12/13 - mục còn lại: clipboard bị ghi
  đè bởi máy thật đúng lúc test (Sandbox dùng chung clipboard), thông báo vẫn đúng 2 dòng đã copy (2026-10-21)
- [ ] Thử tay: Ctrl+Alt+T chọn vùng chữ Nhật trên màn hình thật → dán; chuột phải vùng chọn → Copy chữ; Ctrl+Shift+C
- [x] **Che các chỗ tìm thấy** (người dùng chọn 2026-10-21): khung Tìm chữ thêm **Che Mosaic (N)** / **Làm mờ (N)** (4 nút
  chỗ khớp xếp 2 × 2) - tìm tên khách / số điện thoại / mã đơn rồi che hết trước khi gửi ảnh; mỗi chỗ thành 1 hình Mosaic /
  Làm mờ thật (sửa / xoá từng cái được), cả nhóm 1 bước Undo, vùng nới 4 px. Mức che tự tăng theo chiều cao chữ
  (`RedactAnnotation.StrengthToHide`) - ảnh Sandbox lần đầu cho thấy chữ 40 px làm mờ ở Size 3 vẫn đọc được số. Cập nhật
  F1, README. Unit test 59/59 (9 mới); GUI Sandbox 9/9 (tìm "090" → 2 chỗ, Che Mosaic (2), Undo 1 lần gỡ cả 2, Làm mờ (2),
  phiên tạm còn đúng 2 hình Làm mờ) (2026-10-21)
- [ ] Thử tay: Che Mosaic / Làm mờ trên ảnh chụp thật có chữ Nhật (tên khách, 電話番号) - xem đã kín chưa trước khi gửi
- [ ] **[Đề xuất · Nên có]** Tìm theo mẫu cho Che: "mọi số điện thoại" / "mọi email" / "mọi dãy số ≥ N chữ số" - che hết
  các giá trị khác nhau trong 1 lần thay vì tìm từng cái

## ImageCompare

- [x] Tooltip phím tắt ("Ctrl+V"...) có thể hiện khi rê chuột vùng trống: phím tắt gắn ở Grid gốc →
  `KeyboardAcceleratorPlacementMode="Hidden"`; GUI Sandbox 3/3 không còn tooltip (2026-10-13)
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

## Rdbms.HtmlGenerator (ModuleE)

- [x] Nhiều hồ sơ kết nối (dev / test / prod): combobox Hồ sơ + nút ⋯ (Thêm = sao chép hồ sơ đang chọn, Đổi tên, Xoá);
  mỗi hồ sơ có nguồn + 4 tab kết nối + file `{database} ({hồ sơ})`.db/.html riêng; config.xml cũ tự thành hồ sơ "Mặc
  định" (giữ tên file cũ). Unit test mới `Tests\Rdbms.HtmlGenerator.Tests` 12/12; GUI Sandbox: hồ sơ 26/26, bộ cũ
  29/29. Cập nhật F1, README (2026-10-16); thử tay với DB thật (dev / prod) theo dõi ở mục "Thử tay: So sánh schema" bên dưới
- [x] Đổi tiêu đề cửa sổ "Module E" → "Rdbms.HtmlGenerator - Từ điển dữ liệu (PostgreSQL / Oracle)", tiêu đề trong
  trang bỏ "Module E", dòng mô tả sửa đúng tên file `{tên database}.db/.html` (2026-10-09)
- [x] Không kết nối được thì báo hộp thoại thay vì trông như treo: timeout 10 giây (Postgres + Oracle), lỗi ghi vào
  log + ContentDialog kèm gợi ý (không tới được host / sai user-password / sai database, Service Name, SID).
  Build 0 warning (2026-10-09)
- [ ] Thử tay: bấm "1. Doc Database" với host không tới được (vd 192.168.145.107:1400) → hộp thoại sau ~10 giây
- [x] Thêm nguồn **MySQL / MariaDB** (`MySqlConnector`) và **SQL Server** (`Microsoft.Data.SqlClient` 7, không kéo
  Azure.Identity; Windows Authentication, `Host\Instance`). Nguồn đổi từ radio → **combobox** (nhớ lựa chọn); hộp thoại
  kết nối 4 tab, mở sẵn tab đang chọn. Tiêu đề đổi thành "Từ điển dữ liệu database". Thử bằng console: cả 4 nguồn ngắt
  đúng timeout khi host không tới được / cổng đóng; SQL Server đọc đúng trên LocalDB (khóa chính ghép, khóa ngoại 2 cột,
  view, MS_Description tiếng Việt, lọc schema, sai database → 4060). Build 0 warning (2026-10-09)
- [x] Nút **Cài đặt**: giới hạn thời gian kết nối (mặc định 10 giây), giới hạn thời gian truy vấn (mặc định 120 giây),
  tự mở HTML sau khi xuất; nút "Mặc định". Hộp thoại lỗi phân biệt không kết nối được / truy vấn quá giờ (2026-10-09)
- [x] Nút màu xanh chạy theo bước: đọc xong → "2. Xuat HTML" xanh, xuất xong → "Mo file HTML" xanh (trước đây luôn
  là nút 1); mở lại module tự tính bước từ file .db / .html có sẵn. Cài đặt thêm "Tự mở thư mục chứa file HTML sau khi
  xuất" (Explorer chọn sẵn file). Build 0 warning, app khởi động OK (2026-10-09)
- [x] Thử GUI trong Windows Sandbox (bản Release framework-dependent, không mạng, `.db` mẫu): combobox nhớ nguồn,
  hộp thoại mở sẵn tab đang chọn, Thử kết nối (thiếu Host / host không tới được → đỏ sau 10 giây / đổi tab xoá kết quả),
  Windows Authentication khoá ô Username, Lưu, Cài đặt (Mặc định / Lưu timeout 3 giây + mở thư mục), nút xanh bước
  2 → 3, Xuất HTML mở Explorer, đọc DB lỗi → hộp thoại "tối đa 3 giây" + nút 1 xanh lại: 20/20 (2026-10-09)
- [x] Cỡ chữ hàng nút không đồng nhất: 3 nút bước (gán Style Accent/Default của WinUI) bỏ qua style gọn chung nên chữ
  14 / padding to. SharedUI `CompactStyles.xaml` thêm `CompactButtonStyle` / `CompactAccentButtonStyle` (style ngầm định
  của Button dựa trên `CompactButtonStyle`); nút bước dùng 2 style này. Build solution 0 warning, Sandbox 20/20 (2026-10-09)
- [x] 27 nút `AccentButtonStyle` ở FileTools (12 trang), ImageCompare, Mcf.CrudDiagram / DbDef / Screen, ModuleB/C,
  ScreenCapture (Cài đặt) đổi sang `CompactAccentButtonStyle` — cùng cỡ với nút bên cạnh. Build solution 0 warning;
  Sandbox: mở từng module + mọi trang FileTools + cửa sổ Cài đặt ScreenCapture, không app nào văng, ảnh chụp đồng nhất
  (2026-10-09)
- [x] Cửa sổ hẹp: hàng nút bị cắt → tách 2 hàng (trên: Nguồn + Thiết lập, Cài đặt / Hướng dẫn dồn phải; dưới: 3 bước
  nối bằng mũi tên). Sandbox: cửa sổ ~650 px logic vẫn thấy đủ 7 nút, 21/21 (2026-10-09)
- [x] Bảng trùng tên ở 2 schema làm **cả lần đọc lỗi** (`UNIQUE constraint failed: Tables.TableName`) → chỉ tên trùng
  đổi thành `schema.bảng` (`TableNameQualifier`). Sửa kèm: khoá ngoại nhiều cột của PostgreSQL bị nhân chéo (đọc từ
  `pg_constraint` thay vì information_schema); gom khoá ngoại theo cả schema. Thử console: PostgreSQL 17 tạm (cổng riêng,
  đã xoá) + SQL Server LocalDB, đọc → SQLite → HTML OK (2026-10-09)
- [x] Đọc DB lỗi thì nút "2. Xuất HTML" tắt dù file .db cũ vẫn còn → giữ bật cho bản cũ (2026-10-09)
- [x] Nút **Huỷ** khi đang đọc DB (chỉ hiện lúc đọc, dừng ngay kể cả đang chờ kết nối, .db cũ giữ nguyên) (2026-10-09)
- [x] Giao diện có dấu: cửa sổ, hộp thoại, dòng trạng thái, log, trang HTML xuất ra ("Thiết lập kết nối", "1. Đọc
  database → SQLite", "Tên bảng...", "Khoá chính"...); F1 + README theo tên nút mới. Sandbox 29/29 (thêm: đọc lỗi vẫn
  xuất được .db cũ; Huỷ khi SQL Server chờ ở cổng không trả lời → dừng < 1 giây, không hộp thoại lỗi, .db cũ còn)
  (2026-10-09)
- [x] Nút **Thử kết nối** trong hộp thoại thiết lập (thanh nút Luu | Thử kết nối | Huy): thử bằng thông tin đang nhập
  ở tab hiện tại, chưa cần Lưu; báo phiên bản máy chủ + số bảng / view sẽ đọc (0 bảng = cảnh báo vàng), lỗi đỏ kèm gợi ý.
  Sửa kèm: Oracle sai Service Name trước đây bị báo nhầm "không tới được máy chủ" (ORA-12514 nằm trong ORA-50201);
  `BindByName = true` cho mọi lệnh Oracle (câu đọc bảng dùng `:owner` 2 lần - có thể lỗi ORA-01008 từ trước).
  Thử GUI bằng UI Automation trên LocalDB: thiếu Host / OK 2 bảng / OK 0 bảng / sai database; console: PostgreSQL sai
  mật khẩu (28P01), Oracle sai mật khẩu (1017) / sai Service Name (12514). Build 0 warning (2026-10-09)
- [ ] Thử Oracle với tài khoản thật sau khi thêm `BindByName` (máy dev không có mật khẩu Oracle / PostgreSQL để thử
  đường thành công)
- [x] Thông báo lỗi PostgreSQL lúc đăng nhập bị lỗi font khi máy chủ dùng locale tiếng Nhật (máy chủ gửi Shift-JIS
  trước khi thống nhất UTF-8, chữ vỡ thành "�p�X���[�h...") - phần "Chi tiết" thay bằng dòng "thông báo gốc bị sai bảng mã"
  + mã lỗi + nghĩa tiếng Nhật chuẩn (vd 28P01 パスワード認証に失敗しました); thông báo đọc được giữ nguyên (2026-10-23).
  Unit test 77/77 (thêm `ServerMessageTextTests`: tạo chữ vỡ thật bằng Shift-JIS → UTF-8)
- [ ] Thử tay với máy chủ PostgreSQL tiếng Nhật thật: sai mật khẩu → hộp thoại lỗi không còn chữ vỡ
- [x] Trang HTML thêm **số dòng ước tính** (menu trái + đầu bảng, từ thống kê của DB), bảng **Index** (cột, DESC,
  biểu thức, loại, INCLUDE / WHERE) và **Ràng buộc UNIQUE / CHECK** cho cả 4 nguồn; `.db` cũ vẫn xuất được, trang nhắc
  đọc lại. Thử console PostgreSQL 17 tạm + SQL Server LocalDB, ảnh HTML bằng Edge headless trong Sandbox, GUI Sandbox
  29/29 (2026-10-09)
- [x] Thử với **Oracle 19c thật** (ABLIC_ITA: 6.472 bảng / view, 245.309 cột, đọc 18 giây): số dòng 2.494 / 3.405 bảng có
  thống kê, 5.846 index, 142 UNIQUE + 2.445 CHECK (19.111 NOT NULL tự sinh bỏ đúng). Sửa thêm: index theo hàm hiện biểu
  thức thật thay cho SYS_NC...$ (ALL_IND_EXPRESSIONS, LONG); HTML bỏ trường rỗng → 52 MB còn 24 MB. Ảnh HTML trong
  Sandbox OK (2026-10-09)
- [ ] Thử index / ràng buộc / số dòng với MySQL thật
- [ ] **[Đề xuất · Có thể]** Schema rất lớn (24 MB HTML với 245k cột): cân nhắc nén dữ liệu trong trang hoặc tách file
  theo nhóm bảng nếu trình duyệt mở chậm
- [x] Đổi nguồn (vd Oracle → MySQL) vẫn trỏ file .db / .html của nguồn cũ: MySQL chưa đọc mà "2. Xuất HTML" vẫn bật
  và xuất ra bản Oracle; đọc MySQL thành công thì ghi đè `.db` của Oracle. Nguyên nhân: tên file tính theo nguồn của
  hồ sơ, nhưng nguồn chỉ ghi vào hồ sơ sau khi đã tính lại file đích. Unit test 12/12; GUI Sandbox 16/16 (đổi nguồn tắt /
  bật đúng nút, xuất đúng `mydb.html` / `ORCL.html`, file Oracle không bị ghi, đọc lỗi báo đúng cổng 3306, mở lại nhớ
  nguồn) (2026-10-21)
- [x] Giao diện mới: tiêu đề gọn, Cài đặt / Hướng dẫn cùng hàng tiêu đề; thẻ **Kết nối** (nhãn Hồ sơ / Nguồn thẳng cột,
  2 combobox cùng rộng, dòng tóm tắt Host:Port/Database + "đã đọc lúc …" / "chưa đọc lần nào" / nhắc Thiết lập kết
  nối); thẻ **Các bước** (3 nút có biểu tượng chia đều - cửa sổ hẹp không bị cắt, tiến trình chỉ hiện khi bận, trạng
  thái trong thẻ; đổi nguồn thì trạng thái nói nguồn mới đã đọc chưa thay vì giữ "Đã xuất ...ORCL.html"); **Nhật ký** có
  tiêu đề + nút **Copy** (chép mọi dòng log). Duyệt bằng ảnh trước / sau (Pictures\AppSuite_UI\Rdbms_2026-10-21).
  Cập nhật F1, README. Unit test 12/12; GUI Sandbox 29/29 + 26/26 + 21/21 (2026-10-21)
- [x] **So sánh schema** giữa 2 lần đọc (vd dev ↔ prod, bản cũ ↔ bản mới): nút "So sánh schema" trên cùng → chọn A / B
  từ các file .db (A chọn sẵn file của hồ sơ / nguồn đang chọn, Đổi chỗ, chọn trùng báo lỗi, chưa đủ 2 file báo cách
  tạo) → trang `So sánh {A} - {B}.html`: tóm tắt số bảng, bảng chỉ có ở A / B, bảng có thay đổi (cột thêm / bớt / kiểu /
  độ dài / Null / mặc định / khoá chính / mô tả; khoá ngoại, index, ràng buộc so theo nội dung, không theo tên hệ thống
  sinh), ô tích ẩn thay đổi chỉ khác mô tả; .db bản cũ thì bỏ qua index và ghi rõ. Cập nhật F1, README. Unit test 23/23
  (11 mới); GUI Sandbox 16/16 + ảnh trang kết quả (2026-10-21)
- [ ] Thử tay: So sánh schema với 2 DB thật (dev / prod) - kiểm tra trang kết quả có báo thừa / thiếu gì không
- [ ] Thử tay: đổi nguồn Oracle ↔ MySQL trên máy thật với DB thật, xuất HTML đúng database; Copy nhật ký rồi dán
- [ ] Thử MySQL với máy chủ thật (máy dev không có MySQL — mới thử được trường hợp không kết nối được)
- [ ] Thử tay GUI: combobox đổi nguồn, hộp thoại kết nối 4 tab (tích Windows Authentication khoá ô user/pass),
  hộp thoại Cài đặt (Lưu / Mặc định / Huỷ), tự mở HTML; regression PostgreSQL + Oracle với DB thật
- [x] **Lịch sử các lần đọc + "So với bản trước"**: trước khi "1. Đọc database" ghi đè, `.db` cũ chép vào
  `Data\Database\Lịch sử\{tên}\yyyy-MM-dd HHmmss.db`; đọc xong nhật ký ghi tóm tắt so với lần trước ("1 bảng mới, 2 bảng có
  thay đổi" / "Không có thay đổi" - khi đó không giữ bản trùng); nút "So với bản trước (đọc lúc …)" dưới trạng thái mở trang
  So sánh bản cũ ↔ bản đang dùng; hộp thoại So sánh schema liệt kê cả bản cũ; Cài đặt "Giữ lại số bản đọc cũ" (mặc định 10,
  0 = không giữ). Unit test 29/29 (6 mới `SchemaHistoryTests`); GUI Sandbox 30/30 với PostgreSQL 17 tạm trong Sandbox
  (2026-10-22)
- [x] Trang HTML: lọc **Tất cả / Bảng / View** (kèm số lượng, nhãn VIEW / MATERIALIZED VIEW, áp dụng cả cho tìm cột, nhớ lựa
  chọn) + ô **Tìm cả trong mô tả** (bảng / cột khớp comment, menu trái hiện mô tả khớp, tô chỗ khớp). Edge headless trong
  Sandbox 20/20 (2026-10-22)
- [x] **Excel định nghĩa bảng (テーブル定義書)**: nút "Excel định nghĩa bảng" (dòng "Xuất thêm") → `{tên} テーブル定義書.xlsx`:
  sheet テーブル一覧 (link sang từng sheet) + mỗi bảng / view 1 sheet (thông tin bảng, カラム定義 với データ型 / 桁数 tách riêng,
  インデックス, 外部キー link, 制約), in A4 ngang. **Kiểm tra chất lượng schema**: nút "Kiểm tra chất lượng" → trang HTML 8 quy
  tắc (Nên sửa: không khoá chính, khoá ngoại khác kiểu; Cân nhắc: khoá ngoại không index + CREATE INDEX, index trùng / bị
  bao trùm + DROP INDEX, chưa thống kê; Thông tin: thiếu mô tả bảng / cột, tên khác quy ước), link sang trang từ điển. Cập
  nhật F1, README. Unit test 50/50 (21 mới); GUI Sandbox 23/24 - mục còn lại do kịch bản tìm sai chuỗi (XML ghi `&apos;`),
  link đã kiểm tay trong file (2026-10-22)
- [x] **Bỏ qua bảng / view khi đọc** theo mẫu tên (Cài đặt, trống = đọc hết): vd `tmp_%, %_bk` - `%` / `*` nhiều ký tự, `?`
  1 ký tự, `_` là chữ thật; có dấu chấm thì so với `schema.bảng`; bỏ cả cột / khoá ngoại / index / ràng buộc của bảng đó;
  nhật ký ghi bảng nào bị bỏ. Cập nhật F1, README. Unit test 57/57 (7 mới `TableNameFilterTests`); GUI Sandbox 14/14 với
  PostgreSQL tạm (không mẫu đọc hết, có mẫu bỏ đúng 2 bảng, `tmpl_settings` giữ, xoá mẫu đọc lại đủ) (2026-10-22)
- [x] Hộp thoại **Cài đặt không cuộn được** (người dùng báo 2026-10-22: không thấy ô Bỏ qua bảng): ContentDialog tắt cuộn dọc
  của chính nó → ô "Giữ lại số bản đọc cũ" (từ sáng 2026-10-22) và "Bỏ qua bảng / view" bị cắt mất ở mọi cửa sổ thấp; kiểm thử
  UIA vẫn đặt được giá trị cho ô khuất nên không phát hiện. Sửa: ScrollViewer riêng, MaxHeight theo cửa sổ lúc mở; mô tả dưới
  các ô số thành TextBlock xuống dòng (Description của NumberBox bị cắt). GUI Sandbox: cuộn được, 2 ô cuối thấy trọn
  (2026-10-22)
- [ ] Thử tay: mở `{tên} テーブル定義書.xlsx` bằng Excel thật (Sandbox không có Excel) - xem bố cục, bấm link, xem trước khi in;
  Kiểm tra chất lượng với DB thật (Oracle lớn: số mục, tốc độ). Người dùng đã xuất thử Z_ADDON_IGUACU_BK (SQL Server) 2026-10-23
  → báo 既定値 dạng ((0)) (đã sửa, mục dưới); còn xem bố cục / in bản tiếng Anh / tiếng Việt
- [x] **Excel định nghĩa bảng: chọn ngôn ngữ + sửa từ lần xuất thử thật** (2026-10-23): Cài đặt → "Ngôn ngữ Excel định nghĩa
  bảng" Tiếng Nhật (mặc định, テーブル定義書) / Tiếng Anh (Table Definition, sheet Table List) / Tiếng Việt (Định nghĩa bảng,
  sheet Danh sách bảng) - tiêu đề, nhãn, ○ / ✓, định dạng ngày, tên file (mỗi ngôn ngữ 1 file); config.xml cũ → tiếng Nhật.
  Sửa: 既定値 của SQL Server hiện ((0)) / (getdate()) → bỏ ngoặc bao ngoài (0, getdate()) ở Excel, HTML và trang so sánh; phần
  đầu sheet bảng ghi giá trị vào cả 6 ô C..H của vùng gộp → chỉ ô đầu; 件数（概算） là chữ "1,997" → số định dạng #,##0. Cập nhật
  F1, README. Unit test 71/71; GUI Sandbox 31/31 (config cũ → Nhật, chọn Anh / Việt → Lưu → config.xml, xuất 3 file riêng
  đúng tên sheet / nhãn, mở lại app giữ lựa chọn, Mặc định / Huỷ)
- [x] Người dùng xuất thử bản tiếng Anh / tiếng Việt với DB thật (Z_ADDON_IGUACU_BK, 2026-10-23): đúng tên sheet, nhãn, ✓,
  ngày, mặc định 0 (không còn ((0))). Thêm: nút **Mở thư mục** cuối dòng "Xuất thêm" (luôn bấm được) - Explorer tại
  Data\Database chọn sẵn file vừa xuất (chưa xuất gì → file .db); định nghĩa CHECK của SQL Server bỏ ngoặc bao ngoài
  (([A]=(1) OR [A]=(0)) → [A]=(1) OR [A]=(0)) ở Excel + HTML. Cập nhật F1, README. Unit test 77/77; GUI Sandbox 6/6
- [x] **Sơ đồ quan hệ trong trang HTML** (2026-10-23): mỗi bảng có khung SVG tự vẽ (không thư viện) - bảng đang xem ở giữa,
  bảng con (có FK trỏ tới) bên trái, bảng cha bên phải; hộp chỉ ghi cột PK / FK liên quan, đường nối từ cột FK tới cột được
  tham chiếu (mũi tên, rê chuột xem cột); bấm hộp mở bảng đó (Back quay lại); tự tham chiếu = vòng cung; bảng ngoài file = viền
  đứt; tối đa 8 bảng mỗi bên + "+N bảng khác"; thu gọn được; sáng / tối. Cập nhật F1, README. Thử bằng template + dữ liệu mẫu,
  ảnh Edge headless (orders 6 bảng, categories tự tham chiếu, tenants 15 bảng con, giao diện tối, bấm hộp → customers)
- [ ] Thử tay: xuất lại HTML của DB thật (Z_ADDON_IGUACU_BK / Oracle) → xem sơ đồ với bảng nhiều khoá ngoại, tên dài
- [ ] Thử tay: đọc lại DB thật 2 lần (có / không đổi schema) → xem dòng nhật ký + nút "So với bản trước"; trang HTML thử lọc
  View và tìm theo mô tả tiếng Việt / tiếng Nhật
- [x] ~~Bảng cùng tên ở 2 schema khác nhau (vd dbo.X và sales.X)~~ — thực tế không phải bị gộp mà làm cả lần đọc lỗi;
  đã sửa 2026-10-09 (xem mục bên trên)

## ModuleC

- [x] Nút "Huong dan su dung" đã gắn cửa sổ Hướng dẫn chung (SharedUI.Help.HelpWindow, F1) - không còn placeholder (kiểm lại 2026-10-22)
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
