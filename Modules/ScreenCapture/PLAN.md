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
`SkiaSharp.Views.WinUI`) vẽ bitmap gốc + từng `AnnotationShape` (Rectangle/Ellipse/Line/Arrow/
Highlight/Text/Stamp) đè lên. Tái dùng nguyên vẹn `IEditCommand`/`UndoRedoStack`/`CompositeEditCommand`
từ `Modules/CsvEditor/Commands` (chỉ đổi namespace) cho undo/redo. Crop xử lý destructive (thay
bitmap, có lưu bitmap cũ để undo) — đơn giản hơn crop non-destructive nhưng vẫn undo được đầy đủ.

**Toolbar nâng cấp** (theo yêu cầu "giống PicPick hơn"): 1 hàng, icon Segoe Fluent Icons + label nhỏ,
chia nhóm bằng `AppBarSeparator`. Thêm:
- **Move**: hit-test `Annotations` (duyệt ngược, shape vẽ sau nằm trên) khi click, kéo cập nhật
  `Bounds` trực tiếp, chỉ tạo 1 `MoveResizeAnnotationCommand` (đã có sẵn, không cần command mới) lúc
  thả chuột — không ghi 1 command mỗi frame kéo.
- **Fill**: `Commands/FloodFillCommand.cs` — flood-fill pixel thật (không phải shape tô đặc), thuật
  toán scanline iterative trên raw buffer `SKBitmap.GetPixels()` (không đệ quy — tránh stack overflow
  ảnh lớn; không `GetPixel`/`SetPixel` per-pixel — quá chậm), so màu theo tolerance (mặc định 30/255)
  để tô được vùng có anti-alias nhẹ.
- **Stamps**: `Models/StampAnnotation.cs` — Number stamps vẽ trực tiếp (circle + text, không cần
  font icon); General stamps vẽ bằng `SKTypeface.FromFamilyName("Segoe Fluent Icons")` + glyph Unicode
  (cache `static readonly SKTypeface`, không tạo mới mỗi lần `Render()` vì canvas invalidate liên tục
  lúc kéo shape khác). Glyph tra từ Microsoft Docs Segoe Fluent Icons cheat sheet — 4 mũi tên chéo
  (UpLeft/UpRight/DownLeft/DownRight) không có glyph riêng, dùng lại glyph ArrowUp xoay 45°/135°/
  225°/315° qua `canvas.RotateDegrees` thay vì đoán mã glyph không chắc chắn.
- **Color1/Color2/Size**: tái dùng `SharedUI.Controls.InlineColorPicker` có sẵn (2 instance, Header
  "Color1"/"Color2") — cần merge thêm `SharedUI/Themes/Generic.xaml` vào `App.xaml` (trước đó module
  chỉ merge `CompactStyles.xaml`, thiếu bước bắt buộc để control custom của SharedUI chạy đúng trong
  app unpackaged). Size là `Slider` bind `EditorViewModel.StrokeWidth`. Mọi shape mới tạo đọc 3 giá
  trị `StrokeColor`/`FillColor`/`StrokeWidth` từ ViewModel thay vì hard-code.

**Lưu ý kỹ thuật XAML gặp phải lúc code**: `Window` (WinUI3) không có property `Resources` như WPF —
phải đặt `<Grid.Resources>` trên root Grid. `ToggleButton` không có property `Flyout` (chỉ `Button`
mới có) — nút Stamps phải dùng `Button` thường thay vì `ToggleButton`, nên không tham gia vào nhóm
"bật/tắt" icon như các tool khác (chấp nhận được, không ảnh hưởng chức năng).

**Bug thực tế gặp phải (đã xác nhận qua debug VS, đã sửa):**
1. `SharedUI.Controls.InlineColorPicker` chưa từng được dùng ở đâu khác trong repo (control lần đầu
   thực sự được sử dụng) — nghi vấn ban đầu, đã chủ động thay bằng `Button` + `Flyout` chứa
   `ColorPicker` chuẩn WinUI cho Color1/Color2 để giảm rủi ro, dù chưa xác nhận InlineColorPicker
   có thực sự lỗi hay không.
2. **Nguyên nhân crash thật** (xác nhận qua Visual Studio debugger): `<Slider Minimum="1" Maximum="20"
   Value="3" .../>` khai báo trực tiếp trong XAML ném `XamlParseException` lúc `InitializeComponent()`
   ("Failed to assign to property RangeBase.Minimum"). Đồng thời, việc set `Value="3"` qua XAML kích
   hoạt `ValueChanged` ngay trong lúc `InitializeComponent()` chạy — tại thời điểm đó `_viewModel`
   (field của `EditorWindow`) **chưa được gán** (constructor gán `_viewModel` sau khi gọi
   `InitializeComponent()`), nên handler `SizeSlider_ValueChanged` ném `NullReferenceException` khi
   truy cập `_viewModel.StrokeWidth`. Sửa: bỏ `Minimum`/`Maximum`/`Value` khỏi XAML, gán qua
   code-behind **sau khi** `_viewModel` đã được khởi tạo.
