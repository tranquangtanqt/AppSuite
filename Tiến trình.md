# Tiến trình AppSuite

Danh sách việc cần làm / đã làm của toàn bộ AppSuite. Có vấn đề mới thì thêm 1 dòng `- [ ]` vào đúng
mục; làm xong thì đổi thành `- [x]` và ghi ngày hoàn thành. Chi tiết thiết kế của từng module vẫn nằm
trong `PLAN.md` / `README.md` của module đó — file này chỉ để theo dõi tiến độ.

Cập nhật lần cuối: 2026-09-29

## Chung (repo, build, deploy)

- [x] Mọi module đăng ký đủ ở `AppSuite.sln`, `modules.json`, `Sync-Modules-Dev.ps1`,
  `Publish-AppSuite.ps1`, README gốc; mọi project cùng .NET 8 / WindowsAppSDK 2.2.0 (rà 2026-09-29)
- [x] README gốc: sửa số project "9 project (MainLauncher + 8 module)" → 11 project (2026-09-29)
- [ ] `Modules/1.zip` (8 file Excel tài liệu nội bộ mcframe M7) đang bị commit và đã push — có vẻ nhầm.
  Gỡ khỏi repo (`git rm` + `.gitignore`); xoá khỏi lịch sử thì phải viết lại history + force-push
- [ ] Chưa có project unit test nào trong solution
- [ ] Cập nhật CodeGraph 1.4.1 → 1.6.0 (`codegraph upgrade`)

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
- [ ] Header bảng giữ nguyên khi scroll — CSS chưa có `position: sticky`, và khung `.table-scroll`
  đang `overflow-x: auto` nên thêm sticky thôi chưa đủ, phải sửa cả khung cuộn

## CsvEditor (ModuleF — `Note/ModuleF.txt`)

- [x] Các tính năng theo spec: mở/lưu CSV/TSV (tự nhận delimiter/encoding), sửa ô, thêm/xoá dòng/cột,
  đổi tên cột, Find/Replace, Filter, Sort nhiều cột, Statistics, Undo/Redo, context menu, validation
- [ ] Unit test theo spec: Open, Save, Search, Sort, Filter, Statistics, Undo, Redo
- [ ] Dọn 5 warning `WMC1506` trong `MainWindow.xaml` (vô hại — đã có `Bindings.Update()` thủ công)
