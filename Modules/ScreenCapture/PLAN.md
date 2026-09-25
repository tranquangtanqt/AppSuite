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
   breakpoint/exception dialog của Visual Studio lúc debug, không Alt+Tab sang được. ✅ Đã xử lý
   (2026-09-24): `IsAlwaysOnTop = !Debugger.IsAttached` — xem mục "Tinh chỉnh sau phản hồi".

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

### Tinh chỉnh sau phản hồi người dùng (2026-09-24)

- **Màu Number Stamp mặc định `#D86445`** (thay `Colors.Crimson`) — áp cho ô màu đầu tiên của
  `NumberStamps`, màu preview General Stamps trong flyout, và swatch Fill mặc định ở tab Number Stamp.
  General Stamps khi đặt vẫn lấy Color1 hiện tại, màu flyout chỉ là preview.
- **Viền Number Stamp**: trước là trắng alpha 60 + dày `radius*0.06` → gần như không thấy. Nay viền
  đục, dày `radius*0.12` (`StampAnnotation.RenderNumber`); ô chọn màu trong flyout/ribbon cũng có
  viền trắng cho khớp.
- **Stamp Format Current/Next**: bỏ spin button `Inline` của `NumberBox` (Up nằm trước Down — ngược
  thói quen trái=giảm/phải=tăng — và chèn lên chữ số khi ô hẹp). Thay bằng `SpinButtonPlacementMode=
  "Hidden"` + 2 `Button` `−`/`+` (`StepperButtonStyle`, cao 32px bằng nút Outline/Fill để nhóm không
  lệch). Nút chỉ gán `NumberBox.Value` → đi qua đúng `ValueChanged` cũ (có command/undo), không
  xuống dưới `Minimum`.
- **Launcher làm lại giao diện**: thay 5 `Button` text trơn bằng header (icon + tiêu đề + mô tả) và
  lưới thẻ 2 cột (icon badge + tên + mô tả), thẻ Cuộn trang disable + nhãn "Sắp có". `StatusText` →
  `InfoBar` (chỉ hiện khi có thông báo). Cửa sổ `720×500` DIP, nhân theo DPI qua `GetDpiForWindow`
  (P/Invoke mới trong `NativeMethods`) vì `AppWindow.Resize` nhận pixel vật lý.
- **Mũi tên luôn chỉ hướng xuống-phải (bug)**: `MakeRect` chuẩn hoá rect khi vẽ và `ResizeFromHandle`
  chuẩn hoá khi kéo góc, trong khi `LineArrowAnnotation` dùng Left/Top làm điểm đầu, Right/Bottom làm
  đầu mũi tên → mọi mũi tên bị lật về xuống-phải, kéo góc không đổi được hướng. Sửa:
  - Line/Arrow lưu `Bounds` **không chuẩn hoá** (`LineArrowAnnotation.FromPoints`). Thêm
    `AnnotationShape.NormalizedBounds` cho chỗ cần khung thật (vẽ vùng chọn, handle góc, crop, kiểm
    tra kích thước tối thiểu lúc thả chuột).
  - Hit-test chuyển thành `virtual AnnotationShape.HitTest`; Line/Arrow override theo khoảng cách tới
    đoạn thẳng (khung bao của đường chéo chiếm vùng trống lớn, che shape bên dưới).
  - Move tool với Line/Arrow: 2 handle tròn ở 2 đầu thay cho khung + 4 góc. Bấm gần 1 đầu (bán kính
    `clamp(độ dài/3, HandleSize, 30px)`) → kéo đầu đó (`_lineEndpointHandle`), kể cả khi shape chưa
    được chọn; bấm khúc giữa → di chuyển cả đường. Vẫn ghi 1 `MoveResizeAnnotationCommand` lúc thả.
  - Kèm theo: `CropCommand` inflate 1px trước `Intersect` — đường ngang/dọc có khung cao/rộng 0 từng
    bị xoá nhầm khi crop.
- **Giữ `Shift` để khoá góc/tỉ lệ** (đọc `PointerRoutedEventArgs.KeyModifiers` trong
  `Canvas_PointerMoved`, nên nhấn/nhả Shift giữa chừng có tác dụng ngay ở lần rê chuột kế tiếp):
  - Line/Arrow (vẽ mới + kéo đầu mút): `SnapToAngle` bắt hướng về bội số 45° tính từ đầu đứng yên,
    độ dài = hình chiếu của chuột lên hướng đã bắt (giống PowerPoint).
  - Chữ nhật/Elip (vẽ mới + kéo handle góc): `SnapToSquare` — cạnh = chiều dài hơn, neo ở điểm bắt
    đầu / góc đối diện handle. Highlight và Crop không áp dụng (không có nhu cầu vùng vuông).
