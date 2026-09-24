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

### Cài đặt (`SettingsWindow`)

Mở bằng nút **Cài đặt** ở góc phải cửa sổ chính, hoặc tab *Tệp* của Editor. Bố cục kiểu "Program
Options" của PicPick; bấm *OK* mới lưu (vào `Data\Config\settings.json` cạnh exe), *Mặc định* đưa mọi
tuỳ chọn về ban đầu.

| Trang | Tuỳ chọn |
|---|---|
| Chung | Hẹn giờ trước khi chụp (0–10 giây); chụp xong tự copy ảnh vào clipboard; chạy ngầm ở khay hệ thống; khởi động cùng Windows |
| Tự động lưu | Tự lưu mỗi ảnh chụp thành PNG vào 1 thư mục (mặc định `Pictures\ScreenCapture`), tên = thời điểm chụp; ảnh đã tự lưu đóng tab không hỏi lại |
| Phiên làm việc | Bật/tắt nhớ tab khi tắt app; giới hạn số tab / MB; xem dung lượng + mở thư mục tạm |
| Phím tắt | Phím tắt toàn cục cho Toàn màn hình / Cửa sổ hiện tại / Vùng chọn / Vùng cố định / Chụp lại lần gần nhất — Shift/Ctrl/Alt + 1 phím (PrintScreen, A–Z, 0–9, F1–F12) |

- Phím tắt mặc định giống PicPick: `PrtSc`, `Alt+PrtSc`, `Shift+PrtSc`, `Ctrl+Shift+PrtSc`.
- Dùng được cả khi app đang thu nhỏ, ẩn ở khay hệ thống hoặc đang ở app khác. Chụp bằng phím tắt
  khi cửa sổ chính đang thu nhỏ / ẩn thì chụp xong nó vẫn giữ nguyên, không bật lên.
- Phím đã bị app khác giữ (vd PicPick đang chạy) → đánh dấu ⚠ trong trang Phím tắt + thông báo ở
  cửa sổ chính. **Ngoại lệ**: PrintScreen đơn lẻ khi Windows 11 bật "Use the Print screen key to open
  screen capture" — Snipping Tool bắt phím bằng hook cấp thấp nên đăng ký vẫn "thành công" nhưng app
  không nhận được phím; tắt tuỳ chọn đó của Windows hoặc dùng tổ hợp có Shift/Ctrl/Alt.

**Chạy ngầm ở khay hệ thống** (bật mặc định, tắt được trong Cài đặt → Chung):

- Icon ScreenCapture ở khay (góc phải taskbar; có thể nằm trong nhóm icon ẩn `^`). Click trái → mở
  cửa sổ chính. Click phải → menu: Chụp toàn màn hình / cửa sổ / vùng chọn / vùng cố định, Mở cửa sổ
  chính, Mở Editor, Cài đặt..., **Thoát**.
- Bấm X ở cửa sổ chính → ẩn xuống khay (lần đầu có bong bóng thông báo), phím tắt vẫn dùng được.
  Thoát hẳn bằng *Thoát* ở menu khay (Editor vẫn lưu tạm / hỏi lưu ảnh như khi đóng bình thường).
