namespace ScreenCapture.Models;

/// <summary>1 tính năng trong cửa sổ Hướng dẫn. <paramref name="Keys"/>: phím tắt / thao tác chuột hiện
/// thành các "phím" nhỏ (vd "Ctrl+Z"), null nếu không có.</summary>
public sealed record HelpItem(string Name, string Description, params string[] Keys);

public sealed record HelpSection(string Title, string Glyph, string Summary, IReadOnlyList<HelpItem> Items);

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút "Hướng dẫn" / F1) - liệt kê mọi tính năng của ScreenCapture cho
/// người dùng cuối. Nguồn: README.md mục "Chức năng"; thêm/đổi tính năng thì cập nhật cả 2 chỗ.
/// Riêng phím tắt chụp lấy theo cài đặt hiện tại (người dùng có thể đã đổi).
/// </summary>
public static class HelpContent
{
    /// <summary>Tên hiển thị của từng phím tắt toàn cục (dùng chung với trang Phím tắt của Cài đặt).</summary>
    public static readonly (HotkeyAction Action, string Label)[] HotkeyLabels =
    [
        (HotkeyAction.FullScreen, "Chụp toàn màn hình"),
        (HotkeyAction.CurrentMonitor, "Chụp màn hình hiện tại"),
        (HotkeyAction.ActiveWindow, "Chụp cửa sổ hiện tại"),
        (HotkeyAction.Region, "Chụp vùng chọn"),
        (HotkeyAction.FixedRegion, "Chụp vùng cố định"),
        (HotkeyAction.ScrollCapture, "Chụp cuộn dọc"),
        (HotkeyAction.ScrollCaptureHorizontal, "Chụp cuộn ngang"),
        (HotkeyAction.RepeatLast, "Chụp lại lần gần nhất"),
    ];

