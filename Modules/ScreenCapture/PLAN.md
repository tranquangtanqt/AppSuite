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
  và nút `−`/`+` Stamp Format: build sạch, **chưa thao tác thử trên GUI**.
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
- Editor: Blur/Mosaic (khác Fill — làm mờ/che chứ không đổi màu), Freehand pen, crop không phá huỷ.
  (Resize shape qua 4 handle góc và đổi hướng/độ dài Line/Arrow qua 2 handle đầu mút **đã làm**.)
- Fixed Region: lưu vị trí/kích thước qua lần restart app (hiện chỉ session-only, mất khi đóng app).
- Window capture: chọn cửa sổ khác ngoài foreground window (cần `EnumWindows` + UI danh sách chọn).
- Hotkey toàn cục (`RegisterHotKey`) để kích hoạt capture từ bên ngoài app — hiện chỉ mở được từ
  `CaptureLauncherWindow`.
- Logging: module chưa wire `Common.Logging` (không module nào khác trong repo hiện dùng logging
  ngoài MainLauncher) — cân nhắc thêm sau vì module có nhiều edge case Win32 khó debug.