- Tắt tuỳ chọn → không có icon, bấm X ở cửa sổ chính là thoát hẳn app.
- *Khởi động cùng Windows*: ghi `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (không cần quyền
  admin), chạy với `--tray` → chỉ hiện icon ở khay, không bật cửa sổ chính; tab của phiên trước được nạp
  lại ở lần chụp / mở Editor đầu tiên.
- *Chụp lại lần gần nhất*: lặp lại kiểu chụp gần nhất; vùng chọn / vùng cố định thì chụp lại đúng
  vùng đó ngay, không hiện màn chọn vùng.
- Không cho 2 thao tác dùng chung 1 tổ hợp phím.

### Trình chỉnh sửa (`EditorWindow`)

Sau khi chụp (bất kỳ mode nào), ảnh mở ngay trong cửa sổ chỉnh sửa (tự maximize).

**Nhiều ảnh chụp dạng tab** (giống PicPick): chỉ có 1 cửa sổ Editor. Mỗi lần chụp thêm 1 tab mới
(tên = thời điểm chụp, vd `2026-09-24 13 36 14`) và chuyển sang tab đó; các ảnh chụp trước vẫn giữ
nguyên — kể cả shape đã vẽ và lịch sử Undo riêng của từng ảnh. Công cụ / màu / cỡ nét dùng chung cho
mọi tab. Khi chụp, cả launcher lẫn Editor đều tự thu nhỏ để không lọt vào ảnh.

- Đóng 1 tab (`×`) khi ảnh chưa lưu ra file (kể cả ảnh vừa chụp chưa sửa gì) hoặc đã sửa sau lần lưu
  cuối → hỏi *Lưu* / *Không lưu* / *Huỷ*. Đóng tab cuối cùng = đóng Editor.
- **Đóng tất cả** (nút cuối thanh tab): còn ảnh chưa lưu → hỏi *Lưu tất cả...* (chọn 1 thư mục, lưu
  mọi ảnh chưa lưu vào đó, tên file = tên tab, trùng tên thì thêm " (2)" — không ghi đè file có sẵn) /
  *Đóng không lưu* / *Huỷ*. Xong thì đóng mọi tab và Editor, thư mục lưu tạm được dọn sạch. Huỷ chọn
  thư mục hoặc có ảnh lưu lỗi → không đóng gì.
- **Nhớ tab qua lần tắt/mở app**: đóng cửa sổ Editor (nút X / *Đóng*) không hỏi gì — mọi tab được lưu
  tạm và mở lại đúng như cũ ở lần mở app sau (shape vẫn chỉnh sửa được; lịch sử Undo thì không giữ).
  - Thư mục: `%TEMP%\AppSuite\ScreenCapture\Session\` — `session.json` (danh sách tab + mô tả shape)
    và các file PNG (ảnh nền, ảnh dán).
  - **Không tích luỹ**: thư mục chỉ chứa đúng các tab đang mở ở lần lưu gần nhất. Đóng 1 tab → file
    của tab đó bị xoá ngay; đóng hết tab → thư mục rỗng. Ảnh của các phiên cũ hơn không còn trên ổ.
  - Giới hạn tối đa **30 tab / 300 MB**; vượt thì bỏ các tab cũ nhất khỏi bản lưu tạm.
  - Ghi tạm mỗi khi chụp ảnh mới, đóng tab và đóng cửa sổ.
  - Lưu ý: Windows (Storage Sense / Disk Cleanup) có thể dọn `%TEMP%`; tab nào mất file ảnh thì bỏ qua
    khi mở lại. Ảnh quan trọng vẫn nên *Lưu PNG*.

Thanh công cụ dạng **ribbon** kiểu PicPick, mỗi nhóm nút có nhãn phía dưới, chia thành các tab:

**Cách chọn & chỉnh sửa shape** (áp dụng với mọi công cụ, không cần bấm *Di chuyển* trước):

- **Vẽ/đặt xong là đang chỉnh sửa luôn**: shape vừa vẽ (hoặc stamp/text vừa đặt) được chọn ngay —
  hiện handle, tab contextual (vd *Number Stamp*) nếu có, đổi màu/cỡ nét áp luôn cho shape đó.
- **Bấm trúng 1 shape có sẵn** → chọn shape đó và kéo được ngay (di chuyển / kéo handle góc / kéo đầu
  mũi tên). Chữ nhật và Elip chỉ bắt khi bấm lên **viền**, nên vẫn vẽ được shape khác bên trong khung.
- **Bấm vào vùng trống** (hoặc nhấn `Esc`) → thoát chỉnh sửa. Với công cụ vẽ hình, bấm-kéo ở vùng
  trống thì vẽ shape mới luôn. Riêng *Text* và *Stamps*: nếu đang chọn shape, lần bấm vùng trống đầu
  chỉ bỏ chọn; lần bấm sau mới bật hộp nhập text / đặt stamp. Ví dụ Number Stamps: bấm → stamp 1 (đang
  sửa, tab *Number Stamp*) → bấm ra ngoài (thoát sửa) → bấm → stamp 2 → ... Công cụ Stamps giữ nguyên
  cho tới khi chọn công cụ khác.
- **Kích thước stamp**: kéo handle góc để phóng to/thu nhỏ stamp (luôn giữ tròn/vuông). Stamp đặt
  tiếp theo dùng đúng kích thước stamp vừa chỉnh. Chọn lại stamp từ menu *Stamps* thì về kích thước
  mặc định (32px).
- *Tô màu* là thao tác trên pixel ảnh nên không chọn shape.

**Tab "Trang chủ"**

- **Chọn** — *Move* (icon con trỏ, là công cụ mặc định khi mở ảnh): chỉ chọn/kéo shape, bấm vùng
  trống không vẽ gì. Khi không chọn shape nào, quanh ảnh hiện **8 handle** (4 góc + 4 cạnh) — kéo để
  **đổi kích thước khung ảnh** giống PicPick: kéo ra = mở rộng (phần mới tô trắng), kéo vào = cắt bớt
  cạnh đó. Shape đã vẽ giữ nguyên vị trí so với nội dung ảnh; thanh trạng thái hiện kích thước mới;
  Undo được. *Select*: kéo chuột chọn 1 vùng chữ nhật trên ảnh (viền "kiến bò" đen/trắng, giữ
  `Shift` = vùng vuông) → hiện tab contextual **"Vùng chọn"** (xem dưới). Vùng đã chọn có 8 handle:
  kéo handle để chỉnh kích thước, kéo bên trong vùng để di chuyển; bấm ngoài vùng = chọn lại / bỏ chọn.
  *Xoá* (hoặc phím `Delete`/`Backspace`), *Lên trên* / *Xuống dưới* (đổi thứ tự lớp).
- **Vẽ hình** — Chữ nhật, Elip, Đường thẳng, Mũi tên, Highlight (marker tô trong mờ), Text (click
  vào canvas, nhập text qua dialog).
  - Đường thẳng / Mũi tên giữ đúng hướng kéo chuột. Khi đang chọn: hiện 2 handle tròn ở
    2 đầu; bấm **gần một đầu** (khoảng 1/3 độ dài, tối đa 30px) rồi kéo để đổi hướng/độ dài, bấm
    khúc giữa để di chuyển cả đường. Chọn theo khoảng cách tới thân đường, không theo khung bao.
  - **Giữ `Shift`** khi vẽ hoặc kéo: Đường thẳng / Mũi tên khoá hướng theo bội số 45° (ngang, dọc,
    chéo); Chữ nhật / Elip thành hình vuông / hình tròn (cả lúc vẽ lẫn lúc kéo handle góc).
- **Tô & Dấu**
  - *Tô màu* — bucket fill pixel thật (giống MS Paint/PicPick), click vào 1 vùng liền màu trên ảnh
    gốc để đổi màu cả vùng đó (flood-fill, có ngưỡng tolerance cho vùng anti-alias nhẹ).
  - *Stamps* — flyout chọn dấu:
    - **Number Stamps** — hình tròn có số, 7 màu (mặc định `#D86445`), viền trắng + đổ bóng nhẹ.
      Số **tự tăng** mỗi lần đặt (1, 2, 3...).
    - **General Stamps** — mũi tên 8 hướng, bookmark, pin, flag, tag, info, warning, no-entry,
      heart, plus, minus, check, cross, star. Đặt lên ảnh theo màu Color1 hiện tại.
