# ScreenCapture

Ứng dụng WinUI 3 độc lập: công cụ chụp màn hình + chỉnh sửa ảnh, kiểu PicPick. Không có bất kỳ tham
chiếu nào tới `MainLauncher`; chỉ `ProjectReference` tới `Common` và `SharedUI`.

## Chức năng

### Cửa sổ chính — chọn chế độ chụp

`CaptureLauncherWindow`: lưới thẻ 2 cột × 4 hàng (icon + tên + mô tả; thêm thẻ *Chụp lại lần trước*), màu nhấn `#D86445`. Thông báo (huỷ
chọn vùng, không tìm thấy cửa sổ...) hiện bằng `InfoBar` ở cuối cửa sổ.

- **Toàn màn hình** — chụp toàn bộ virtual screen (mọi màn hình).
- **Màn hình hiện tại** — chụp 1 màn hình: màn đang có con trỏ chuột (dùng nhiều màn hình). Mặc định chưa có phím tắt.
- **Cửa sổ hiện tại** — chụp cửa sổ đang active (bất kỳ ứng dụng nào, kể cả app render bằng
  DirectX như trình duyệt).
- **Vùng chọn** — kéo-thả chọn 1 vùng màn hình, thả chuột là chụp ngay. Hoặc **chụp 1 cửa sổ bất
  kỳ**: di chuột lên cửa sổ nào thì cửa sổ đó được tô viền, **click** (không kéo) là chụp đúng khung
  cửa sổ đó (phần đang thấy trên màn hình lúc bấm phím tắt).
- **Vùng cố định** — chọn vùng, chỉnh lại kích thước qua 4 handle góc, nhấn Enter để chụp (Escape
  để huỷ). Vị trí/kích thước lần chụp gần nhất được nhớ lại, **kể cả sau khi tắt mở lại app** (lưu
  trong `settings.json`; màn hình đổi mà vùng cũ lọt ra ngoài thì được cắt cho vừa / bỏ qua).
- **Cuộn dọc** (phím tắt mặc định `Ctrl+Alt+PrtSc`, hoặc menu khay) — kéo chọn vùng nội dung cần
  cuộn (trang web, tài liệu, danh sách...; nên bỏ thanh menu cố định ra ngoài vùng). App đưa cửa sổ đó
  lên, tự lăn chuột trong vùng, chụp từng khung và ghép thành 1 ảnh dài. Dừng khi tới cuối trang, khi
  bấm **Esc**, hoặc chạm giới hạn số lần cuộn / độ dài ảnh (mặc định 150 lần / 30.000px, đổi trong
  Cài đặt > Chụp cuộn). Đầu/chân trang cố định trong vùng chỉ giữ
  1 lần; viền, khung focus, thanh cuộn lọt vào vùng chọn không làm hỏng việc ghép. Thanh trạng thái
  Editor ghi số khung, kích thước và lý do dừng. Nội dung tự thay đổi (video, ảnh động, trang tự tải
  thêm khi cuộn) có thể làm dừng sớm — app giữ phần đã ghép được.
- **Cuộn ngang** (mặc định không có phím tắt — gán trong Cài đặt; hoặc menu khay) — như Cuộn dọc
  nhưng cuộn sang phải và ghép thành 1 ảnh rộng (bảng tính, timeline, bảng nhiều cột...; nên bỏ cột
  cố định bên trái ra ngoài vùng). App lăn ngang (bánh xe ngang / touchpad); nếu cửa sổ không phản
  ứng thì tự chuyển sang **Shift + lăn chuột** (cách trình duyệt, Excel cuộn ngang). Dừng khi tới
  mép phải, Esc, hoặc cùng giới hạn như cuộn dọc (độ dài ảnh tính theo chiều rộng).

### Hướng dẫn (cửa sổ dùng chung `SharedUI.Help.HelpWindow`)

Liệt kê mọi tính năng cho người dùng cuối, chia 16 danh mục (chế độ chụp, phím tắt, khay, Editor,
vẽ, che, cắt, zoom, lưu, cài đặt...). Có ô tìm kiếm không phân biệt dấu ("cat" ra "Cắt"). Mục *Phím
tắt chụp* hiện đúng phím đang cài đặt. Mở bằng nút **Hướng dẫn** ở cửa sổ chính, nút **?** góc phải
ribbon / nút *Hướng dẫn* ở tab *Tệp* của Editor, menu khay, hoặc **F1**. Nội dung ở
`Models/HelpContent.cs` — **thêm/đổi tính năng thì cập nhật cả file đó lẫn README này**.

### Cài đặt (`SettingsWindow`)

