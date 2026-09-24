# ScreenCapture

Ứng dụng WinUI 3 độc lập: công cụ chụp màn hình + chỉnh sửa ảnh, kiểu PicPick. Không có bất kỳ tham
chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới `Common` và `SharedUI`.

## Chức năng

### Cửa sổ chính — chọn chế độ chụp

`CaptureLauncherWindow`: lưới thẻ 2 cột (icon + tên + mô tả), màu nhấn `#D86445`. Thông báo (huỷ
chọn vùng, không tìm thấy cửa sổ...) hiện bằng `InfoBar` ở cuối cửa sổ.

- **Toàn màn hình** — chụp toàn bộ virtual screen (mọi màn hình).
- **Cửa sổ hiện tại** — chụp cửa sổ đang active (bất kỳ ứng dụng nào, kể cả app render bằng
  DirectX như trình duyệt).
- **Vùng chọn** — kéo-thả chọn 1 vùng màn hình, thả chuột là chụp ngay.
- **Vùng cố định** — chọn vùng, chỉnh lại kích thước qua 4 handle góc, nhấn Enter để chụp (Escape
  để huỷ). Vị trí/kích thước lần chụp gần nhất được nhớ lại trong phiên làm việc hiện tại.
- **Cuộn trang** — chưa làm (thẻ bị disable, nhãn "Sắp có"), xem `PLAN.md` mục "Chưa làm".

### Trình chỉnh sửa (`EditorWindow`)

Sau khi chụp (bất kỳ mode nào), ảnh mở ngay trong cửa sổ chỉnh sửa (tự maximize). Thanh công cụ
dạng **ribbon** kiểu PicPick, mỗi nhóm nút có nhãn phía dưới, chia thành các tab:

**Tab "Trang chủ"**

- **Chọn** — *Di chuyển*: click chọn 1 shape (viền nét đứt + 4 handle góc), kéo để di chuyển, kéo
  handle góc để đổi kích thước. *Xoá* (hoặc phím `Delete`/`Backspace`), *Lên trên* / *Xuống dưới*
  (đổi thứ tự lớp).
- **Vẽ hình** — Chữ nhật, Elip, Đường thẳng, Mũi tên, Highlight (marker tô trong mờ), Text (click
  vào canvas, nhập text qua dialog).
  - Đường thẳng / Mũi tên giữ đúng hướng kéo chuột. Khi chọn bằng *Di chuyển*: hiện 2 handle tròn ở
    2 đầu; bấm **gần một đầu** (khoảng 1/3 độ dài, tối đa 30px) rồi kéo để đổi hướng/độ dài, bấm
    khúc giữa để di chuyển cả đường. Chọn theo khoảng cách tới thân đường, không theo khung bao.
- **Tô & Dấu**
  - *Tô màu* — bucket fill pixel thật (giống MS Paint/PicPick), click vào 1 vùng liền màu trên ảnh
    gốc để đổi màu cả vùng đó (flood-fill, có ngưỡng tolerance cho vùng anti-alias nhẹ).
  - *Stamps* — flyout chọn dấu:
    - **Number Stamps** — hình tròn có số, 7 màu (mặc định `#D86445`), viền trắng + đổ bóng nhẹ.
      Số **tự tăng** mỗi lần đặt (1, 2, 3...).
    - **General Stamps** — mũi tên 8 hướng, bookmark, pin, flag, tag, info, warning, no-entry,
      heart, plus, minus, check, cross, star. Đặt lên ảnh theo màu Color1 hiện tại.
- **Cắt & Sửa** — *Cắt* (crop, shape nằm ngoài vùng cắt bị bỏ), *Undo* / *Redo* từng bước.
- **Màu** — *Color1* (màu nét/màu chính) / *Color2* (màu fill/highlight), color picker. Đổi màu khi
  đang chọn 1 shape sẽ áp luôn cho shape đó.
- **Cỡ nét** — slider 1-20px, cũng áp cho shape đang chọn.

**Tab "Tệp"** — *Lưu PNG*, *Copy* ảnh (đã gộp mọi shape) vào clipboard, *Đóng* cửa sổ.

**Tab "Number Stamp"** (contextual) — tự hiện và tự chuyển sang khi chọn 1 Number Stamp đã đặt,
tự ẩn khi bỏ chọn:

- **Edit** — *Flatten* (gộp stamp vào ảnh, không chỉnh được nữa), *Xoá*.
- **Shape Styles** — đổi nhanh màu nền stamp theo 7 màu có sẵn.
- **Stamp Format** — *Current*: sửa số của stamp đang chọn; *Next*: số sẽ dùng cho stamp đặt tiếp
  theo. Mỗi ô có nút `−` (trái) / `+` (phải) và gõ số trực tiếp được, tối thiểu 1.
- **Shape Colors** — *Outline* (màu viền) / *Fill* (màu nền) riêng cho stamp đang chọn.
- **Arrange** — *Lên trên* / *Xuống dưới*.

Mọi thao tác chỉnh sửa (vẽ, di chuyển, đổi kích thước/hướng, đổi màu/cỡ nét, đổi số stamp,
Flatten, Tô màu, Cắt, đổi thứ tự lớp, xoá) đều Undo/Redo được.

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
  `Modules/CsvEditor/Commands`. Mỗi thao tác là 1 command trong `Commands/` (`MoveResizeAnnotation`,
  `ChangeAnnotationStyle`, `ChangeStampColors`, `ChangeStampNumber`, `FlattenAnnotation`,
  `FloodFill`, `ReorderAnnotation`, `Crop`...).
- **Shape** (`Models/`): mỗi loại tự vẽ bằng Skia qua `AnnotationShape.Render`. `Bounds` của
  `LineArrowAnnotation` lưu điểm đầu/cuối nên **không chuẩn hoá** — code cần khung thật dùng
  `NormalizedBounds`; hit-test qua `AnnotationShape.HitTest` (đường thẳng override theo khoảng cách
  tới đoạn thẳng).
- Không có DI container, giống mọi module khác trong AppSuite — service khởi tạo thủ công trong
  code-behind của View.

Xem `PLAN.md` để biết đầy đủ quyết định thiết kế, rủi ro chưa kiểm chứng bằng chạy thực tế, và danh
sách việc chưa làm (Scroll capture, blur/mosaic, v.v.).