- **Cắt & Sửa** — *Cắt* (crop, shape nằm ngoài vùng cắt bị bỏ), *Undo* / *Redo* từng bước, *Dán*
  (`Ctrl+V`): dán ảnh trong clipboard (ảnh copy từ app khác, hoặc file ảnh copy trong Explorer) thành
  1 đối tượng ảnh — đặt ở góc trên-trái vùng chọn (nếu có) hoặc phần ảnh đang nhìn thấy, được chọn sẵn
  để kéo / co giãn (giữ `Shift` = đúng tỉ lệ) / Flatten. Ảnh dán lớn hơn ảnh hiện tại → khung ảnh tự
  nới ra (nền trắng), cùng 1 bước Undo.
- **Màu** — *Color1* (màu nét/màu chính) / *Color2* (màu fill/highlight), color picker. Đổi màu khi
  đang chọn 1 shape sẽ áp luôn cho shape đó.
- **Cỡ nét** — slider 1-20px, cũng áp cho shape đang chọn.

**Tab "Tệp"** — *Lưu PNG*, *Copy* ảnh (đã gộp mọi shape) vào clipboard, *Cài đặt*, *Đóng* cửa sổ.

**Tab "Vùng chọn"** (contextual) — tự hiện khi dùng *Select* chọn 1 vùng, tự ẩn khi bỏ chọn:

- *Cắt ảnh* (`Enter`) — cắt ảnh còn đúng vùng chọn (shape nằm ngoài vùng bị bỏ).
- *Copy* (`Ctrl+C`) — copy vùng (ảnh + shape đang thấy) vào clipboard.
- *Cut* (`Ctrl+X`) — copy vùng rồi tô trắng vùng đó trên ảnh nền.
- *Xoá vùng* (`Delete`) — tô trắng vùng đó trên ảnh nền.
- *Bỏ chọn* (`Esc`).

Cut/Xoá vùng chỉ đổi pixel ảnh nền, shape (mũi tên, chữ...) nằm trong vùng vẫn giữ nguyên. Tất cả
Undo được.

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

**Phím tắt trong Editor** (cũng hiện trong tooltip của nút tương ứng):

| Phím | Chức năng |
|---|---|
| `Ctrl+Z` | Undo |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Ctrl+S` | Lưu PNG |
| `Ctrl+C` | Copy ảnh (đã gộp mọi shape) vào clipboard |
| `Ctrl+V` | Dán ảnh từ clipboard |
| `Delete` / `Backspace` | Xoá shape đang chọn |
| `Esc` | Bỏ chọn shape (thoát chỉnh sửa) / bỏ vùng chọn |
| Khi có vùng chọn (*Select*): `Ctrl+C` / `Ctrl+X` / `Delete` / `Enter` | Copy / Cut / Xoá vùng / Cắt ảnh theo vùng |
| Giữ `Shift` khi vẽ/kéo | Khoá góc 45° (đường/mũi tên), vuông/tròn (chữ nhật/elip) |

Khi đang gõ trong ô số (Current/Next), các phím trên thuộc về ô đó (vd `Ctrl+Z` hoàn tác chữ vừa
gõ), không kích hoạt lệnh của Editor.

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