- **Phím tắt Editor**: `Ctrl+Z` Undo, `Ctrl+Y`/`Ctrl+Shift+Z` Redo, `Ctrl+S` Lưu, `Ctrl+C` Copy —
  thêm vào `Content_KeyDown` sẵn có (không dùng `KeyboardAccelerator` trên nút vì nút Lưu/Copy nằm ở
  tab Tệp bị `Collapsed`, accelerator của element collapsed không chạy). Gọi qua `ICommand` của
  ViewModel, có kiểm `CanExecute` (Undo/Redo khi stack rỗng, Save/Copy async đang chạy). Handler gắn
  bằng `+=` nên chỉ nhận phím control đang focus chưa xử lý → TextBox trong NumberBox vẫn giữ
  Ctrl+Z/Ctrl+C/Backspace của nó. Tooltip trên nút ghi phím tắt tương ứng.
- **Chọn/chỉnh sửa trực tiếp, không cần tool "Di chuyển"** (yêu cầu: vẽ xong là chỉnh sửa được
  luôn, bấm vùng trống mới thoát, bấm vào shape nào là sửa shape đó):
  - `Canvas_PointerPressed` gọi `TryBeginEditExisting` trước với mọi tool (trừ Fill): trúng handle
    của shape đang chọn → resize/kéo đầu mút; trúng shape bất kỳ → chọn + kéo. Không trúng → bỏ chọn
    rồi mới chạy tool (vẽ draft / đặt stamp / text). Tool Move giờ chỉ còn nghĩa "không vẽ gì".
  - Vẽ xong (`PointerReleased`), đặt stamp, thêm text → `SelectedAnnotation = shape mới` → handle +
    tab contextual hiện ngay.
  - Text + Stamp: nếu đang có selection, bấm vùng trống chỉ bỏ chọn; lần bấm sau mới bật
    `ContentDialog` / đặt stamp. Theo yêu cầu người dùng cho Stamp: bấm → stamp 1 + tab Number Stamp →
    bấm ra ngoài thoát sửa → bấm → stamp 2... Tool Stamp giữ tới khi chọn tool khác (`SelectTool`).
  - Kích thước stamp kế tiếp nhớ theo stamp vừa resize: field `_stampSize` (mặc định
    `DefaultStampSize = 32`) cập nhật lúc thả chuột sau khi kéo handle góc của 1 `StampAnnotation`;
    reset về mặc định trong `StampItem_Click`/`NumberStampItem_Click` (chọn lại từ flyout = đặt từ
    đầu). Resize stamp luôn khoá vuông (dùng chung nhánh `SnapToSquare`) vì stamp vẽ theo cạnh ngắn
    hơn — khung méo làm handle lệch khỏi hình. Undo resize không trả `_stampSize` về cũ (trạng thái
    công cụ, giống `_numberStampCounter`).
  - `RectangleAnnotation`/`EllipseAnnotation` override `HitTest` chỉ trúng **viền** — nếu bắt cả
    khung thì không vẽ được gì bên trong 1 khung lớn.
  - Bấm chọn mà không kéo → không ghi `MoveResizeAnnotationCommand` rỗng vào lịch sử Undo.
  - `EditorViewModel`: khi Undo/Redo làm shape đang chọn biến khỏi `Annotations` → tự bỏ chọn.
  - `Esc` bỏ chọn.
  - Hệ quả cần biết: vì shape vừa vẽ đang được chọn, đổi Color1/Size ngay sau khi vẽ sẽ đổi shape đó
    (giống PicPick); muốn đổi cho shape kế tiếp thì bấm vùng trống/`Esc` trước.
- **Tool Move kiểu PicPick + đổi kích thước khung ảnh**:
  - Nút đổi tên "Move", icon con trỏ (`PathIcon`), là tool mặc định khi mở Editor (trước là Chữ nhật).
  - Canvas có lề `CanvasPad = 16px` quanh ảnh; mọi thứ vẽ sau `canvas.Translate(_viewOrigin)` và
    `ToCanvasPoint` trừ `_viewOrigin` → toạ độ pointer vẫn là toạ độ pixel ảnh. Kích thước
    `SKXamlCanvas` tính lại trong `UpdateCanvasLayout()` = (ảnh hoặc khung đang kéo + 2×lề) / scale —
    sửa luôn lỗi cũ: trước đặt `Canvas.Width = bitmap.Width` (DIP) nên ở DPI 150% canvas rộng hơn ảnh
    1.5 lần, thừa vùng trống.
  - Tool Move + không chọn shape → vẽ 8 handle (TL,T,TR,R,BR,B,BL,L). Kéo handle: delta lấy theo toạ
    độ **cửa sổ** (`GetCurrentPoint(null)`), không theo Canvas — vì khung mở rộng sang trái/lên trên làm
    Canvas lớn ra và dời ảnh, toạ độ theo Canvas sẽ nhảy theo gây giật. Xem trước bằng khung nét đứt.
  - Thả chuột → `EditorViewModel.ResizeCanvas(SKRectI)` → `ResizeCanvasCommand` mới: bitmap mới nền
    trắng, vẽ bitmap cũ lệch (−Left, −Top), dịch mọi annotation cùng offset (undo dịch ngược).
  - **Bug cũ phát hiện kèm**: `CropCommand` không dịch annotation theo vùng cắt → shape lệch khỏi nội
    dung sau khi cắt. Đã sửa dùng chung `ResizeCanvasCommand.OffsetAll`.
  - Chưa có: con trỏ chuột đổi thành mũi tên resize khi rê lên handle (`ProtectedCursor` của
    `SKXamlCanvas` là protected, cần subclass).