Mở bằng nút **Cài đặt** ở góc phải cửa sổ chính, hoặc tab *Tệp* của Editor. Bố cục kiểu "Program
Options" của PicPick; bấm *OK* mới lưu (vào `Data\Config\settings.json` cạnh exe), *Mặc định* đưa mọi
tuỳ chọn về ban đầu.

| Trang | Tuỳ chọn |
|---|---|
| Chung | Hẹn giờ trước khi chụp (0–10 giây); **Sau khi chụp** — các ô tick độc lập: *Mở trong Editor* (mặc định bật; tắt = chụp nhanh liên tục), *Copy vào clipboard*, *Tự lưu file* (= ô Tự động lưu ở trang Lưu ảnh), *Hiện thông báo nhỏ* (góc dưới-phải màn có con trỏ: ảnh thu nhỏ, đã lưu / đã copy, nút *Mở trong Editor* / *Mở thư mục*; không giành focus, tự ẩn sau 6 giây, rê chuột vào thì giữ, đóng trước lần chụp sau); phải chọn ít nhất 1 việc; lưu / copy lỗi mà không mở Editor thì luôn hiện thông báo; **chụp kèm con trỏ chuột** (vẽ con trỏ đúng hình / vị trí lúc chụp — Toàn màn hình, Màn hình hiện tại, Cửa sổ hiện tại, Chụp lại lần trước; không áp cho vùng chọn / vùng cố định / chụp cuộn vì con trỏ đang ở góc vùng kéo hoặc đang lăn trang); chạy ngầm ở khay hệ thống; khởi động cùng Windows |
| Lưu ảnh | Chất lượng JPG khi Lưu / Lưu thành (30–100, mặc định 90). Tự lưu mỗi ảnh chụp thành PNG vào 1 thư mục (mặc định `Pictures\ScreenCapture`), tên theo **mẫu tên file** (xem dưới); ảnh đã tự lưu đóng tab không hỏi lại |
| Phiên làm việc | Bật/tắt nhớ tab khi tắt app; giới hạn số tab / MB; xem dung lượng + mở thư mục tạm |
| Chụp cuộn | Số lần cuộn tối đa (10–1000, mặc định 150); độ dài ảnh tối đa theo chiều cuộn (2.000–60.000px, mặc định 30.000); thời gian chờ sau mỗi lần cuộn (200–3000ms, mặc định 450 — tăng cho trang tải chậm) |
| Phím tắt | Phím tắt toàn cục cho Toàn màn hình / Màn hình hiện tại / Cửa sổ hiện tại / Vùng chọn / Vùng cố định / Chụp cuộn dọc / Chụp cuộn ngang / Chụp lại lần gần nhất — Shift/Ctrl/Alt + 1 phím (PrintScreen, A–Z, 0–9, F1–F12) |

- Phím tắt mặc định giống PicPick: `PrtSc`, `Alt+PrtSc`, `Shift+PrtSc`, `Ctrl+Shift+PrtSc`,
  `Ctrl+Alt+PrtSc` (chụp cuộn dọc). Màn hình hiện tại, Chụp cuộn ngang và Chụp lại lần gần nhất mặc định không có phím.
- Dùng được cả khi app đang thu nhỏ, ẩn ở khay hệ thống hoặc đang ở app khác. Chụp bằng phím tắt
  khi cửa sổ chính đang thu nhỏ / ẩn thì chụp xong nó vẫn giữ nguyên, không bật lên.
- Phím đã bị app khác giữ (vd PicPick đang chạy) → đánh dấu ⚠ trong trang Phím tắt + thông báo ở
  cửa sổ chính. **Ngoại lệ**: PrintScreen đơn lẻ khi Windows 11 bật "Use the Print screen key to open
  screen capture" — Snipping Tool bắt phím bằng hook cấp thấp nên đăng ký vẫn "thành công" nhưng app
  không nhận được phím; tắt tuỳ chọn đó của Windows hoặc dùng tổ hợp có Shift/Ctrl/Alt.

**Chạy ngầm ở khay hệ thống** (bật mặc định, tắt được trong Cài đặt → Chung):

- Icon ScreenCapture ở khay (góc phải taskbar; có thể nằm trong nhóm icon ẩn `^`). Click trái → mở
  cửa sổ chính. Click phải → menu: Chụp toàn màn hình / màn hình hiện tại / cửa sổ / vùng chọn / vùng cố định / cuộn, Mở cửa sổ
  chính, Mở Editor, Cài đặt..., Hướng dẫn, **Thoát**.
- Bấm X ở cửa sổ chính → ẩn xuống khay (lần đầu có bong bóng thông báo), phím tắt vẫn dùng được.
  Thoát hẳn bằng *Thoát* ở menu khay (Editor vẫn lưu tạm / hỏi lưu ảnh như khi đóng bình thường).
