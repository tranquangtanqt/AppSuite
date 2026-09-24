# ScreenCapture

Ứng dụng WinUI 3 độc lập: công cụ chụp màn hình + chỉnh sửa ảnh, kiểu PicPick. Không có bất kỳ tham
chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới `Common` và `SharedUI`.

## Chức năng (Phase 1)

Cửa sổ chính (`CaptureLauncherWindow`) có 4 nút chụp:

- **Toàn màn hình** — chụp toàn bộ virtual screen (mọi màn hình).
- **Cửa sổ hiện tại** — chụp cửa sổ đang active (bất kỳ ứng dụng nào, kể cả app render bằng
  DirectX như trình duyệt).
- **Vùng chọn** — kéo-thả chọn 1 vùng màn hình, thả chuột là chụp ngay.
- **Vùng cố định** — chọn vùng, chỉnh lại kích thước qua 4 handle góc, nhấn Enter để chụp (Escape
  để huỷ). Vị trí/kích thước lần chụp gần nhất được nhớ lại trong phiên làm việc hiện tại.
- **Cuộn trang** — chưa làm (nút bị disable), xem `PLAN.md` mục "Chưa làm".

Sau khi chụp (bất kỳ mode nào), ảnh mở ngay trong cửa sổ chỉnh sửa (`EditorWindow`, tự maximize),
toolbar 1 hàng chia nhóm bằng icon (Segoe Fluent Icons):

- **Di chuyển** — chọn/kéo di chuyển 1 shape đã vẽ (viền chấm chấm đánh dấu shape đang chọn).
- **Vẽ**: Chữ nhật, Elip, Đường thẳng, Mũi tên, Highlight (marker tô trong mờ).
- **Text** — click vào canvas, nhập text qua dialog.
- **Tô màu** — bucket fill pixel thật (giống MS Paint/PicPick), click vào 1 vùng liền màu trên ảnh
  gốc để đổi màu cả vùng đó (thuật toán flood-fill, có ngưỡng tolerance cho vùng anti-alias nhẹ).
- **Stamps** — dán icon in sẵn lên ảnh: Number Stamps (hình tròn số 1-7 nhiều màu), General Stamps
  (mũi tên 8 hướng, bookmark, pin, flag, tag, info/warning/no-entry/heart/plus/minus/check/cross).
- **Cắt ảnh** (Crop).
- **Undo/Redo** từng bước.
- **Color1** (màu vẽ/tô chính) / **Color2** (màu fill/highlight) — color picker.
- **Size** — độ dày nét vẽ (slider 1-20px).
- **Lưu PNG** / **Copy** vào clipboard.

## Chạy độc lập

Mở `Modules\ScreenCapture\ScreenCapture.csproj` (double-click hoặc "Open Project" trong VS), đặt
làm Startup Project, F5. Không cần mở `MainLauncher` hay `AppSuite.sln`.

## Kiến trúc

- **Nguyên lý capture duy nhất**: mọi mode đều là chụp 1 rect từ desktop đã composite qua GDI
  `BitBlt` (`Services/CaptureService.cs`, P/Invoke trong `Services/Interop/NativeMethods.cs`) — module
  đầu tiên trong AppSuite dùng P/Invoke. Không dùng `Windows.Graphics.Capture` để tránh COM interop
  phức tạp không cần thiết.
- **Overlay chọn vùng** (`Views/RegionOverlayWindow`): freeze-then-select — chụp toàn màn hình trước,
  hiển thị ảnh tĩnh đó full-screen/topmost, cho chọn vùng trên ảnh tĩnh.
- **Editor** (`Views/EditorWindow` + `ViewModels/EditorViewModel`): canvas `SKXamlCanvas`
  (SkiaSharp.Views.WinUI). Undo/Redo tái dùng nguyên vẹn pattern `IEditCommand`/`UndoRedoStack` từ
  `Modules/CsvEditor/Commands`.
- Không có DI container, giống mọi module khác trong AppSuite — service khởi tạo thủ công trong
  code-behind của View.

Xem `PLAN.md` để biết đầy đủ quyết định thiết kế, rủi ro chưa kiểm chứng bằng chạy thực tế, và danh
sách việc chưa làm (Scroll capture, blur/mosaic, v.v.).
