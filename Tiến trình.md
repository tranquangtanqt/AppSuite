# Tiến trình AppSuite

Danh sách việc cần làm / đã làm của toàn bộ AppSuite. Có vấn đề mới thì thêm 1 dòng `- [ ]` vào đúng
mục; làm xong thì đổi thành `- [x]` và ghi ngày hoàn thành. Chi tiết thiết kế của từng module vẫn nằm
trong `PLAN.md` / `README.md` của module đó — file này chỉ để theo dõi tiến độ.

Cập nhật lần cuối: 2026-10-01

## Chung (repo, build, deploy)

- [x] Mọi module đăng ký đủ ở `AppSuite.sln`, `modules.json`, `Sync-Modules-Dev.ps1`,
  `Publish-AppSuite.ps1`, README gốc; mọi project cùng .NET 8 / WindowsAppSDK 2.2 (rà 2026-09-29; từ 2026-10-01 dùng component WinUI 2.2.1 + Runtime 2.2.0)
- [x] README gốc: sửa số project "9 project (MainLauncher + 8 module)" → 11 project (2026-09-29)
- [x] Có project unit test đầu tiên: `Tests\CsvEditor.Tests` (đã thêm vào `AppSuite.sln`) (2026-09-29)
- [x] Unit test cho engine ImageCompare (40) + parser Excel của Mcf.CrudDiagram (21), Mcf.DbDef (11), Mcf.Screen (29)
  — `Tests\*.Tests`, đã thêm vào `AppSuite.sln`, tất cả đạt trên máy dev; build sln 0 warning (2026-09-30)
- [ ] Chạy 4 project test mới trong Windows Sandbox (publish self-contained như CsvEditor.Tests)
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

## MainLauncher

- [x] Card module bị cắt mất hàng nút Start/Stop/Restart khi mô tả dài → card cao cố định, tên 1 dòng,
  mô tả tối đa 2 dòng + tooltip (2026-09-29)
- [x] Thu gọn khoảng trống thừa trong card (Height 228 → 186) (2026-09-29)
- [x] Bỏ hàng tile liên kết tài liệu (WinUI / Windows App SDK / Gallery / Toolkit) ở header Dashboard (2026-09-29)
- [x] Mở cửa sổ ở trạng thái maximize (2026-09-29)
- [x] README MainLauncher cập nhật theo các thay đổi trên (2026-09-29)

## ScreenCapture

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

## ImageCompare

- [x] Module So sánh ảnh + Tìm chữ (OCR Tesseract, tiếng Việt) — kiểm tra engine 30/30 + GUI trong Sandbox (2026-09-25)
- [x] Commit toàn bộ module + đăng ký vào sln / modules.json / build scripts
- [ ] Chạy thử GUI hộp thoại *Lưu PNG* và *Xuất báo cáo HTML*
- [ ] Chạy bản Release trên máy thật từ MainLauncher (kể cả Tìm chữ):
  `.\build\Publish-AppSuite.ps1 -Targets ImageCompare,MainLauncher`
- [ ] (Tuỳ chọn) So sánh chữ giữa A và B: dòng thêm / bớt / sửa qua OCR
- Giới hạn đã biết: OCR không chạy trên ARM64 (không có bản Tesseract native)

## Mcf.CrudDiagram.HtmlGenerator (ModuleH — `Note/ModuleH.txt`)

- [x] Dòng không có dữ liệu thì không vẽ
- [ ] Chạy UI thật (chọn thư mục → Đọc Excel → Xuất HTML → Mở HTML): index đủ 71 logic, tìm `MAM_BP`
  ra đúng các logic dùng bảng đó — PLAN.md ghi "chưa tự kiểm chứng bằng UI thật, cần user xác nhận"
- [ ] Header bảng giữ nguyên khi scroll — CSS chưa có `position: sticky`, và khung `.table-scroll`
  đang `overflow-x: auto` nên thêm sticky thôi chưa đủ, phải sửa cả khung cuộn

## ModuleC

- [ ] Nút "Huong dan su dung" vẫn là placeholder chưa gắn hành vi (PLAN.md ghi cố ý để sau)

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
- [x] `PLAN.md` lỗi thời ("Bỏ unit test", mục "Rủi ro chưa kiểm chứng", Sort đọc số theo culture) —
  cập nhật theo test 89/89 + GUI Sandbox 11/11 + `CsvNumber` (2026-09-30)