3. `RegionOverlayWindow` set `presenter.IsAlwaysOnTop = true` khiến overlay full-screen đè lên cả
   breakpoint/exception dialog của Visual Studio lúc debug, không Alt+Tab sang được. **Đang tạm tắt**
   (`IsAlwaysOnTop = false`) để debug dễ hơn — **cần bật lại** sau khi xác nhận không còn bug nào khác
   (xem `Views/RegionOverlayWindow.xaml.cs`, có comment TODO tại chỗ này).

### Editor: ribbon 2 tab (File/Home) thay cho toolbar 1 hàng

Người dùng gửi ảnh ribbon PicPick (nhiều tab: File/Home/Share/View) và muốn "đẹp giống vậy" — đảo lại
quyết định trước đó ("không cần nhiều tab"). Đã hỏi lại và chốt: **chỉ làm 2 tab File + Home**, không
thêm tab Share/View rỗng vì ScreenCapture chưa có tính năng upload/email/zoom tương ứng (làm tab
không dùng được là để dở dang).

- **Tab header**: 2 `ToggleButton` thường (`HomeTabHeader`/`FileTabHeader`), tự quản lý `IsChecked`
  qua code-behind (`RibbonTabHeader_Click`) giống cách `ToolButtons` đã làm — không cần
  `RadioButton`/`ControlTemplate` riêng, dùng nền checked mặc định của theme làm chỉ báo tab active.
- **Ribbon content**: mỗi tab là 1 `StackPanel` riêng (`HomeRibbonPanel`/`FileRibbonPanel`), toggle
  `Visibility` khi đổi tab. Mỗi nhóm nút bọc trong `Border` (`RibbonGroupStyle`: viền + bo góc) kèm
  `TextBlock` nhãn nhóm nhỏ phía dưới (Chọn/Vẽ hình/Tô & Dấu/Cắt & Sửa/Màu/Cỡ nét ở Home; Lưu/Cửa sổ
  ở File) — giống cách PicPick ghi nhãn dưới mỗi cụm icon.
- Toàn bộ event handler cũ (Move/Shape/Fill/Stamp/Color/Size/Undo/Redo/Save/Copy) giữ nguyên 100%,
  chỉ di chuyển vị trí control trong cây XAML — không phát sinh logic mới ngoài 1 nút "Đóng" ở tab
  File (`CloseButton_Click` → `this.Close()`).

### Editor: tab contextual "Number Stamp" khi chọn 1 stamp số (giống PicPick)

Chọn 1 Number Stamp đã đặt (Move tool) tự động chuyển ribbon sang tab thứ 3 "Number Stamp"
(`Views/EditorWindow.xaml.cs` → `UpdateNumberStampTab()`, gọi từ handler `PropertyChanged` của
`SelectedAnnotation`), có 5 nhóm giống PicPick: Edit (Flatten/Delete), Shape Styles (7 preset màu,
tái dùng đúng `NumberStamps` ObservableCollection của flyout Stamps), Stamp Format (2 `NumberBox`
Current/Next), Shape Colors (Outline/Fill tách biệt), Arrange (Bring to Front/Send to Back). Bỏ tab
đi tự động khi bỏ chọn hoặc chọn shape khác.

- **`StampAnnotation`**: `NumberValue` đổi `init`→`set` (sửa được sau khi đặt), thêm `OutlineColor`
  (mặc định trắng, tách khỏi `Color` kế thừa nay chỉ còn nghĩa Fill) — đúng gap khiến trước đó không
  đổi được riêng viền/số.
- **3 command mới** theo đúng pattern `ChangeAnnotationStyleCommand`/`CropCommand`:
  `ChangeStampColorsCommand` (Fill+Outline cùng lúc), `ChangeStampNumberCommand`,
  `FlattenAnnotationCommand` (rasterize 1 shape vào bitmap nền + bỏ khỏi Annotations, undo chèn lại
  đúng layer index cũ).
- **`NumberBox` Minimum/SmallChange gán qua code-behind, không qua XAML attribute** — áp dụng lại bài
  học từ lỗi `Slider.Minimum` (`XamlParseException`) đã gặp ở mục toolbar trước đó, vì `NumberBox`
  cùng họ range-control dễ dính lỗi tương tự.
- **"Next" (số cho stamp kế tiếp)** không qua ViewModel/UndoRedo — vẫn là field
  `_numberStampCounter` trong `EditorWindow` như cũ (trạng thái công cụ phiên làm việc, không phải
  nội dung tài liệu).

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
- Editor: Blur/Mosaic (khác Fill — làm mờ/che chứ không đổi màu), Freehand pen, crop không phá huỷ,
  resize/scale annotation qua handle sau khi vẽ xong (Move hiện chỉ di chuyển, chưa resize lại).
- Fixed Region: lưu vị trí/kích thước qua lần restart app (hiện chỉ session-only, mất khi đóng app).
- Window capture: chọn cửa sổ khác ngoài foreground window (cần `EnumWindows` + UI danh sách chọn).
- Hotkey toàn cục (`RegisterHotKey`) để kích hoạt capture từ bên ngoài app — hiện chỉ mở được từ
  `CaptureLauncherWindow`.
- Logging: module chưa wire `Common.Logging` (không module nào khác trong repo hiện dùng logging
  ngoài MainLauncher) — cân nhắc thêm sau vì module có nhiều edge case Win32 khó debug.