- **Tool Select (chọn vùng) + tab contextual "Vùng chọn"**:
  - `CaptureTool.Select`, nút *Select* cạnh *Move*. Bấm-kéo → `_regionDrag` (kẹp trong khung ảnh,
    `Shift` = vuông); thả → `_region` (`SKRectI`, bỏ nếu < 2px). Vẽ viền "kiến bò" (trắng liền + đen
    đứt). Xử lý trước `TryBeginEditExisting` → tool Select không chọn/kéo shape.
  - `SetRegion()` bật/tắt tab "Vùng chọn" (cùng cơ chế tab Number Stamp). Vùng tự bỏ khi đổi tool,
    bật Cắt, hoặc `Bitmap` đổi (cắt/xoá vùng/đổi khung/undo...) vì toạ độ cũ không còn đúng.
  - Thao tác: *Cắt ảnh* → `EditorViewModel.Crop` sẵn có; *Copy* → `CopyRegionAsync` (cắt từ
    `RenderComposited()` nên có cả shape); *Xoá vùng* → `EraseRegionCommand` mới (tô trắng pixel ảnh
    nền, không đụng shape); *Cut* = Copy + Xoá vùng.
  - Phím khi có vùng chọn được ưu tiên: `Ctrl+C` copy vùng (thay vì cả ảnh), `Ctrl+X`, `Delete`,
    `Enter` (cắt ảnh), `Esc`.
  - Chỉnh vùng đã chọn: 8 handle (dùng chung `CanvasHandlePoints`/`HitTestPoints` với handle khung
    ảnh) + kéo bên trong = di chuyển (`_regionHandle`: 0..7 / 8 = `RegionMoveHandle`). Di chuyển bị
    kẹp trong ảnh; chỉnh quá nhỏ (< 2px) thì giữ vùng cũ.
- **Dán ảnh (`Ctrl+V` / nút *Dán* ở nhóm Cắt & Sửa)**:
  - `IClipboardService.GetBitmapAsync()`: đọc `StandardDataFormats.Bitmap`, fallback
    `StorageItems` (file ảnh copy trong Explorer). **Đã chạy thử thật**: đọc clipboard WinRT trong app
    unpackaged hoạt động (ảnh test 300×160 dán đúng, status "Đã dán ảnh...").
  - Model mới `ImageAnnotation` (vẽ bitmap co giãn vừa `Bounds`) → dùng lại toàn bộ cơ chế shape:
    chọn/kéo/handle góc/đổi lớp/Flatten/Undo. Giữ `Shift` khi kéo góc = giữ tỉ lệ gốc (`SnapToAspect`).
  - Vị trí: góc trên-trái vùng chọn nếu có, không thì góc trên-trái phần đang nhìn thấy
    (`CanvasScroller` offset × scale − `_viewOrigin`). Dán xong chuyển tool Move + chọn sẵn ảnh.
  - Ảnh vượt khung → `CompositeEditCommand` [ResizeCanvas (nới phải/dưới, offset 0) + AddAnnotation]
    = 1 bước Undo.
