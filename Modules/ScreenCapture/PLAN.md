# ScreenCapture: Công cụ chụp màn hình + chỉnh sửa ảnh (PicPick-like) cho AppSuite

## Bối cảnh

AppSuite chưa có công cụ chụp màn hình. Người dùng muốn 1 tool kiểu PicPick: chụp màn hình theo
nhiều chế độ rồi mở ngay 1 cửa sổ chỉnh sửa đơn giản. Theo đúng luật kiến trúc repo, đây là 1 module
mới độc lập tên `ScreenCapture` (PascalCase — tool thuần theo quy ước đặt tên module ở README gốc),
chỉ `ProjectReference` tới `Common` + `SharedUI`, không bao giờ reference `MainLauncher`.

Phạm vi Phase 1 đã chốt với người dùng:
- 4 mode chụp: **Full-screen**, **Window** (cửa sổ đang active), **Region** (kéo-thả chọn vùng),
  **Fixed Region** (chọn vùng, chỉnh handle, Enter để chụp).
- **Scroll capture** (tự cuộn + ghép ảnh) để sau — không nằm trong Phase 1.
- Sau khi chụp, ảnh mở ngay ra cửa sổ chỉnh sửa (không lưu thẳng ra file).

## Thiết kế

### Nguyên lý capture duy nhất

Cả 4 mode quy về 1 nguyên lý: chụp 1 rect từ desktop đã composite (`BitBlt` từ desktop DC qua GDI
P/Invoke — `Services/Interop/NativeMethods.cs`, `Services/CaptureService.cs`). Không dùng
`Windows.Graphics.Capture` (COM interop phức tạp không cần thiết) vì DWM luôn composite output cuối
cùng vào 1 buffer bất kể công nghệ render bên dưới (GDI/DirectX/...).

- **Full-screen**: `GetSystemMetrics(SM_*VIRTUALSCREEN)` — toàn bộ virtual screen (mọi màn hình).
- **Window**: `GetForegroundWindow` → `DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)` (fallback
  `GetWindowRect`). Giới hạn MVP: chỉ chụp cửa sổ đang active, không có UI chọn cửa sổ khác.
- **Region/Fixed Region**: overlay chọn vùng kiểu freeze-then-select (`Views/RegionOverlayWindow`) —
  chụp toàn màn hình trước, hiển thị ảnh tĩnh đó full-screen/topmost qua `AppWindow` +
  `OverlappedPresenter`, cho kéo-thả chọn vùng trên ảnh tĩnh (tránh overlay trong suốt trên desktop
  sống, dễ vỡ trong WinUI3). Fixed Region: sau khi thả chuột, chuyển sang trạng thái chỉnh 4 handle
  góc, Enter xác nhận, Escape huỷ. Vị trí/kích thước lần chụp gần nhất chỉ nhớ trong phiên làm việc
  hiện tại (session-only — **giả định mặc định, chưa lưu ra đĩa**).
- **Scroll**: chưa làm, chỉ có `CaptureMode.Scroll` trong enum làm điểm mở rộng.

### Editor + Undo/Redo

`Views/EditorWindow` + `ViewModels/EditorViewModel`: canvas `SKXamlCanvas` (từ
`SkiaSharp.Views.WinUI`) vẽ bitmap gốc + từng `AnnotationShape` (Rectangle/Arrow/Text) đè lên. Tái
dùng nguyên vẹn `IEditCommand`/`UndoRedoStack`/`CompositeEditCommand` từ `Modules/CsvEditor/Commands`
(chỉ đổi namespace) cho undo/redo. Crop xử lý destructive (thay bitmap, có lưu bitmap cũ để undo) —
đơn giản hơn crop non-destructive nhưng vẫn undo được đầy đủ.

### Không dùng DI container

Giống mọi module khác trong AppSuite: không có DI container, ViewModel/Service khởi tạo thủ công
trong code-behind của View (View sở hữu việc mở cửa sổ mới — `CaptureLauncherWindow` mở
`RegionOverlayWindow`/`EditorWindow` trực tiếp, không qua ViewModel).

## Kiểm chứng

- `dotnet build Modules\ScreenCapture\ScreenCapture.csproj -p:Platform=x64` — build sạch (đã chạy
  thành công).
- `dotnet build AppSuite.sln` — không ảnh hưởng 11 project còn lại (đã chạy thành công, 0 lỗi).
- **Chưa chạy thử GUI thực tế đầy đủ** — các rủi ro sau cần người dùng tự kiểm chứng khi chạy thật:
  - ✅ **Đã xác nhận và sửa** (phản hồi thực tế từ người dùng dùng máy 2 màn hình): overlay Region/
    Fixed Region bị phóng to khi màn hình chạy DPI scale >100%. Nguyên nhân: `BackgroundImage`
    (`Stretch="None"`) hiển thị bitmap capture (kích thước theo device pixel) nhưng WinUI layout tính
    theo DIP (logical pixel) — ảnh bị vẽ to hơn màn hình thật đúng bằng hệ số scale. Đã sửa: đổi
    `Stretch="Fill"` + set `Width`/`Height` của ảnh bằng `virtualRect.Width/Height` chia cho
    `XamlRoot.RasterizationScale` ngay khi overlay activate (`RegionOverlayWindow.xaml.cs`).
  - `SKXamlCanvas` (SkiaSharp.Views.WinUI) có render đúng trong project unpackaged này không.
  - `AppWindow`/`OverlappedPresenter` với `IsAlwaysOnTop` trên máy đa màn hình DPI khác nhau.
  - `DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)` trên các loại cửa sổ khác nhau (browser, Electron,
    WPF, WinUI).
  - Copy ảnh vào clipboard trên app unpackaged (`Windows.ApplicationModel.DataTransfer.Clipboard`) —
    nếu lỗi, cần chuyển sang GDI clipboard thuần (`OpenClipboard`/`SetClipboardData(CF_DIB)`).
  - Delay 200ms sau `SetForegroundWindow`/minimize launcher có đủ cho các app khác nhau repaint xong
    trước khi chụp không — con số này là ước lượng, cần tinh chỉnh thực tế.

## Chưa làm (fast-follow)

- Scroll capture (tự cuộn + ghép ảnh dài/rộng) — `CaptureMode.Scroll` đã có sẵn làm điểm mở rộng.
- Editor: Blur/Mosaic, thêm shape (Ellipse, Highlight, Numbered step, Freehand pen), crop không phá
  huỷ, resize/scale annotation qua handle sau khi vẽ xong.
- Fixed Region: lưu vị trí/kích thước qua lần restart app (hiện chỉ session-only, mất khi đóng app).
- Window capture: chọn cửa sổ khác ngoài foreground window (cần `EnumWindows` + UI danh sách chọn).
- Hotkey toàn cục (`RegisterHotKey`) để kích hoạt capture từ bên ngoài app — hiện chỉ mở được từ
  `CaptureLauncherWindow`.
- Logging: module chưa wire `Common.Logging` (không module nào khác trong repo hiện dùng logging
  ngoài MainLauncher) — cân nhắc thêm sau vì module có nhiều edge case Win32 khó debug.