- Tắt tuỳ chọn → không có icon, bấm X ở cửa sổ chính là thoát hẳn app.
- *Khởi động cùng Windows*: ghi `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (không cần quyền
  admin), chạy với `--tray` → chỉ hiện icon ở khay, không bật cửa sổ chính; tab của phiên trước được nạp
  lại ở lần chụp / mở Editor đầu tiên.
- *Chụp lại lần gần nhất* (cũng là thẻ *Chụp lại lần trước* ở cửa sổ chính): lặp lại kiểu chụp gần nhất; vùng chọn /
  vùng cố định thì chụp lại đúng vùng đó ngay, không hiện màn chọn vùng.
- Không cho 2 thao tác dùng chung 1 tổ hợp phím.

**Mẫu tên file khi tự lưu** (Cài đặt > Lưu ảnh, `Models/FileNameTemplate`): ô chọn mẫu có sẵn hoặc gõ tuỳ ý + dòng xem
trước. Mặc định `{date}_{time}_{app}` → `2026-10-07_10-31-10_EXCEL.png` (`{date}_{time}` = `yyyy-MM-dd_HH-mm-ss`).

| Thẻ | Giá trị |
|---|---|
| `{date}` / `{time}` | Ngày `yyyy-MM-dd` / giờ `HH-mm-ss` lúc chụp |
| `{app}` | Tên tiến trình của cửa sổ bị chụp (`EXCEL`, `msedge`; app Store lấy app bên trong khung `ApplicationFrameHost`); không có cửa sổ → `Desktop`. Mọi trang web trong cùng trình duyệt ra cùng 1 tên — phân biệt bằng `{window}` |
| `{window}` | Tiêu đề cửa sổ bị chụp (≤ 50 ký tự) |
| `{mode}` | Kiểu chụp: `ToanManHinh`, `ManHinh`, `CuaSo`, `Vung`, `VungCoDinh`, `Cuon` |
| `{size}` | `rộng x cao` px |
| `{n}` | Số thứ tự `001`, `002`… = số lớn nhất của các file cùng mẫu trong thư mục đích + 1 |

Dấu `\` (hoặc `/`) = thư mục con, vd `{date}\{time}_{app}` → mỗi ngày 1 thư mục; `..` bị bỏ. Ký tự cấm trong tên file →
`_`; trùng tên thêm ` (2)`; mẫu rỗng → mặc định; đuôi ảnh gõ kèm ở cuối (`.png`, `.jpg`…) tự bỏ (tự lưu luôn là PNG) —
vd `{date}\{date}_{time}_{app}.png` → `2026-10-07\2026-10-07_10-31-10_EXCEL.png` (có sẵn trong danh sách mẫu). Cửa sổ bị chụp (`Services/CaptureTarget`): *Cửa sổ hiện tại* = cửa sổ
đó; vùng chọn / vùng cố định / cuộn = cửa sổ trên cùng chứa **tâm vùng** (danh sách cửa sổ lấy cùng lúc chụp ảnh nền
đứng yên); toàn màn hình / màn hình hiện tại = cửa sổ đang active (là của ScreenCapture thì cửa sổ dưới con trỏ).

### Trình chỉnh sửa (`EditorWindow`)

Sau khi chụp (bất kỳ mode nào), ảnh mở ngay trong cửa sổ chỉnh sửa (tự maximize).

**Nhiều ảnh chụp dạng tab** (giống PicPick): chỉ có 1 cửa sổ Editor. Mỗi lần chụp thêm 1 tab mới
(tên = thời điểm chụp, vd `2026-09-24 13 36 14`) và chuyển sang tab đó; các ảnh chụp trước vẫn giữ
nguyên — kể cả shape đã vẽ và lịch sử Undo riêng của từng ảnh. Công cụ / màu / cỡ nét dùng chung cho
mọi tab. Khi chụp, cả launcher lẫn Editor đều tự thu nhỏ để không lọt vào ảnh.

- **Mở ảnh có sẵn** (`Ctrl+O`, tab *Tệp* > *Mở*, nút *Mở ảnh* ở cửa sổ chính, hoặc kéo-thả file vào Editor / cửa sổ chính):
  PNG, JPG, BMP, GIF (khung đầu), WEBP; chọn nhiều file → mỗi file 1 tab, tên tab = tên file. Ảnh chụp điện thoại tự xoay
  đúng chiều theo EXIF. Tab mở từ file coi như **đã lưu** (không sửa gì thì đóng không hỏi) và gắn với file đó: *Lưu*
  (`Ctrl+S`) **ghi đè file gốc** (PNG / JPG / BMP; GIF / WEBP thì hỏi nơi lưu) — muốn giữ ảnh gốc thì dùng *Lưu thành…*. File không phải ảnh / đọc lỗi → báo ở thanh trạng thái, các file khác vẫn mở. Tối đa 250 triệu pixel.
- **Ảnh mới** (`Ctrl+N`, tab *Tệp* > *Ảnh mới*, hoặc nút *Ảnh mới* ở cửa sổ chính — giống *New* của PicPick): tạo ảnh
  trống thành 1 tab tên `Ảnh mới`. Hộp thoại: mẫu kích thước (*Ảnh trong clipboard*, *Ảnh đang mở*, *Màn hình chính*,
  640×480 … 1920×1080; mặc định là ảnh trong clipboard nếu có), rộng / cao (1–16384 px, tối đa 64 triệu pixel ~ 8000 × 8000;
  nút ⇄ đổi ngang ↔ dọc, nhập tay
  → *Tuỳ chỉnh*), màu nền (*Trắng* / *Đen* / *Trong suốt* / màu bất kỳ qua ô màu; lần đầu *Đen* như gợi ý của PicPick,
  sau đó nhớ màu lần trước — `NewImageBackColor` trong settings.json). Xem *Nền trong suốt* ở nhóm *Cắt & Sửa*.
- Đóng 1 tab (`×`, hoặc `Ctrl+W` / `Ctrl+F4` cho tab đang mở) khi ảnh chưa lưu ra file (kể cả ảnh vừa chụp chưa sửa gì) hoặc đã sửa sau lần lưu
  cuối, hoặc file đã lưu (kể cả ảnh tự lưu) bị xoá / đổi tên ngoài app → hỏi *Lưu* / *Không lưu* / *Huỷ* (*Lưu* ghi lại đúng
  chỗ cũ, tạo lại thư mục nếu cần). Đóng tab cuối cùng = đóng Editor.
- **Đóng tất cả** (nút cuối thanh tab): còn ảnh chưa lưu → hỏi *Lưu tất cả...* (chọn 1 thư mục, lưu
  mọi ảnh chưa lưu vào đó, tên file = tên tab, trùng tên thì thêm " (2)" — không ghi đè file có sẵn) /
  *Đóng không lưu* / *Huỷ*. Xong thì đóng mọi tab và Editor, thư mục lưu tạm được dọn sạch. Huỷ chọn
  thư mục hoặc có ảnh lưu lỗi → không đóng gì.
- **Nhớ tab qua lần tắt/mở app**: đóng cửa sổ Editor (nút X / *Đóng*) không hỏi gì — mọi tab được lưu
  tạm và mở lại đúng như cũ ở lần mở app sau (shape vẫn chỉnh sửa được; lịch sử Undo thì không giữ).
  - Thư mục: `%TEMP%\AppSuite\ScreenCapture\Session\` — `session.json` (danh sách tab + mô tả shape + ảnh gốc trước khi Cắt, nếu có)
    và các file PNG (ảnh nền, ảnh dán).
  - **Không tích luỹ**: thư mục chỉ chứa đúng các tab đang mở ở lần lưu gần nhất. Đóng 1 tab → file
    của tab đó bị xoá ngay; đóng hết tab → thư mục rỗng. Ảnh của các phiên cũ hơn không còn trên ổ.
  - Giới hạn tối đa **30 tab / 300 MB**; vượt thì bỏ các tab cũ nhất khỏi bản lưu tạm.
  - Ghi tạm mỗi khi chụp ảnh mới, đóng tab và đóng cửa sổ.
  - Lưu ý: Windows (Storage Sense / Disk Cleanup) có thể dọn `%TEMP%`; tab nào mất file ảnh thì bỏ qua
    khi mở lại. Ảnh quan trọng vẫn nên *Lưu* ra file.

Thanh công cụ dạng **ribbon** kiểu PicPick, mỗi nhóm nút có nhãn phía dưới, chia thành các tab:

**Cách chọn & chỉnh sửa shape** (áp dụng với mọi công cụ, không cần bấm *Di chuyển* trước):

- **Vẽ/đặt xong là đang chỉnh sửa luôn**: shape vừa vẽ (hoặc stamp/text vừa đặt) được chọn ngay —
  hiện handle, tab contextual (vd *Number Stamp*) nếu có, đổi màu/cỡ nét áp luôn cho shape đó.
- **Bấm trúng 1 shape có sẵn** → chọn shape đó và kéo được ngay (di chuyển / kéo handle góc / kéo đầu
  mũi tên). Chữ nhật và Elip chỉ bắt khi bấm lên **viền**, nên vẫn vẽ được shape khác bên trong khung.
  Shape phủ cả 1 vùng (**ảnh dán**, Highlight, Mosaic / Blur): đang cầm công cụ vẽ thì bấm bên trong là vẽ chồng lên
  (dán 1 ảnh to vẫn vẽ thêm được); chọn / kéo chúng bằng *Di chuyển* (handle góc của shape đang chọn vẫn kéo được).
- **Bấm vào vùng trống** (hoặc nhấn `Esc`) → thoát chỉnh sửa. Với công cụ vẽ hình, bấm-kéo ở vùng
  trống thì vẽ shape mới luôn. Riêng *Text* và *Stamps*: nếu đang chọn shape, lần bấm vùng trống đầu
  chỉ bỏ chọn; lần bấm sau mới bắt đầu gõ chữ / đặt stamp. Bỏ chọn (bấm vùng trống / `Esc`) thì ribbon tự về tab
  *Trang chủ* để chọn công cụ khác. Ví dụ Number Stamps: bấm → stamp 1 (đang
  sửa, tab *Number Stamp*) → bấm ra ngoài (thoát sửa) → bấm → stamp 2 → ... Công cụ Stamps giữ nguyên
  cho tới khi chọn công cụ khác.
- **Kích thước stamp**: kéo handle góc để phóng to/thu nhỏ stamp (luôn giữ tròn/vuông). Stamp đặt
  tiếp theo dùng đúng kích thước stamp vừa chỉnh. Chọn lại stamp từ menu *Stamps* thì về kích thước
  mặc định (32px).
- *Tô màu* là thao tác trên pixel ảnh nên không chọn shape.

**Tab "Trang chủ"**

- **Chọn** — *Move* (icon con trỏ, là công cụ mặc định khi mở ảnh): chỉ chọn/kéo shape, bấm vùng
  trống không vẽ gì. Khi không chọn shape nào, quanh ảnh hiện **8 handle** (4 góc + 4 cạnh) — kéo để
  **đổi kích thước khung ảnh** giống PicPick: kéo ra = mở rộng, kéo vào = cắt bớt cạnh đó. Phần mở
  rộng hiện lại nội dung gốc đã bị Cắt trước đó (nếu có), ngoài phạm vi ảnh chụp thì tô trắng. Shape đã vẽ giữ nguyên vị trí so với nội dung ảnh; thanh trạng thái hiện kích thước mới;
  Undo được. *Select*: kéo chuột chọn 1 vùng chữ nhật trên ảnh (viền "kiến bò" đen/trắng, giữ
  `Shift` = vùng vuông) → hiện tab contextual **"Vùng chọn"** (xem dưới). Vùng đã chọn có 8 handle:
  kéo handle để chỉnh kích thước, kéo bên trong vùng để di chuyển; bấm ngoài vùng = chọn lại / bỏ chọn.
  *Xoá* (hoặc phím `Delete`/`Backspace`), *Lên trên* / *Xuống dưới* (đổi thứ tự lớp).
- **Vẽ hình** — Chữ nhật, Elip, Đường thẳng, Mũi tên, Bút, Highlight (marker tô trong mờ), Text, Chú thích.
  - *Text* — bấm vào ảnh rồi **gõ chữ ngay tại chỗ** (gõ được Unikey / IME tiếng Nhật): `Enter` xuống dòng; `Esc`,
    `Ctrl+Enter` hoặc bấm ra ngoài là xong (1 bước Undo). Sửa chữ đã có: nhấp đúp lên chữ, hoặc chọn rồi `F2` / `Enter`.
    Trong lúc gõ, ribbon tự mở tab *Định dạng* với phông / cỡ / màu của chữ đó — đổi gì áp luôn cho chữ đang gõ. Kéo
    handle góc = đổi **khung** chữ (chữ tự xuống dòng theo bề rộng, cỡ chữ giữ nguyên, như PicPick); giữ `Ctrl` khi kéo
    = đổi cỡ chữ. Chữ Nhật / Trung / Hàn mà phông đang chọn không có thì tự lấy phông Windows có ký tự đó.
  - *Chú thích* — bong bóng lời nói: kéo khung (hoặc bấm 1 cái = khung cỡ mặc định) rồi gõ chữ luôn; chữ tự xuống dòng
    theo bề rộng khung, khung tự cao thêm khi chữ dài. Khi đang chọn: kéo chấm tròn ở đầu đuôi để chỉ vào chỗ cần chú
    thích. Viền + chữ màu Color1, nền Color2.
  - *Bút* — vẽ tự do (khoanh tròn, gạch chân...), màu Color1, cỡ theo *Size*. Luôn vẽ nét mới kể cả
    khi bắt đầu trên hình khác; sửa nét đã vẽ (di chuyển, co giãn, đổi màu, xoá) bằng công cụ *Move*.
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
- **Che** — che thông tin nhạy cảm (mật khẩu, email, số tài khoản...) trước khi gửi ảnh. Chỉ che
  khi bạn chủ động dùng: chọn công cụ rồi kéo khung lên vùng cần che; không dùng thì ảnh giữ nguyên.
  - *Mosaic* — ô vuông pixel (mỗi ô = màu trung bình vùng bên dưới). *Size* = cỡ ô (Size 3 ≈ 10px,
    20 ≈ 44px).
  - *Làm mờ* — làm mờ Gauss. *Size* = độ mờ. Chữ nhỏ mà Size thấp có thể vẫn đoán được — nên để
    Size cao hoặc dùng Mosaic.
  - Vùng che là 1 shape: chọn lại để di chuyển / co giãn / đổi Size / xoá, Undo được, nhớ qua phiên
    làm việc. Ảnh xuất ra khi *Lưu* / *Copy* là ảnh đã che (ảnh gốc trong Editor vẫn giữ để sửa
    tiếp). Chỉ che ảnh chụp bên dưới, không che các shape (chữ, mũi tên...) vẽ trước đó ở cùng chỗ.
- **Chữ** — *Tìm chữ* (`Ctrl+F`): khung bên phải ảnh, đọc chữ trong ảnh (OCR, offline) rồi tìm — cùng cách tìm với
  *Tìm chữ* của ImageCompare (thư viện dùng chung `Common.Ocr`):
  - *Ngôn ngữ*: Tiếng Nhật (Windows OCR; máy chưa có gói OCR tiếng Nhật của Windows thì Tesseract dự phòng) hoặc Tiếng
    Việt / English (Tesseract). Nhớ qua lần mở app (`OcrLanguage` trong settings.json).
  - Mỗi ảnh đọc 1 lần (nhớ theo tab + ảnh + ngôn ngữ); ảnh đổi (cắt, xoay, hiệu ứng, Undo...) thì đọc lại. Chỉ đọc ảnh
    chụp, không đọc hình đã vẽ lên.
  - Ô tìm trống: danh sách mọi dòng đọc được. Gõ chữ: không phân biệt dấu / hoa thường (2 ô tick để bật), bỏ qua khoảng
    trắng, không thấy thì tự tìm gần đúng (sai / thiếu 1 ký tự, dấu `≈`). Chỗ khớp tô vàng đè lên ảnh (chỉ để xem), bấm 1
    dòng trong danh sách để cuộn tới; `Enter` / `Shift+Enter` = chỗ kế tiếp / trước, `Esc` = đóng khung.
  - *Copy chữ*: toàn bộ chữ của ảnh, mỗi dòng 1 dòng (chữ Nhật không chen dấu cách).
  - *Tô Highlight (N)*: mọi chỗ khớp thành hình Highlight vàng thật (1 bước Undo, lưu / copy kèm ảnh).
- **Cắt & Sửa** — *Cắt* (crop, shape nằm ngoài vùng cắt bị bỏ; **khôi phục được**: kéo handle khung
  ảnh ra lại là hiện lại phần đã cắt — kể cả sau khi tắt mở lại app), *Undo* / *Redo*
  từng bước, *Dán*
  (`Ctrl+V`): dán ảnh trong clipboard (ảnh copy từ app khác, hoặc file ảnh copy trong Explorer) thành
  1 đối tượng ảnh — đặt ở góc trên-trái vùng chọn (nếu có) hoặc phần ảnh đang nhìn thấy, được chọn sẵn
  để kéo / co giãn (giữ `Shift` = đúng tỉ lệ) / Flatten. Ảnh dán lớn hơn ảnh hiện tại → khung ảnh tự
  nới ra (nền trắng), cùng 1 bước Undo.
  - *Xoay* (menu): *Xoay phải 90°* (`Ctrl+R`), *Xoay trái 90°* (`Ctrl+Shift+R`), *Xoay 180°*, *Lật ngang*, *Lật dọc*,
    **Về hướng ban đầu** (xoay / lật ngược mọi lần Xoay / Lật trước đó trong 1 bước Undo — kể cả sau khi tắt mở lại app;
    mờ khi ảnh chưa xoay / lật), *Đổi cỡ ảnh…* (`Ctrl+E`: co giãn cả nội dung theo % hoặc px, giữ tỉ lệ hoặc không, tối
    đa 16384 px mỗi cạnh và 64 triệu pixel — khác
    kéo khung ảnh). Hình đã vẽ xoay / co giãn theo và vẫn sửa được; chữ và stamp giữ chiều đứng, stamp mũi tên tự đổi
    hướng. Phần đã Cắt trước đó không khôi phục được nữa sau khi xoay / đổi cỡ (Undo thì được).
  - *Hiệu ứng* (menu, chỉ áp lên ảnh nền — hình đã vẽ giữ nguyên; mỗi lần áp 1 bước Undo): *Độ sáng / tương phản…*,
    *Làm xám*, *Sepia*, *Đảo màu*, *Làm nét*, *Viền ảnh…* (màu, dày 1–60 px), *Đổ bóng…* (độ lan / độ đậm, nền quanh bóng
    trong suốt), *Mép rách…* (chọn cạnh, độ sâu), *Watermark…* (chữ hoặc ảnh logo; giữa / 4 góc / lặp chéo; độ đục).
    Mục có "…" mở hộp thoại có xem trước.
  - **Nền trong suốt**: ảnh mới nền *Trong suốt*, hoặc ảnh đã Đổ bóng / Mép rách — phần trong suốt hiện ô caro; nới
    khung, xoá vùng, Cut tô trong suốt thay vì trắng. Lưu PNG / Copy giữ trong suốt; JPG / BMP nền trắng.
- **Màu & Cỡ nét** — *Color1* (màu nét/màu chính) / *Color2* (màu fill/highlight), color picker.
  *Size* — slider 1-20px. Đổi màu / Size khi đang chọn 1 shape sẽ áp luôn cho shape đó.
- Ribbon vừa cửa sổ rộng ~1100px logic (vd màn 1920px ở 150%). Cửa sổ hẹp hơn: rê chuột lên ribbon
  hiện thanh cuộn ngang, lăn chuột để tới các nhóm bên phải.

**Zoom** (góc phải thanh trạng thái): *Vừa cửa sổ* / `−` / `100% ▾` (25–800%) / `+`; `Ctrl` + lăn
chuột zoom quanh con trỏ; `Ctrl++` / `Ctrl+-` / `Ctrl+0`. Mỗi tab nhớ mức zoom riêng (không lưu qua
phiên); zoom không ảnh hưởng ảnh khi lưu / copy. Phóng to hiện rõ từng pixel. Ảnh lớn có giới hạn
zoom tối đa (ảnh 1920×1080 ≈ 400%) để không tốn quá nhiều bộ nhớ. Cạnh ô zoom là ô **"Ảnh x / n"** (tab đang xem / số
tab đang mở).

**Tab "Tệp"** — *Mở*, *Ảnh mới*; *Lưu* (`Ctrl+S`): tab đã gắn với 1 file (đã lưu, tự lưu, hoặc mở từ PNG / JPG / BMP) →
ghi đè file đó, không hỏi; chưa có → như *Lưu thành…* (`Ctrl+Shift+S`): chọn nơi lưu + định dạng PNG / JPG / BMP (chọn sẵn
định dạng của file hiện tại, chưa có thì PNG; gõ tên có đuôi `.jpg` / `.bmp` thì theo đuôi đó), tên điền sẵn = tên file
hoặc tên tab (vd `2026-09-24 15 31 59`). JPG / BMP ghép nền trắng; chất lượng JPG trong Cài đặt > Lưu ảnh. File gắn
với tab được nhớ qua phiên làm việc. Ghi ra file tạm rồi mới thay file thật (lưu lỗi không làm hỏng file cũ; file đang
bị app khác khoá → báo ở thanh trạng thái),
*Copy* ảnh (đã gộp mọi shape) vào clipboard, *Cài đặt*, *Hướng dẫn*, *Đóng* cửa sổ.

**Tab "Vùng chọn"** (contextual) — tự hiện khi dùng *Select* chọn 1 vùng, tự ẩn khi bỏ chọn:

- *Cắt ảnh* (`Enter`) — cắt ảnh còn đúng vùng chọn (shape nằm ngoài vùng bị bỏ).
- *Copy* (`Ctrl+C`) — copy vùng (ảnh + shape đang thấy) vào clipboard.
- *Cut* (`Ctrl+X`) — copy vùng rồi tô trắng vùng đó trên ảnh nền.
- *Xoá vùng* (`Delete`) — tô trắng vùng đó trên ảnh nền.
- *Copy chữ* — đọc chữ trong vùng (OCR, ngôn ngữ theo khung *Tìm chữ*) rồi copy vào clipboard; ảnh đã đọc chữ thì lấy
  luôn các chữ nằm trong vùng, không đọc lại.
- *Bỏ chọn* (`Esc`).

Cut/Xoá vùng chỉ đổi pixel ảnh nền, shape (mũi tên, chữ...) nằm trong vùng vẫn giữ nguyên. Tất cả
Undo được.

**Tab "Định dạng"** (contextual) — tự hiện khi chọn (hoặc đang vẽ / đang gõ) chữ, khung chú thích, chữ nhật, elip,
đường, mũi tên, nét bút, hoặc khi đang cầm công cụ vẽ chúng; chỉ hiện các nhóm hợp với loại hình đó. Vẽ xong / bắt đầu
gõ chữ là tự chuyển sang tab này; bỏ chọn thì về *Trang chủ*. Đổi gì áp luôn cho hình đang chọn (Undo được), đồng thời là
định dạng cho hình vẽ tiếp theo bằng công cụ đang cầm (mỗi công cụ nhớ riêng).

- **Màu & Cỡ nét** — dùng chung với tab Trang chủ (đổi màu chữ / nét không phải quay lại).
- **Phông chữ** — phông (gõ vài chữ đầu để nhảy tới; mặc định Segoe UI), **cỡ chữ** (ô xổ xuống như Word: 8…128, hoặc
  gõ số 6–400 rồi `Enter`), đậm (B), nghiêng (I).
- **Nền & viền chữ** — nền ô chữ (Color2), viền quanh từng nét chữ + màu viền (chữ rõ trên ảnh nhiều màu).
- **Hình** — tô nền (Color2) cho chữ nhật / elip / khung chú thích, bo góc.
- **Nét & độ trong suốt** — nét liền / đứt / chấm, độ đục 10–100%.
- **Đầu mũi tên** — kiểu đầu ở 2 đầu đường / mũi tên: không có, tam giác, chữ V, chấm tròn (vd mũi tên 2 đầu).

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
| `Ctrl+S` | Lưu (ghi đè file đang gắn với tab; chưa có thì hỏi nơi lưu) |
| `Ctrl+Shift+S` | Lưu thành… (PNG / JPG / BMP) |
| `Ctrl+C` | Copy ảnh (đã gộp mọi shape) vào clipboard |
| `Ctrl+V` | Dán ảnh từ clipboard |
| `Ctrl+N` | Ảnh mới (ảnh trống thành tab mới) |
| `Ctrl+O` | Mở ảnh có sẵn thành tab mới |
| `Ctrl+R` / `Ctrl+Shift+R` | Xoay ảnh phải / trái 90° |
| `Ctrl+E` | Đổi cỡ ảnh… |
| `Ctrl+F` | Tìm chữ trong ảnh (trong ô tìm: `Enter` / `Shift+Enter` = chỗ khớp kế tiếp / trước, `Esc` = đóng) |
| Nhấp đúp lên chữ / `F2` | Sửa chữ ngay trên ảnh (`Esc` / `Ctrl+Enter` = xong) |
| `Ctrl` + kéo góc chữ | Đổi cỡ chữ (kéo thường = đổi khung chữ) |
| `Delete` / `Backspace` | Xoá shape đang chọn |
| `Esc` | Bỏ chọn shape (thoát chỉnh sửa) / bỏ vùng chọn |
| Khi có vùng chọn (*Select*): `Ctrl+C` / `Ctrl+X` / `Delete` / `Enter` | Copy / Cut / Xoá vùng / Cắt ảnh theo vùng |
| Giữ `Shift` khi vẽ/kéo | Khoá góc 45° (đường/mũi tên), vuông/tròn (chữ nhật/elip) |
| `Ctrl` + lăn chuột / `Ctrl++` / `Ctrl+-` / `Ctrl+0` | Zoom quanh con trỏ / phóng to / thu nhỏ / về 100% |
| `Ctrl+W` / `Ctrl+F4` | Đóng tab đang mở (ảnh chưa lưu thì hỏi Lưu / Không lưu / Huỷ; tab cuối = đóng Editor) |
| `F1` | Mở cửa sổ Hướng dẫn |

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
- **Tìm chữ** (`Views/EditorWindow.TextSearch.cs`): OCR + tìm chữ dùng thư viện chung `Common.Ocr` (cùng ImageCompare);
  csproj import `Common.Ocr.targets` để chép native Tesseract x64 + tessdata + VC++ runtime cạnh exe (bản publish nặng
  thêm ~10 MB). Kết quả đọc nhớ theo tab (`ConditionalWeakTable`), đọc trên bản sao ảnh ở luồng nền.
- Không có DI container, giống mọi module khác trong AppSuite — service khởi tạo thủ công trong
  code-behind của View.
- **Log**: `Logs\screencapture-yyyy-MM-dd.log` cạnh exe (`Services/AppLog.cs`, qua
  `RollingFileLoggerProvider` của Common — cùng định dạng với MainLauncher), giữ 14 ngày. Ghi lỗi bị
  nuốt (lưu cài đặt / phiên / tự lưu / clipboard / chụp từ phím tắt), lỗi không xử lý được (thay cho
  `crash.log` cũ), phím tắt bị chiếm, kết quả mỗi lần chụp và chụp cuộn. Gửi kèm file này khi báo lỗi.

Xem `PLAN.md` để biết đầy đủ quyết định thiết kế, rủi ro chưa kiểm chứng bằng chạy thực tế, và danh
sách việc chưa làm.