- **Nhiều ảnh chụp dạng tab trong 1 Editor** (yêu cầu: "các lần chụp được lưu lại như PicPick, không
  mất đi"):
  - `CaptureLauncherWindow` giữ 1 `_editor` duy nhất: lần đầu tạo, các lần sau gọi
    `EditorWindow.AddCapture(bitmap)`; `Closed` → null.
  - `EditorWindow`: `TabView` (Row 2, chỉ dùng làm thanh tab — không có content) mỗi tab `Tag` = 1
    `EditorViewModel` riêng (bitmap, annotation, UndoRedo riêng). `_viewModel` hết `readonly`;
    `SwitchTo(vm)` gỡ/gắn `RequestRedraw`/`PropertyChanged`, mang Tool/Color1/Color2/Size từ ảnh trước
    sang (thiết lập của cửa sổ, không phải của ảnh), reset mọi trạng thái kéo dở + vùng chọn, tính lại
    layout canvas, cuộn về (0,0). Tên tab = `yyyy-MM-dd HH mm ss`, trùng giây thì thêm "(2)".
  - `EditorViewModel.NeedsSave` = chưa từng lưu ra file **hoặc** `UndoRedo.IsDirty`. `SaveToFileAsync()`
    trả bool + `MarkClean()` (trước đây Save không đánh dấu clean).
  - Đóng tab → `ConfirmCloseDocumentAsync` (Lưu / Không lưu / Huỷ); đóng tab cuối = đóng cửa sổ.
    Đóng cửa sổ: `AppWindow.Closing` (nút X) + nút *Đóng* → `TryCloseWindowAsync` hỏi 1 lần nếu còn ảnh
    chưa lưu (`_forceClose` để lần `Close()` thật không bị chặn lại).
  - **Bug phát hiện khi test**: lần chụp thứ 2 trở đi dính cả cửa sổ Editor vào ảnh (chỉ launcher được
    thu nhỏ). Sửa: `MinimizeForCapture` thu nhỏ cả Editor; huỷ chụp → `RestoreEditorAfterCancel`; chụp
    xong `AddCapture` tự mở lại. Đã chạy thử 2 lần chụp liên tiếp qua UI Automation: 2 tab đúng tên, ảnh
    thứ 2 không còn dính Editor.
  - Chưa có: kéo đổi thứ tự tab.
- **Nhớ tab qua lần tắt/mở app** (người dùng chọn: shape vẫn chỉnh sửa được, thư mục `%TEMP%`):
  - `Services/SessionService.cs`: thư mục `Path.GetTempPath()\AppSuite\ScreenCapture\Session\`.
    `session.json` (Version, ActiveId, Tabs[Id, Title, SavedToFile, Image, Shapes[Type, 4 cạnh Bounds
    giữ nguyên thứ tự, Color, StrokeWidth + riêng từng loại: IsArrow / Text+FontSize / StampKind+
    NumberValue+OutlineColor / Image]]) + PNG `{tabId}_{guid}.png` cho ảnh nền và ảnh dán.
  - Mỗi bitmap 1 file (map qua `ConditionalWeakTable<SKBitmap,string>`) → ảnh không đổi thì không
    encode lại; bitmap trong app bất biến (mọi thao tác pixel tạo bitmap mới) nên map này đúng.
  - `Save`: giữ tối đa 30 tab mới nhất, cắt tiếp theo 300 MB (luôn giữ ≥1 tab mới nhất), ghi manifest
    qua file `.tmp` rồi `File.Move` (không bị hỏng nửa chừng), rồi **xoá mọi file không được tham
    chiếu** → thư mục không bao giờ tích luỹ ảnh của tab đã đóng / phiên cũ.
  - Ghi tạm khi: `AddCapture`, đóng tab, đóng cửa sổ. Đóng cửa sổ không còn hỏi (vì có khôi phục); chỉ
    hỏi nếu ghi tạm lỗi mà còn ảnh chưa lưu. Đóng tab vẫn hỏi Lưu/Không lưu/Huỷ (đóng tab = bỏ ảnh).
  - `CaptureLauncherWindow`: mở app → `Load()` → còn tab thì mở Editor ngay; chụp khi Editor chưa mở
    cũng nạp phiên cũ trước rồi thêm tab mới (không ghi đè mất tab cũ).
  - `EditorViewModel`: `Id` (Guid), `SavedToFile`, `RestoreFromSession(shapes, savedToFile)` (nạp shape
    không qua Undo).
  - **Đã chạy thử**: chụp 2 lần + dán 1 ảnh → đóng Editor bằng X → thư mục có đúng 2 PNG nền (~359 KB
    mỗi ảnh full-screen) + 1 PNG ảnh dán + session.json; mở lại app → Editor tự mở với đúng 2 tab, tab
    đang xem được chọn lại.
  - Chưa có: autosave định kỳ khi đang sửa (sửa xong mà app bị kill trước khi chụp/đóng tab/đóng cửa
    sổ thì mất phần sửa đó, ảnh gốc vẫn còn); bộ đếm Number Stamp không khôi phục theo số lớn nhất.
- **Nút "Đóng tất cả" + Lưu tất cả vào 1 thư mục**:
  - Nút ở `TabView.TabStripFooter`. `CloseAllTabsAsync`: có ảnh `NeedsSave` → ContentDialog *Lưu tất
    cả...* / *Đóng không lưu* / *Huỷ*. Lưu tất cả: `IImageFileService.PickFolderAsync` (`FolderPicker` +
    `InitializeWithWindow`) → `EditorViewModel.SaveToFolder` từng ảnh chưa lưu →
    `SavePngToFolder` (tên = Title, lọc ký tự không hợp lệ, trùng tên thêm " (n)", không ghi đè).
    Huỷ chọn thư mục / có ảnh lỗi → báo lỗi, giữ nguyên tab. Xong → xoá hết tab, `TrySaveSession()` (0
    tab → dọn thư mục phiên), đóng Editor.
  - **Bug cũ sửa kèm**: `SaveAsPngAsync` dùng `OpenStreamForWriteAsync` không cắt file → ghi đè lên
    PNG cũ lớn hơn để lại đuôi dữ liệu thừa (file hỏng). Thêm `stream.SetLength(0)`.
  - Đã chạy thử luồng *Đóng tất cả → Đóng không lưu* qua UI Automation (2 ảnh): hộp thoại đủ 3 nút,
    Editor đóng, thư mục phiên chỉ còn session.json rỗng. Luồng chọn thư mục chưa tự động hoá được.
- **Cửa sổ Cài đặt + phím tắt toàn cục** (theo mẫu "Program Options" PicPick; chỉ làm tuỳ chọn cho
  tính năng app đã có):
  - `Models/AppSettings.cs` (+ `HotkeyAction`, `HotkeyBinding`: Shift/Ctrl/Alt + tên phím → VK),
    `Services/SettingsStore.cs` (JSON ở `Data\Config\settings.json` cạnh exe — cùng quy ước
    `ConnectionSettingsStore` của Rdbms.HtmlGenerator; file hỏng → mặc định).
  - `Services/HotkeyService.cs`: `RegisterHotKey` (MOD_NOREPEAT) trên HWND launcher; WM_HOTKEY bắt qua
    `SetWindowSubclass` với `[UnmanagedCallersOnly]` + function pointer (WinUI 3 không cho WndProc);
    xử lý đẩy lên `DispatcherQueue`. `Apply()` đăng ký lại toàn bộ, trả về binding lỗi (đã bị giữ).
  - `Views/SettingsWindow`: 4 trang (Chung / Tự động lưu / Phiên làm việc / Phím tắt — lưới phím tắt
    dựng trong code-behind), chỉnh trên bản sao; OK → validate (thư mục tự lưu, trùng tổ hợp) → callback
    launcher `ApplySettings` (lưu file, `SessionService.ApplySettings`, `PersistSession`, đăng ký lại
    phím). Có phím lỗi → giữ cửa sổ mở, đánh ⚠.
  - `CaptureLauncherWindow` viết lại luồng chụp: `BeginCaptureAsync` (chặn chụp chồng `_isCapturing`,
    thu nhỏ launcher+Editor, chờ 200ms + hẹn giờ) → chụp → `FinishCaptureAsync` (mở Editor, tự lưu qua
    `EditorViewModel.SaveToFolder`, tự copy). Launcher đang thu nhỏ trước khi chụp thì không bật lên
    lại (`IsIconic`). "Chụp lại lần gần nhất" nhớ `_lastKind` + `_lastRect`.
  - `SessionService`: `MaxTabs`/`MaxBytes` thành property + `Enabled`; tắt nhớ tab → Load rỗng, Save
    dọn sạch; Editor đóng cửa sổ lúc đó hỏi lưu như trước.
  - **Đã chạy thử thật**: mở app → `PrtSc` + `Shift+PrtSc` đăng ký được, `Alt+PrtSc` và
    `Ctrl+Shift+PrtSc` báo bị giữ (máy đang chạy PicPick dùng đúng các phím này); mô phỏng bấm `PrtSc`
    → tự chụp + mở Editor; `Shift+PrtSc` → hiện overlay chọn vùng; cửa sổ Cài đặt hiển thị đúng, ⚠
    đúng 2 phím bị giữ. Chưa thử: tự lưu / tự copy / hẹn giờ trên GUI.
- **Chạy ngầm ở khay hệ thống + khởi động cùng Windows**:
  - `Services/TrayIconService.cs`: `Shell_NotifyIcon` trên HWND launcher, callback `WM_APP+1` bắt qua
    subclass riêng (id khác HotkeyService, 2 subclass cùng tồn tại được); icon vẽ bằng Skia → PNG →
    `CreateIconFromResourceEx` (không cần file .ico); menu chuột phải = `CreatePopupMenu` +
    `TrackPopupMenuEx(TPM_RETURNCMD)` (kèm SetForegroundWindow + PostMessage WM_NULL theo tài liệu);
    tự thêm lại icon khi nhận "TaskbarCreated" (Explorer khởi động lại). `NOTIFYICONDATAW` khai báo
    với `fixed char` buffer (blittable, dùng được với LibraryImport).
  - `AppSettings.RunInTray` (mặc định bật) / `StartWithWindows`. Launcher: `AppWindow.Closing` → bật
    khay thì `AppWindow.Hide()` (+ bong bóng lần đầu), tắt thì `ExitAsync`. `ExitAsync` gọi
    `EditorWindow.RequestCloseAsync()` (đổi tên từ TryCloseWindowAsync, trả bool; mở Editor lên nếu cần
    hỏi) → Huỷ thì không thoát.
  - `MinimizeForCapture`: cửa sổ đang ẩn (`AppWindow.IsVisible == false`) thì không `SW_MINIMIZE` (sẽ
    làm nó hiện ở taskbar) và chụp xong không bật lại.
  - `StartupRegistration`: HKCU Run = `"exe" --tray`. `App.OnLaunched` đọc `--tray` → launcher
    `StartHidden` (không Activate, không mở Editor phiên cũ ngay).
  - **Đã chạy thử**: bấm X → process còn chạy, cửa sổ ẩn; `Shift+PrtSc` khi đang ẩn → hiện overlay
    chọn vùng, Esc → cửa sổ chính vẫn ẩn; chạy `--tray` → không có cửa sổ nào hiện, process sống; log
    chẩn đoán xác nhận `Shell_NotifyIcon(NIM_ADD)` thành công. Chưa tự động hoá được: click icon / menu
    khay (icon nằm trong nhóm icon ẩn nên UI Automation không thấy).
  - **Phát hiện khi test**: PrtSc đơn lẻ không tới app dù `RegisterHotKey` thành công — Snipping Tool
    (Windows 11, "Print screen key opens screen capture") bắt phím bằng hook cấp thấp trước. Không phát
    hiện được bằng API → ghi chú trong trang Phím tắt + README.
- **Chụp cuộn trang** (`Services/ScrollCaptureService.cs`):
  - Luồng: overlay chọn vùng (như Vùng chọn, có dòng hướng dẫn riêng) → đưa cửa sổ dưới tâm vùng lên
    foreground (`WindowFromPoint` + `GetAncestor(GA_ROOT)` — gọi từ khay/phím tắt thì foreground đang
    là taskbar/launcher ẩn) → chụp khung đầu khi màn hình đã đứng yên (2 lần chụp liên tiếp giống nhau)
    → lặp: `SendInput` lăn chuột (số nấc = cao vùng / 300, 1–5) tại tâm vùng, chờ 450ms, chụp, ghép.
  - Ghép: mỗi dòng pixel hash FNV-1a (cả dòng + 16 khối ngang). Chỉ tính **cột động** (khác nhau giữa
    2 khung ở cùng vị trí) — bỏ viền / khung focus / lề đứng yên. Đầu/chân trang cố định = dải dòng
    giống hệt ở cùng vị trí. Dò độ dịch s: dòng khớp nếu ≥ 12/16 khối giống (chịu được con trượt thanh
    cuộn lọt vào vùng); chọn s đạt ≥ 90% dòng khớp với nhiều dòng "có nội dung" khớp nhất.
  - Dừng: khung không đổi sau 2 lần lăn thử lại (tới cuối), Esc (`GetAsyncKeyState`), 80 bước / 30.000px,
    hoặc không ghép được sau 1 lần chụp lại (giữ phần đã ghép). Dải mới lưu dạng bitmap nhỏ, không giữ
    mọi khung đầy đủ trong RAM.
  - Nối vào: thẻ Cuộn trang (bỏ "Sắp có"), `HotkeyAction.ScrollCapture` (mặc định `Ctrl+Alt+PrtSc`;
    `SettingsStore.Load` bổ sung phím cho file cài đặt cũ thiếu action mới), menu khay.
  - **Bug "chụp cuộn từ khay không hoạt động"** (người dùng báo): lệnh từ khay/phím tắt chạy
    `_ = Task` → exception bị nuốt, không báo gì. Đổi sang `RunInBackground` (bắt lỗi → InfoBar +
    crash.log) + đưa cửa sổ đích lên foreground trước khi lăn chuột.
  - **Kiểm thử tự động (cô lập)**: `SessionService.Folder` đổi được qua biến môi trường
    `SCREENCAPTURE_SESSION_DIR`; `SCREENCAPTURE_SCROLL_DEBUG_DIR` lưu từng khung + `shift.log`. Test trên
    form WinForms 300 dòng (script trong scratchpad, không đụng thư mục phiên thật — kiểm tra trước/sau).
    Qua 4 vòng sửa theo lỗi tìm được (cửa sổ vẽ lại sau khi kích hoạt; cột viền/khung focus; 1 lần lăn
    chuột bị lỡ → dừng sớm; con trượt thanh cuộn làm 10% dòng lệch) → vòng cuối **12/12 lần thành
    công** (9 qua nút, 3 qua đường nền như khay), ảnh 47 khung ~650×7820px, đủ Dong 001–300, không lặp.
  - **Sự cố trong lúc test (2026-09-24)**: lệnh "sao lưu" `Copy-Item -LiteralPath dir\*` không chép gì
    (LiteralPath không hiểu `*`) rồi thư mục phiên thật bị xoá → mất 3 tab chưa lưu của người dùng. Từ
    đó mọi test dùng thư mục phiên riêng qua `SCREENCAPTURE_SESSION_DIR`.
- **Chụp cuộn ngang** (người dùng chọn hướng rõ ràng — 2 thẻ "Cuộn dọc" / "Cuộn ngang", không tự đoán):
  - `ScrollCaptureService.CaptureAsync(rect, ScrollDirection)`. Mỗi khung được **chuyển vị** (cột ↔
    dòng) ngay khi chụp → "cột mới hiện ở mép phải" thành "dòng mới hiện ở dưới", dùng lại nguyên
    thuật toán ghép dọc; ảnh ghép xong chuyển vị ngược lại. Số nấc lăn tính theo chiều rộng vùng.
  - Lăn ngang bằng `MOUSEEVENTF_HWHEEL` (dương = sang phải). Nếu bước đầu khung không đổi (app không
    hỗ trợ HWHEEL) → chuyển sang Shift + lăn dọc (`keybd_event(VK_SHIFT)` bao quanh `SendInput`).
  - `MaxSteps` 80 → 150: vùng chọn thấp/hẹp thì mỗi bước cuộn ít, 80 bước chưa hết nội dung (gặp khi
    test cuộn ngang). Giới hạn chính vẫn là `MaxHeight` 30.000px (theo chiều cuộn).
  - Nối vào: thẻ launcher "Cuộn ngang" (thẻ cũ đổi tên "Cuộn dọc", 2 thẻ chung 1 hàng),
    `HotkeyAction.ScrollCaptureHorizontal` (mặc định không có phím), menu khay "Chụp cuộn ngang",
    thông báo overlay + trạng thái Editor ghi rõ hướng ("đã tới mép phải").
  - **Kiểm thử tự động trong Windows Sandbox (2026-09-25)** — chuột/phím giả lập không đụng màn hình
    thật, `%TEMP%` riêng nên không thể chạm thư mục phiên thật. Form WinForms vẽ lưới ô có nhãn, dài
    12.000px, 3 chế độ: lăn dọc / nhận HWHEEL / chỉ nhận Shift+lăn; ảnh ghép (AutoSave) so từng pixel
    với ảnh "đáp án" vẽ cùng nội dung. Kết quả: dọc 2/2, ngang HWHEEL 3/3, ngang Shift+lăn 3/3 — đủ
    chiều dài, **lệch 0,00%**, ~40 giây/lượt. Lỗi dừng sớm ~6000px ở chế độ Shift gặp 1 lần khi test
    trên máy thật hôm trước không tái hiện (có thể do cửa sổ khác trên máy thật chen vào).
  - Ghi chú khi dựng test: overlay WinUI không nhận thao tác kéo nếu con trỏ chỉ dời bằng
    `SetCursorPos` — phải dùng `mouse_event(MOVE | ABSOLUTE)`. Sandbox không có .NET/Windows App SDK
    runtime → publish self-contained bản Debug (`WindowsAppSDKSelfContained` chỉ đặt cho project app,
    đặt toàn cục thì SharedUI báo lỗi "should not be applied to a class library") và chép thêm thư mục
    `SharedUI\` từ bin (bản Debug không nhúng XBF của SharedUI vào `.pri`; bản Release thì có, nên bản
    deploy qua `build\Publish-AppSuite.ps1` không bị ảnh hưởng).
- **Giới hạn chụp cuộn chỉnh được trong Cài đặt** (người dùng yêu cầu, thay vì hằng số): trang mới
  *Chụp cuộn* trong `SettingsWindow` — `AppSettings.ScrollMaxSteps` (10–1000, mặc định 150),
  `ScrollMaxLength` (2.000–60.000px theo chiều cuộn, mặc định 30.000; trần 60.000 vì ảnh rộng 2000px ×
  60.000px ≈ 480 MB RAM), `ScrollSettleMs` (200–3000ms, mặc định 450; ban đầu cho tối thiểu 100 nhưng hạ thấp vậy dễ dừng sớm "tới cuối" ở trang cuộn mượt → nâng lên 200). Khoảng hợp lệ là hằng
  `*Range` trong `AppSettings`, dùng chung cho NumberBox và cho `ScrollCaptureService` (constructor nhận
  `AppSettings`, kẹp giá trị lạ từ file sửa tay). File cài đặt cũ thiếu 3 khoá → mặc định. Thông báo
  "chạm giới hạn" ở Editor chỉ tới Cài đặt > Chụp cuộn. Chọn trang theo `Tag` (`SelectCategory`) thay vì
  chỉ số, vì chèn trang làm lệch `SelectedIndex`.
  - **Đã chạy thử trong Sandbox**: 10 lần cuộn + chờ 200ms → dừng đúng sau 10 lần lăn (11 khung, ảnh
    2654px = 654 + 10 × 200, khớp pixel 100%, 6 giây); mặc định cuộn ngang vẫn đủ 11960px, khớp 100%;
    chụp màn hình trang Cài đặt mới (mở qua UI Automation) hiển thị đúng.
- **Che thông tin nhạy cảm — Mosaic / Làm mờ** (nhóm ribbon "Che", `Models/RedactAnnotation.cs`):
  - Là 1 shape (`RedactAnnotation`, `Mode` = Mosaic | Blur), không phải thao tác pixel như Tô màu → che
    chỉ khi người dùng chủ động vẽ, di chuyển / co giãn / xoá / Undo được, ảnh gốc giữ nguyên tới khi
    Lưu / Copy / Flatten. Mức độ dùng lại `StrokeWidth` (thanh Size 1–20) → panel chỉnh shape đã chọn,
    `ChangeAnnotationStyleCommand` và lưu phiên dùng được luôn; Color không áp dụng.
  - Cần pixel ảnh nền → thêm `AnnotationShape.Render(canvas, baseImage)` (mặc định gọi `Render(canvas)`),
    4 chỗ vẽ shape (canvas Editor, hình nháp, `RenderComposited`, Flatten) truyền ảnh nền. Chỉ che ảnh
    nền, không che shape nằm dưới. `Render(canvas)` không có ảnh nền → tô xám đặc (không lộ nội dung).
  - Mosaic: thu nhỏ vùng về 1 pixel/ô (lọc Medium = trung bình), phóng to lại không nội suy; ô =
    4 + Size × 2 px. Blur: `SKImageFilter.CreateBlur` sigma = 3 + Size, lấy thêm viền 3σ pixel thật quanh
    vùng để mép mờ đều. Kết quả cache theo (ảnh nền, vùng, Size) — không tính lại mỗi lần vẽ canvas.
  - Lưu phiên: `ShapeDto.RedactMode`.
  - **Đã chạy thử trong Sandbox** (Editor thật, thao tác chuột + UI Automation): Mosaic và Làm mờ che
    kín chữ, phần ngoài vùng nguyên vẹn, ảnh Copy ra clipboard đã che; chọn lại vùng Mosaic + Size 12 →
    ô to hơn; Ctrl+Z ×2 gỡ đúng việc đổi Size và vùng Làm mờ. Nhớ qua phiên: đóng Editor → tắt hẳn
    app → mở lại, 2 vùng che (đúng Mosaic/Blur) + mũi tên + stamp khôi phục nguyên vẹn.
  - Flatten **không áp dụng** cho vùng che: nút Flatten chỉ nằm trong tab contextual "Number Stamp".
    Không cần — Lưu / Copy đã xuất ảnh đã che.
- **Kiểm tra GUI bổ sung trong Sandbox (2026-09-25)**: kéo đầu mút mũi tên (đầu đổi hướng, đuôi giữ
  nguyên); nút `−`/`+` Current/Next của Number Stamp (1 → 2, Next 2 → 3, stamp vẽ lại đúng số).
  **Sửa**: menu General Stamps hiện 4 mũi tên chéo thành mũi tên lên — glyph chéo là mũi tên lên xoay
  đi, stamp trên ảnh có xoay (`GlyphRotationDegrees`) nhưng icon trong menu thì không. Thêm
  `StampAnnotation.RotationOf` dùng chung cho `RotateTransform` của icon trong menu → đủ 8 hướng.
- **`RegionOverlayWindow.IsAlwaysOnTop` bật lại** (bug #3 ở trên): `= !Debugger.IsAttached` — topmost
  khi chạy thật, tự tắt khi debug trong VS để không che breakpoint/exception dialog.

### Không dùng DI container

Giống mọi module khác trong AppSuite: không có DI container, ViewModel/Service khởi tạo thủ công
trong code-behind của View (View sở hữu việc mở cửa sổ mới — `CaptureLauncherWindow` mở
`RegionOverlayWindow`/`EditorWindow` trực tiếp, không qua ViewModel).

## Kiểm chứng

- `dotnet build Modules\ScreenCapture\ScreenCapture.csproj -p:Platform=x64` — build sạch (đã chạy
  thành công).
- `dotnet build AppSuite.sln` — không ảnh hưởng 11 project còn lại (đã chạy thành công, 0 lỗi).
- Launcher mới: đã chạy app + chụp màn hình xác nhận hiển thị đúng (DPI 150%). Mũi tên kéo đầu mút
  và nút `−`/`+` Stamp Format: ✅ đã thao tác thử trên GUI (Sandbox, 2026-09-25).
- **Chưa chạy thử GUI thực tế đầy đủ** — các rủi ro sau cần người dùng tự kiểm chứng khi chạy thật:
  - ✅ **Đã xác nhận và sửa** (phản hồi thực tế từ người dùng dùng máy 2 màn hình): overlay Region/
    Fixed Region bị phóng to khi màn hình chạy DPI scale >100%. Nguyên nhân: `BackgroundImage`
    (`Stretch="None"`) hiển thị bitmap capture (kích thước theo device pixel) nhưng WinUI layout tính
    theo DIP (logical pixel) — ảnh bị vẽ to hơn màn hình thật đúng bằng hệ số scale. Đã sửa: đổi
    `Stretch="Fill"` + set `Width`/`Height` của ảnh bằng `virtualRect.Width/Height` chia cho
    `XamlRoot.RasterizationScale` ngay khi overlay activate (`RegionOverlayWindow.xaml.cs`).
  - ✅ `SKXamlCanvas` (SkiaSharp.Views.WinUI) render đúng trong project unpackaged (thấy trên ảnh chụp
    Editor khi test trong Sandbox).
  - `AppWindow`/`OverlappedPresenter` với `IsAlwaysOnTop` trên máy đa màn hình DPI khác nhau.
  - `DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)` trên các loại cửa sổ khác nhau (browser, Electron,
    WPF, WinUI).
  - ✅ Copy ảnh vào clipboard trên app unpackaged (`Windows.ApplicationModel.DataTransfer.Clipboard`)
    chạy được — test trong Sandbox đọc lại ảnh từ clipboard bằng app khác (`Clipboard.GetImage`) đúng.
  - Delay 200ms sau `SetForegroundWindow`/minimize launcher có đủ cho các app khác nhau repaint xong
    trước khi chụp không — con số này là ước lượng, cần tinh chỉnh thực tế.

## Chưa làm (fast-follow)

- Editor: Freehand pen, crop không phá huỷ. (Blur/Mosaic **đã làm** — nhóm "Che".)
  (Resize shape qua 4 handle góc và đổi hướng/độ dài Line/Arrow qua 2 handle đầu mút **đã làm**.)
- Fixed Region: lưu vị trí/kích thước qua lần restart app (hiện chỉ session-only, mất khi đóng app).
- Window capture: chọn cửa sổ khác ngoài foreground window (cần `EnumWindows` + UI danh sách chọn).
- Logging: module chưa wire `Common.Logging` (không module nào khác trong repo hiện dùng logging
  ngoài MainLauncher) — cân nhắc thêm sau vì module có nhiều edge case Win32 khó debug.