    public static IReadOnlyList<HelpSection> Build(AppSettings settings) =>
    [
        new("Bắt đầu nhanh", "\uE768", "Chụp → chỉnh sửa → lưu hoặc copy, chỉ trong vài bước.",
        [
            new("1. Chụp", "Bấm 1 thẻ ở cửa sổ chính (Toàn màn hình, Màn hình hiện tại, Cửa sổ hiện tại, Vùng chọn...), dùng phím tắt, hoặc click phải icon ở khay hệ thống. Cửa sổ ScreenCapture tự thu nhỏ để không lọt vào ảnh."),
            new("2. Chỉnh sửa", "Ảnh mở ngay trong Editor: vẽ khung, mũi tên, chữ, đánh số, che thông tin nhạy cảm, cắt ảnh... Mọi thao tác đều Undo được."),
            new("3. Lưu hoặc chia sẻ", "Lưu (PNG / JPG / BMP), hoặc copy ảnh vào clipboard rồi dán vào chat/email/tài liệu.", "Ctrl+S", "Ctrl+C"),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn ở cửa sổ chính, tab Tệp hoặc nút ? của Editor, menu khay, hoặc phím F1.", "F1"),
        ]),

        new("Chế độ chụp", "\uE722", "Các kiểu chụp ở cửa sổ chính, menu khay và phím tắt.",
        [
            new("Toàn màn hình", "Chụp toàn bộ màn hình - gồm mọi màn hình nếu dùng nhiều màn hình."),
            new("Màn hình hiện tại", "Chụp 1 màn hình - màn đang có con trỏ chuột (dùng nhiều màn hình). Mặc định chưa có phím tắt - gán trong Cài đặt > Phím tắt."),
            new("Cửa sổ hiện tại", "Chụp cửa sổ đang active của bất kỳ ứng dụng nào (kể cả trình duyệt, app đồ hoạ)."),
            new("Vùng chọn", "Kéo-thả chọn 1 vùng, thả chuột là chụp ngay. Hoặc di chuột lên 1 cửa sổ (được tô viền) rồi click (không kéo) để chụp đúng khung cửa sổ đó.", "Kéo chuột", "Click"),
            new("Vùng cố định", "Chọn vùng, chỉnh kích thước bằng 4 handle góc, nhấn Enter để chụp (Esc để huỷ). Vùng được nhớ lại cho lần sau, kể cả khi tắt mở lại app.", "Enter", "Esc"),
            new("Chụp cuộn dọc", "Kéo chọn vùng nội dung cần cuộn (trang web, tài liệu, danh sách...). App tự lăn chuột, chụp từng khung và ghép thành 1 ảnh dài. Dừng khi tới cuối trang, khi bấm Esc hoặc chạm giới hạn trong Cài đặt > Chụp cuộn. Nên để thanh menu cố định nằm ngoài vùng chọn.", "Esc"),
            new("Chụp cuộn ngang", "Như cuộn dọc nhưng cuộn sang phải và ghép thành 1 ảnh rộng (bảng tính, timeline...). Cửa sổ không cuộn ngang được bằng bánh xe thì app tự dùng Shift + lăn chuột. Nên để cột cố định bên trái nằm ngoài vùng chọn."),
            new("Chụp lại lần gần nhất", "Lặp lại kiểu chụp gần nhất; với vùng chọn / vùng cố định thì chụp lại đúng vùng đó ngay. Mặc định chưa có phím tắt - gán trong Cài đặt > Phím tắt; hoặc thẻ \"Chụp lại lần trước\" ở cửa sổ chính."),
            new("Hẹn giờ trước khi chụp", "Chờ 0-10 giây rồi mới chụp, để kịp mở menu / tooltip cần chụp. Đặt trong Cài đặt > Chung."),
        ]),

        new("Phím tắt chụp", "\uE765", "Dùng được cả khi app đang thu nhỏ, ẩn ở khay hoặc đang làm việc ở app khác. Đổi trong Cài đặt > Phím tắt.",
        [
            .. HotkeyLabels.Select(h =>
            {
                var binding = settings.GetHotkey(h.Action);
                return binding.IsEnabled
                    ? new HelpItem(h.Label, "Phím tắt toàn cục đang cài đặt.", binding.Describe())
                    : new HelpItem(h.Label, "Chưa gán phím - gán trong Cài đặt > Phím tắt.");
            }),
            new("Phím bị app khác chiếm", "Phím đã bị app khác giữ (vd PicPick đang chạy) được đánh dấu ⚠ trong Cài đặt > Phím tắt và có thông báo ở cửa sổ chính. Phím PrintScreen đơn lẻ không hoạt động khi Windows 11 bật \"Use the Print screen key to open screen capture\" - tắt tuỳ chọn đó hoặc dùng tổ hợp có Shift/Ctrl/Alt."),
        ]),

        new("Khay hệ thống", "\uE7F4", "ScreenCapture chạy ngầm ở khay (góc phải taskbar, có thể nằm trong nhóm icon ẩn ^).",
        [
            new("Click trái icon", "Mở cửa sổ chính."),
            new("Click phải icon", "Menu: các kiểu chụp, Mở cửa sổ chính, Mở Editor, Hướng dẫn, Cài đặt, Thoát."),
            new("Bấm X ở cửa sổ chính", "Chỉ ẩn xuống khay - phím tắt vẫn dùng được. Thoát hẳn bằng Thoát ở menu khay."),
            new("Khởi động cùng Windows", "Bật trong Cài đặt > Chung: app chạy ngầm ở khay khi đăng nhập Windows, không bật cửa sổ chính."),
            new("Tắt chạy ngầm", "Tắt \"Chạy ngầm ở khay hệ thống\" trong Cài đặt > Chung: không có icon ở khay, bấm X là thoát hẳn app."),
        ]),

        new("Editor: tab ảnh & phiên làm việc", "\uE8A5", "Chỉ có 1 cửa sổ Editor; mỗi lần chụp mở thêm 1 tab.",
        [
            new("Nhiều ảnh dạng tab", "Mỗi ảnh chụp là 1 tab (tên = thời điểm chụp). Mỗi tab giữ riêng hình đã vẽ, lịch sử Undo và mức zoom; công cụ / màu / cỡ nét dùng chung. Góc phải thanh trạng thái ghi ảnh đang mở là ảnh thứ mấy trên tổng số tab (vd \"Ảnh 3 / 6\")."),
            new("Mở ảnh có sẵn", "Tab Tệp > Mở (hoặc nút Mở ảnh ở cửa sổ chính, hoặc kéo-thả file vào Editor / cửa sổ chính): mở ảnh PNG, JPG, BMP, GIF (khung đầu), WEBP thành tab mới để ghi chú - chọn được nhiều file, mỗi file 1 tab, tên tab = tên file. Ảnh chụp điện thoại tự xoay đúng chiều theo EXIF. Tab mở từ file coi như đã lưu: không sửa gì thì đóng không hỏi. Lưu (Ctrl+S) ghi đè chính file đó (ảnh gốc mất - muốn giữ thì dùng Lưu thành…); GIF / WEBP thì Lưu hỏi nơi lưu mới.", "Ctrl+O"),
            new("Ảnh mới", "Tab Tệp > Ảnh mới (hoặc nút Ảnh mới ở cửa sổ chính): tạo ảnh trống thành 1 tab - chọn cỡ theo mẫu (ảnh trong clipboard, ảnh đang mở, màn hình chính, 640×480 … 1920×1080) hoặc nhập rộng / cao (nút ⇄ đổi ngang ↔ dọc), và màu nền (Trắng, Đen, màu bất kỳ - nhớ cho lần sau). Dùng làm nền để ghép nhiều ảnh dán vào.", "Ctrl+N"),
            new("Đóng 1 tab", "Bấm × trên tab, hoặc Ctrl+W / Ctrl+F4 để đóng tab đang mở. Ảnh chưa lưu, đã sửa sau lần lưu cuối, hoặc file đã lưu (kể cả ảnh tự lưu) bị xoá / đổi tên → hỏi Lưu / Không lưu / Huỷ (Lưu = ghi lại đúng chỗ cũ). Đóng tab cuối cùng = đóng Editor.", "Ctrl+W", "Ctrl+F4"),
            new("Đóng tất cả", "Nút cuối thanh tab. Còn ảnh chưa lưu → hỏi Lưu tất cả (chọn 1 thư mục, tên file = tên tab, không ghi đè file có sẵn) / Đóng không lưu / Huỷ."),
            new("Nhớ tab khi tắt app", "Đóng cửa sổ Editor không hỏi gì: mọi tab được lưu tạm và mở lại y như cũ ở lần sau (hình đã vẽ vẫn sửa được, lịch sử Undo thì không giữ). Tối đa 30 tab / 300 MB, đổi hoặc tắt trong Cài đặt > Phiên làm việc."),
            new("Lưu ý thư mục tạm", "Tab được lưu tạm trong %TEMP% - Windows có thể dọn thư mục này. Ảnh quan trọng vẫn nên Lưu ra file."),
        ]),

        new("Chọn & chỉnh sửa hình", "\uE7C9", "Áp dụng với mọi công cụ - không cần bấm Move trước.",
        [
            new("Vẽ xong là đang sửa", "Hình vừa vẽ (hoặc stamp / chữ vừa đặt) được chọn ngay: kéo handle, đổi màu / cỡ nét áp luôn cho hình đó."),
            new("Bấm trúng hình có sẵn", "Chọn hình đó và kéo được ngay: di chuyển, kéo handle góc, kéo đầu mũi tên. Chữ nhật và Elip chỉ bắt khi bấm lên viền, nên vẫn vẽ được hình khác bên trong. Ảnh dán, Highlight, Mosaic / Blur phủ cả 1 vùng: đang cầm công cụ vẽ thì bấm bên trong là vẽ chồng lên - chọn / kéo chúng bằng Move.", "Click", "Kéo"),
            new("Bỏ chọn", "Bấm vào vùng trống hoặc nhấn Esc.", "Esc"),
            new("Move", "Công cụ mặc định: chỉ chọn / kéo hình, bấm vùng trống không vẽ gì. Khi không chọn hình nào, quanh ảnh có 8 handle để đổi kích thước khung ảnh (xem mục Cắt, khung ảnh & dán)."),
            new("Xoá, đổi thứ tự lớp", "Xoá hình đang chọn; Lên trên / Xuống dưới để đưa hình lên trên hoặc xuống dưới hình khác.", "Delete", "Backspace"),
            new("Undo / Redo", "Mọi thao tác (vẽ, di chuyển, đổi kích thước, đổi màu, cắt, tô màu, xoá...) đều hoàn tác được từng bước.", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z"),
        ]),

        new("Vẽ hình", "\uE70F", "Nhóm Vẽ hình ở tab Trang chủ. Màu nét = Color1, màu nền = Color2, độ dày = Size (1-20px).",
        [
            new("Chữ nhật, Elip", "Kéo để vẽ khung. Giữ Shift = hình vuông / hình tròn (cả lúc vẽ lẫn lúc kéo handle).", "Shift"),
            new("Đường thẳng, Mũi tên", "Kéo từ điểm đầu tới điểm cuối. Giữ Shift = khoá hướng theo bội số 45°. Khi đang chọn: bấm gần 1 đầu rồi kéo để đổi hướng / độ dài, bấm khúc giữa để di chuyển cả đường.", "Shift"),
            new("Bút", "Vẽ tự do (khoanh tròn, gạch chân...). Luôn vẽ nét mới kể cả khi bắt đầu trên hình khác; sửa nét đã vẽ bằng Move."),
            new("Highlight", "Bút dạ quang tô trong mờ lên chữ cần làm nổi bật."),
            new("Text", "Bấm vào ảnh rồi gõ chữ ngay tại chỗ - Enter xuống dòng; Esc, Ctrl+Enter hoặc bấm ra ngoài là xong. Sửa chữ đã có: nhấp đúp lên chữ (hoặc chọn rồi F2 / Enter). Kéo handle góc của chữ = đổi khung chữ (chữ tự xuống dòng theo bề rộng khung, cỡ chữ giữ nguyên, như PicPick); giữ Ctrl khi kéo = đổi cỡ chữ. Chữ Nhật / Trung / Hàn tự lấy phông Windows có ký tự đó.", "Nhấp đúp", "F2", "Esc", "Ctrl+Enter", "Ctrl + kéo góc"),
            new("Khung chú thích", "Bong bóng lời nói: kéo khung (hoặc bấm 1 cái để có khung cỡ mặc định) rồi gõ chữ luôn - chữ tự xuống dòng theo bề rộng khung, khung tự cao thêm khi chữ dài. Khi đang chọn: kéo chấm tròn ở đầu đuôi để chỉ vào chỗ cần chú thích. Viền + chữ màu Color1, nền Color2."),
            new("Màu & cỡ nét", "Color1 (màu nét / màu chữ), Color2 (màu nền: Highlight, tô nền hình, nền ô chữ, nền khung chú thích), Size. Đổi khi đang chọn 1 hình sẽ áp luôn cho hình đó."),
        ]),

        new("Tab Định dạng", "", "Tự hiện khi chọn chữ, khung chú thích, chữ nhật, elip, đường, mũi tên, nét bút - hoặc khi đang cầm công cụ vẽ chúng. Chỉ hiện các nhóm hợp với loại hình đó.",
        [
            new("Áp cho hình nào", "Đang chọn 1 hình: đổi gì áp luôn cho hình đó (Undo được). Đồng thời là định dạng cho các hình vẽ tiếp theo bằng công cụ đang cầm - mỗi công cụ nhớ riêng (vd Đường thẳng và Mũi tên có đầu mũi tên khác nhau)."),
            new("Phông chữ", "Chọn phông (gõ vài chữ đầu để nhảy tới), cỡ chữ, chữ đậm (B), nghiêng (I) - cho Text và Khung chú thích."),
            new("Nền & viền chữ", "Nền ô chữ: ô màu Color2 phía sau chữ. Viền chữ: viền quanh từng nét chữ (chọn màu ở ô bên cạnh) - chữ đọc rõ trên ảnh nhiều màu."),
            new("Hình", "Tô nền (màu Color2) cho Chữ nhật / Elip / Khung chú thích; hình có tô nền thì bấm đâu bên trong cũng chọn được. Bo góc cho Chữ nhật / Khung chú thích."),
            new("Nét & độ trong suốt", "Nét liền / đứt / chấm. Độ đục 10-100%: hình mờ đi để vẫn thấy ảnh bên dưới."),
            new("Đầu mũi tên", "Kiểu đầu ở điểm đầu và điểm cuối của Đường / Mũi tên: không có, tam giác, chữ V, chấm tròn - vd mũi tên 2 đầu. Đầu mũi tên to theo Size."),
            new("Màu & cỡ nét", "Nhóm Color1 / Color2 / Size cũng nằm đầu tab này - không phải quay về Trang chủ."),
        ]),

        new("Tô màu & Stamps", "\uE790", "Nhóm Tô & Dấu ở tab Trang chủ.",
        [
            new("Tô màu", "Click vào 1 vùng liền màu trên ảnh để đổi màu cả vùng đó (giống Paint). Đây là thao tác trên pixel ảnh, không tạo hình để chọn lại."),
            new("Number Stamps", "Hình tròn có số, 7 màu. Số tự tăng mỗi lần đặt (1, 2, 3...) - tiện đánh dấu các bước. Bấm ra vùng trống để thoát sửa stamp vừa đặt, bấm tiếp để đặt stamp kế."),
            new("General Stamps", "Mũi tên 8 hướng, bookmark, pin, cờ, tag, info, cảnh báo, cấm, tim, cộng, trừ, check, dấu X, ngôi sao - theo màu Color1."),
            new("Kích thước stamp", "Kéo handle góc để phóng to / thu nhỏ (luôn giữ tròn / vuông). Stamp đặt tiếp theo dùng đúng kích thước vừa chỉnh; chọn lại từ menu Stamps thì về mặc định."),
        ]),

        new("Tab Number Stamp", "\uEA3B", "Tự hiện khi chọn 1 Number Stamp đã đặt, tự ẩn khi bỏ chọn.",
        [
            new("Current / Next", "Current: sửa số của stamp đang chọn. Next: số dùng cho stamp đặt tiếp theo. Có nút − / + hoặc gõ số trực tiếp."),
            new("Shape Styles", "Đổi nhanh màu nền stamp theo 7 màu có sẵn."),
            new("Outline / Fill", "Màu viền và màu nền riêng cho stamp đang chọn."),
            new("Flatten", "Gộp stamp vào ảnh (không chỉnh được nữa)."),
            new("Xoá, Lên trên / Xuống dưới", "Xoá stamp hoặc đổi thứ tự lớp."),
        ]),

        new("Che thông tin nhạy cảm", "\uE72E", "Nhóm Che ở tab Trang chủ - che mật khẩu, email, số tài khoản... trước khi gửi ảnh.",
        [
            new("Mosaic", "Kéo khung lên vùng cần che → vùng đó thành các ô vuông pixel. Size = cỡ ô."),
            new("Làm mờ", "Kéo khung lên vùng cần che → làm mờ. Size = độ mờ. Chữ nhỏ mà Size thấp có thể vẫn đoán được - nên để Size cao hoặc dùng Mosaic."),
            new("Sửa vùng che", "Vùng che là 1 hình: chọn lại để di chuyển / co giãn / đổi Size / xoá, Undo được. Ảnh khi Lưu / Copy là ảnh đã che."),
            new("Lưu ý", "Chỉ che ảnh chụp bên dưới, không che các hình (chữ, mũi tên...) đã vẽ ở cùng chỗ."),
        ]),

        new("Cắt, khung ảnh & dán", "\uE7A8", "Nhóm Cắt & Sửa ở tab Trang chủ: Cắt, Xoay (xoay / lật / đổi cỡ ảnh), Hiệu ứng, Undo / Redo, Dán.",
        [
            new("Cắt", "Kéo khung vùng cần giữ; hình nằm ngoài vùng cắt bị bỏ. Khôi phục được: kéo handle khung ảnh ra lại là hiện lại phần đã cắt, kể cả sau khi tắt mở lại app."),
            new("Đổi kích thước khung ảnh", "Công cụ Move, không chọn hình nào → kéo 1 trong 8 handle quanh ảnh: kéo ra = mở rộng (nền trắng, ảnh nền trong suốt thì trong suốt), kéo vào = cắt bớt cạnh đó. Hình đã vẽ giữ nguyên vị trí."),
            new("Xoay / lật ảnh", "Nút Xoay: xoay phải / trái 90°, xoay 180°, lật ngang, lật dọc cả ảnh. Các hình đã vẽ xoay theo và vẫn sửa được; chữ và stamp giữ chiều đứng, stamp mũi tên tự đổi hướng. Về hướng ban đầu: xoay / lật ngược lại mọi lần Xoay / Lật trước đó trong 1 bước (cả sau khi mở lại app), hình vẽ đi theo ảnh. Phần đã Cắt trước đó không khôi phục được nữa (Undo thì được).", "Ctrl+R", "Ctrl+Shift+R"),
            new("Đổi cỡ ảnh", "Nút Xoay > Đổi cỡ ảnh…: co giãn cả nội dung ảnh theo % hoặc theo px (giữ tỉ lệ hoặc không) - khác kéo khung ảnh. Hình vẽ, nét, cỡ chữ, stamp co giãn theo.", "Ctrl+E"),
            new("Dán ảnh", "Dán ảnh trong clipboard (ảnh copy từ app khác hoặc file ảnh copy trong Explorer) thành 1 hình ảnh, kéo / co giãn được (giữ Shift = đúng tỉ lệ). Ảnh dán lớn hơn thì khung ảnh tự nới ra.", "Ctrl+V"),
            new("Nền trong suốt", "Ảnh mới chọn màu nền \"Trong suốt\", hoặc ảnh đã Đổ bóng / Mép rách: phần trong suốt hiện ô caro trong Editor. Nới khung, xoá vùng, Cut của ảnh đó tô trong suốt thay vì trắng. Lưu PNG / copy giữ nền trong suốt; JPG / BMP nền trắng."),
        ]),

        new("Hiệu ứng ảnh", "", "Nút Hiệu ứng (nhóm Cắt & Sửa). Chỉ áp lên ảnh nền - các hình đã vẽ giữ nguyên, vẫn sửa được. Mỗi lần áp là 1 bước Undo.",
        [
            new("Có hộp thoại xem trước", "Mục có \"…\" mở hộp thoại: chỉnh tuỳ chọn bên trái, xem trước bên phải (kể cả các hình đã vẽ), bấm Áp dụng. Tuỳ chọn lần trước được nhớ trong lúc app chạy."),
            new("Viền ảnh", "Viền màu bất kỳ, dày 1-60 px bao quanh ảnh; ảnh to thêm, các hình dời theo."),
            new("Đổ bóng", "Bóng mờ xuống dưới-phải, chỉnh độ lan / độ đậm; nền quanh bóng trong suốt. Làm Mép rách trước rồi Đổ bóng thì bóng theo đúng răng cưa."),
            new("Mép rách", "Xé răng cưa các cạnh chọn (trên / dưới / trái / phải), chỉnh độ sâu; phần bị xé thành trong suốt."),
            new("Độ sáng / tương phản", "2 thanh trượt -100 … +100."),
            new("Làm xám, Sepia, Đảo màu, Làm nét", "Áp ngay, không hỏi. Làm nét lặp lại nhiều lần để nét hơn."),
            new("Watermark", "Đóng dấu chữ (phông, cỡ, đậm, màu) hoặc ảnh (logo, chỉnh cỡ %) mờ lên ảnh: ở giữa, 1 trong 4 góc, hoặc lặp kín ảnh theo đường chéo; chỉnh độ đục."),
        ]),

        new("Vùng chọn", "\uE8B3", "Công cụ Select + tab contextual \"Vùng chọn\" (tự hiện khi có vùng chọn).",
        [
            new("Chọn vùng", "Công cụ Select: kéo chuột chọn 1 vùng chữ nhật (giữ Shift = vùng vuông). Kéo handle để chỉnh kích thước, kéo bên trong để di chuyển, bấm ngoài để chọn lại.", "Kéo chuột", "Shift"),
            new("Cắt ảnh theo vùng", "Cắt ảnh còn đúng vùng chọn.", "Enter"),
            new("Copy vùng", "Copy vùng (ảnh + hình đang thấy) vào clipboard.", "Ctrl+C"),
            new("Cut vùng", "Copy vùng rồi tô trắng vùng đó trên ảnh nền.", "Ctrl+X"),
            new("Xoá vùng", "Tô trắng vùng đó trên ảnh nền (hình vẽ bên trong vẫn giữ).", "Delete"),
            new("Bỏ chọn", "Bỏ vùng chọn.", "Esc"),
        ]),

        new("Zoom", "\uE71E", "Góc dưới bên phải Editor. Mỗi tab nhớ mức zoom riêng; zoom không ảnh hưởng ảnh khi lưu / copy.",
        [
            new("Phóng to / thu nhỏ", "Nút + / − ở góc phải thanh trạng thái, hoặc phím tắt.", "Ctrl++", "Ctrl+-"),
            new("Zoom quanh con trỏ chuột", "Giữ Ctrl rồi lăn chuột trên ảnh - điểm dưới con trỏ đứng yên.", "Ctrl+lăn chuột"),
            new("Về 100%", "Kích thước thật của ảnh.", "Ctrl+0"),
            new("Vừa cửa sổ, chọn mức nhanh", "Nút Vừa cửa sổ, hoặc bấm ô % để chọn 25%-800%. Ảnh lớn có giới hạn zoom tối đa (ảnh 1920×1080 ≈ 400%) để không tốn quá nhiều bộ nhớ."),
            new("Vẽ khi đang zoom", "Mọi công cụ vẫn đặt đúng vị trí trên ảnh; handle luôn cùng cỡ trên màn hình. Phóng to thấy rõ từng pixel để vẽ chính xác."),
        ]),

        new("Lưu & chia sẻ", "\uE74E", "Tab Tệp của Editor và các tuỳ chọn tự động.",
        [
            new("Lưu", "Tab đã gắn với 1 file (đã lưu trước đó, hoặc mở từ PNG / JPG / BMP) → ghi đè file đó, không hỏi. Ảnh chụp chưa lưu → hỏi nơi lưu như Lưu thành. Nhớ qua lần tắt mở app.", "Ctrl+S"),
            new("Lưu thành…", "Chọn nơi lưu và định dạng PNG / JPG / BMP (chọn sẵn định dạng của file hiện tại, chưa có thì PNG); tên điền sẵn = tên file hoặc tên tab. JPG / BMP ghép nền trắng. Chất lượng JPG: Cài đặt > Lưu ảnh (mặc định 90).", "Ctrl+Shift+S"),
            new("Copy ảnh", "Copy ảnh (đã gộp mọi hình vẽ, vùng che) vào clipboard để dán sang app khác.", "Ctrl+C"),
            new("Tự động lưu", "Cài đặt > Lưu ảnh: mỗi ảnh chụp tự lưu thành PNG vào 1 thư mục (mặc định Pictures\\ScreenCapture), tên theo mẫu (mặc định {date}_{time}_{app} → 2026-10-07_10-31-10_EXCEL.png - đổi ở Cài đặt > Lưu ảnh > Mẫu tên file); đóng tab không hỏi lại."),
            new("Tự copy sau khi chụp", "Cài đặt > Chung: chụp xong tự copy ảnh vào clipboard."),
        ]),

        new("Phím tắt trong Editor", "\uE92E", "Cũng hiện trong tooltip của nút tương ứng. Khi đang gõ trong ô số (Current/Next), phím thuộc về ô đó.",
        [
            new("Undo", "Hoàn tác bước vừa làm.", "Ctrl+Z"),
            new("Redo", "Làm lại bước vừa hoàn tác.", "Ctrl+Y", "Ctrl+Shift+Z"),
            new("Lưu", "Lưu vào file đang gắn với tab (chưa có thì hỏi).", "Ctrl+S"),
            new("Lưu thành…", "Lưu ra file mới, chọn PNG / JPG / BMP.", "Ctrl+Shift+S"),
            new("Copy ảnh", "Copy ảnh (hoặc vùng chọn, nếu có) vào clipboard.", "Ctrl+C"),
            new("Dán ảnh", "Dán ảnh từ clipboard.", "Ctrl+V"),
            new("Ảnh mới", "Tạo ảnh trống thành tab mới.", "Ctrl+N"),
            new("Mở ảnh", "Mở ảnh có sẵn thành tab mới.", "Ctrl+O"),
            new("Xoay ảnh", "Xoay phải / trái 90°.", "Ctrl+R", "Ctrl+Shift+R"),
            new("Đổi cỡ ảnh", "Co giãn ảnh theo % hoặc px.", "Ctrl+E"),
            new("Xoá", "Xoá hình đang chọn (hoặc xoá vùng chọn).", "Delete", "Backspace"),
            new("Bỏ chọn", "Bỏ chọn hình / vùng chọn.", "Esc"),
            new("Vùng chọn", "Cut vùng / Cắt ảnh theo vùng.", "Ctrl+X", "Enter"),
            new("Zoom", "Phóng to / thu nhỏ / về 100% / zoom quanh con trỏ.", "Ctrl++", "Ctrl+-", "Ctrl+0", "Ctrl+lăn chuột"),
            new("Khoá góc / hình đều", "Giữ khi vẽ / kéo: đường thẳng khoá 45°, chữ nhật → vuông, elip → tròn, vùng chọn → vuông, ảnh dán giữ tỉ lệ.", "Shift"),
            new("Đóng tab", "Đóng tab đang mở (ảnh chưa lưu thì hỏi trước).", "Ctrl+W", "Ctrl+F4"),
            new("Hướng dẫn", "Mở cửa sổ này.", "F1"),
        ]),

        new("Cài đặt", "\uE713", "Nút Cài đặt ở cửa sổ chính, tab Tệp của Editor hoặc menu khay. Bấm OK mới lưu; Mặc định đưa mọi tuỳ chọn về ban đầu.",
        [
            new("Chung", "Hẹn giờ trước khi chụp; tự copy ảnh sau khi chụp; chụp kèm con trỏ chuột (Toàn màn hình, Màn hình hiện tại, Cửa sổ hiện tại, Chụp lại lần trước - không áp cho vùng chọn / chụp cuộn); chạy ngầm ở khay hệ thống; khởi động cùng Windows."),
            new("Tự động lưu", "Tự lưu mỗi ảnh chụp thành PNG vào 1 thư mục."),
            new("Mẫu tên file", "Tên file khi tự lưu, chọn mẫu có sẵn hoặc gõ tuỳ ý (có dòng xem trước). Thẻ: {date} ngày 2026-10-07, {time} giờ 10-31-10, {app} chương trình bị chụp (EXCEL, msedge - mọi trang web cùng 1 trình duyệt ra cùng tên, phân biệt bằng {window}), {window} tiêu đề cửa sổ (≤ 50 ký tự), {mode} kiểu chụp, {size} rộng x cao, {n} số thứ tự 001, 002… trong thư mục. Dấu \\ = thư mục con, vd {date}\\{time}. Ký tự cấm thành _; trùng tên thêm (2). Gõ kèm .png ở cuối cũng được (tự bỏ) - vd {date}\\{date}_{time}_{app}.png → thư mục 2026-10-07, file 2026-10-07_10-31-10_EXCEL.png."),
            new("Phiên làm việc", "Bật/tắt nhớ tab khi tắt app; giới hạn số tab / dung lượng; xem dung lượng và mở thư mục tạm."),
            new("Chụp cuộn", "Số lần cuộn tối đa, độ dài ảnh tối đa, thời gian chờ sau mỗi lần cuộn (tăng lên cho trang tải chậm)."),
            new("Phím tắt", "Gán phím tắt toàn cục cho từng kiểu chụp: Shift/Ctrl/Alt + 1 phím (PrintScreen, A-Z, 0-9, F1-F12). Không cho 2 thao tác trùng phím."),
            new("Báo lỗi", "Log ở thư mục Logs cạnh file ScreenCapture.exe (giữ 14 ngày) - gửi kèm file log khi báo lỗi."),
        ]),
    ];
}
