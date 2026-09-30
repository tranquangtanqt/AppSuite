# Tiến trình AppSuite

Danh sách việc cần làm / đã làm của toàn bộ AppSuite. Có vấn đề mới thì thêm 1 dòng `- [ ]` vào đúng
mục; làm xong thì đổi thành `- [x]` và ghi ngày hoàn thành. Chi tiết thiết kế của từng module vẫn nằm
trong `PLAN.md` / `README.md` của module đó — file này chỉ để theo dõi tiến độ.

Cập nhật lần cuối: 2026-09-30

## Chung (repo, build, deploy)

- [x] Mọi module đăng ký đủ ở `AppSuite.sln`, `modules.json`, `Sync-Modules-Dev.ps1`,
  `Publish-AppSuite.ps1`, README gốc; mọi project cùng .NET 8 / WindowsAppSDK 2.2.0 (rà 2026-09-29)
- [x] README gốc: sửa số project "9 project (MainLauncher + 8 module)" → 11 project (2026-09-29)
- [x] Có project unit test đầu tiên: `Tests\CsvEditor.Tests` (đã thêm vào `AppSuite.sln`) (2026-09-29)
- [x] Unit test cho engine ImageCompare (40) + parser Excel của Mcf.CrudDiagram (21), Mcf.DbDef (11), Mcf.Screen (29)
  — `Tests\*.Tests`, đã thêm vào `AppSuite.sln`, tất cả đạt trên máy dev; build sln 0 warning (2026-09-30)
- [ ] Chạy 4 project test mới trong Windows Sandbox (publish self-contained như CsvEditor.Tests)
- [ ] Unit test còn thiếu: Rdbms.HtmlGenerator (cần PostgreSQL/Oracle thật hoặc tách phần dựng HTML), ModuleB/C,
  ScreenCapture (logic chỉnh ảnh / phiên làm việc)
- [ ] Cập nhật CodeGraph 1.4.1 → 1.6.0 (`codegraph upgrade`)
- [x] Build cả `AppSuite.sln` (x64, `--no-incremental`): 0 warning, 0 error (rà 2026-09-30)
- [ ] File này chưa có mục cho ModuleA/B, Mcf.DbDef, Mcf.Screen, Rdbms.HtmlGenerator — rà PLAN.md của
  chúng (2026-09-30) không thấy việc nào còn dở

**Lưu ý vận hành**: chạy MainLauncher ở cấu hình nào (Debug/Release) thì phải chạy
`.\build\Sync-Modules-Dev.ps1 -Configuration <cấu hình đó>` để chép module vào cạnh launcher — thiếu
bước này thì Start module nào cũng lỗi "Executable not found" (đã gặp 2026-09-29).

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
