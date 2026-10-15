namespace MeRon.Core;

/// <summary>1 mục trong cửa sổ Hướng dẫn. <paramref name="Keys"/>: phím / thao tác hiện thành các "phím" nhỏ.</summary>
public sealed record HelpItem(string Name, string Description, params string[] Keys);

/// <param name="PageTag">Tên trang (class trong MeRon.Views) mà mục này hướng dẫn - F1 trên trang đó mở thẳng mục này;
/// null = mục chung.</param>
public sealed record HelpSection(string Title, string Glyph, string Summary, string? PageTag, IReadOnlyList<HelpItem> Items);

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (mục "Hướng dẫn" cuối menu / F1) cho người dùng cuối. Mỗi trang có đúng 1 mục (unit test
/// kiểm tra). Thêm / đổi tính năng thì cập nhật cả README.
/// </summary>
public static class HelpContent
{
    /// <summary>Các trang có trên menu - unit test đối chiếu với <see cref="Sections"/>.</summary>
    public static IReadOnlyList<string> Pages { get; } =
        ["OrdersPage", "OrderFormPage", "OrderDetailPage", "PurchasesPage", "PurchaseFormPage",
         "ReportOrderPage", "ReportProductPage", "ReportSummaryPage", "ReportCustomerPage", "LapsedCustomersPage", "ImportPage", "ExportPage", "CustomersPage", "ProductsPage", "CategoriesPage", "AddressesPage"];

    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "MeRon bản máy tính: xem và quản lý dữ liệu bán hàng của app điện thoại me-ron (khách hàng, món ăn, đơn hàng...). Dữ liệu lấy từ file backup .tsv mà điện thoại xuất ra.", null,
        [
            new("1. Lấy file backup từ điện thoại", "Trên điện thoại: Cài đặt → Sao lưu → Xuất file sao lưu. App lưu 8 file .tsv vào Download/MeRon/<thời gian> và tải lên Google Drive. Chép cả thư mục đó về máy tính, hoặc dùng thẳng link folder Google Drive."),
            new("2. Nhập vào máy tính", "Mở trang Nhập dữ liệu, bấm Chọn thư mục... (hoặc dán link folder Google Drive rồi Enter), kiểm tra số dòng từng bảng rồi bấm Nhập (thay toàn bộ)."),
            new("3. Đặt hàng", "Trang Đặt hàng: xem / lọc đơn, thêm / sửa đơn, Chi tiết gộp theo khách + Thanh toán toàn bộ."),
            new("4. Mua hàng, Thống kê", "Mua hàng: phiếu nhập nguyên liệu theo ngày. Thống kê: Theo đơn hàng, Theo món ăn, Tổng hợp (bán + mua), Khách hàng tiềm năng."),
            new("5. Xem / sửa danh mục", "Các trang Khách hàng, Món ăn, Danh mục món ăn, Địa chỉ ở menu bên trái."),
            new("Ô ngày", "Gõ thẳng ngày kiểu 14/10/2026 rồi Enter (hoặc rời ô). Gõ tắt: 14/10 = năm nay, 14 = ngày 14 tháng này, 141026 hoặc 14102026; dấu / - . đều được. Gõ sai thì ô trả lại ngày cũ. Mũi tên lên / xuống = sau / trước 1 ngày. Nút lịch (hoặc Alt+↓, F4): lịch tiếng Việt, tuần bắt đầu thứ Hai, nút Hôm nay; Từ ngày / Đến ngày có thêm Xoá ngày (= không giới hạn). Ngày giao, ngày thanh toán, ngày nhập là bắt buộc: xoá trắng thì giữ ngày cũ.", "Enter", "↑", "↓", "Alt+↓"),
            new("Khoảng ngày", "Mọi trang có Từ ngày – Đến ngày (tính cả 2 ngày) và nút lịch nhỏ để chọn nhanh: Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày. Lần đầu mở trang thì là hôm nay (như điện thoại); sau đó chuyển sang trang khác rồi quay lại (kể cả bằng menu) vẫn giữ nguyên khoảng ngày đã chọn - đến khi tắt app."),
            new("Điện thoại là nơi nhập liệu chính", "Mỗi lần nhập là THAY TOÀN BỘ dữ liệu trên máy tính bằng bản backup - những gì thêm / sửa trên máy tính sẽ mất ở lần nhập sau. Bản cũ luôn được lưu lại trong thư mục Backups trước khi nhập. Muốn đưa phần sửa trên máy tính sang điện thoại: trang Xuất / Xoá dữ liệu → Xuất TSV, rồi nhập trên điện thoại."),
            new("Mở lại hướng dẫn này", "Mục Hướng dẫn cuối menu trái, hoặc phím F1 (mở thẳng phần của trang đang xem).", "F1"),
        ]),

        new("Phím tắt", "", "Thao tác nhanh bằng bàn phím (không cần chuột).", null,
        [
            new("Hướng dẫn", "Mở hướng dẫn của trang đang xem.", "F1"),
            new("Tìm / lọc", "Con trỏ vào ô tìm (Khách hàng, Món ăn, Danh mục, Địa chỉ) hoặc ô Lọc theo khách (Đặt hàng, Theo đơn hàng) để gõ ngay.", "Ctrl+F"),
            new("Thêm", "Ở trang danh sách: thêm đơn / phiếu nhập / khách hàng / món ăn / danh mục / địa chỉ.", "Ctrl+N"),
            new("Tab trạng thái", "Đặt hàng, Theo đơn hàng: Tất cả / Đã lên đơn / Đã thanh toán / Huỷ.", "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4"),
            new("Khoảng ngày nhanh", "Mở menu chọn nhanh khoảng ngày (Hôm nay, Hôm qua, 7 ngày qua...) rồi chọn bằng mũi tên + Enter. Đặt hàng, Mua hàng, các trang Thống kê.", "Ctrl+D"),
            new("Làm mới", "Trang Đặt hàng: đọc lại danh sách.", "F5"),
            new("Lưu", "Form đơn / phiếu nhập.", "Ctrl+S"),
            new("Lưu và thêm đơn mới", "Form đơn: lưu xong mở luôn form đơn mới cùng ngày giao - nhập liên tiếp nhiều đơn.", "Ctrl+Shift+N"),
            new("Thêm món / thêm hàng", "Form đơn: thêm dòng món, con trỏ vào ô món. Form phiếu nhập: thêm khoản, con trỏ vào ô Tên hàng.", "Ctrl+M"),
            new("Tăng / giảm số lượng", "Trong ô Số lượng: mũi tên lên = +1, xuống = −1.", "↑", "↓"),
            new("Sao chép đơn", "Form sửa đơn: như nút Sao chép đơn.", "Ctrl+Shift+C"),
            new("Ô ngày", "Gõ ngày rồi Enter; mũi tên lên / xuống = ±1 ngày; Alt+↓ hoặc F4 mở lịch.", "↑", "↓", "Alt+↓", "F4"),
            new("Quay lại", "Form đơn / phiếu nhập: quay lại, không lưu. Đang mở danh sách gợi ý / lịch / menu thì Esc đóng cái đó trước.", "Esc"),
        ]),

        new("Đặt hàng", "", "Danh sách đơn đặt hàng: lọc, sắp xếp, thêm / sửa đơn.", "OrdersPage",
        [
            new("Lọc theo trạng thái", "Tất cả / Đã lên đơn / Đã thanh toán / Huỷ. Lần đầu mở trang thì mặc định là Đã lên đơn, giao hôm nay. Phím Ctrl+1…4 = 4 tab.", "Ctrl+1…4"),
            new("Lọc theo khách", "Gõ tên hoặc số điện thoại vào ô Lọc theo khách hàng (không phân biệt dấu) rồi chọn trong gợi ý (Enter = gợi ý đầu). Nút X bỏ lọc. Ctrl+F đưa con trỏ vào ô.", "Ctrl+F"),
            new("Khoảng ngày giao", "Từ ngày – Đến ngày, tính theo ngày giao hàng (cả 2 ngày). Nút Khoảng ngày chọn nhanh: Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày (bỏ lọc ngày). (Ctrl+D). Đổi bộ lọc là tìm lại ngay.", "Ctrl+D"),
            new("Sắp xếp", "Tăng / Giảm dần theo thành tiền hoặc theo ngày giao đơn. Mặc định: ngày giao mới nhất trước. Nút Sắp xếp in đậm khi đang dùng cách khác mặc định."),
            new("Thẻ đơn", "Tên khách, trạng thái, ngày giao, các món (đơn giá, số lượng, thành tiền), ghi chú, Tạm tính / Giảm giá (khi có giảm giá), Tổng. Vạch màu bên trái + nhãn trạng thái: cam = Đã lên đơn (nền trắng), xanh lá = Đã thanh toán (nền xanh nhạt), xám = Huỷ (nền xám, chữ mờ). Bấm vào thẻ để sửa.", "Click"),
            new("Thêm đơn", "Nút Thêm (Ctrl+N). Đang lọc 1 khách thì đơn mới điền sẵn khách đó; Từ ngày = Đến ngày thì điền sẵn ngày giao đó.", "Ctrl+N"),
            new("Giữ bộ lọc", "Trạng thái, khách, khoảng ngày, sắp xếp được giữ nguyên khi thêm / sửa đơn, xem Chi tiết, hoặc chuyển sang trang khác (vd Mua hàng) rồi quay lại - đến khi tắt app. Các trang báo cáo, Mua hàng và ô tìm ở trang danh mục cũng giữ như vậy."),
            new("Làm mới", "Nút ⟳ hoặc F5 đọc lại danh sách.", "F5"),
        ]),

        new("Thêm / sửa đơn", "", "Form đơn hàng: khách, các món, giảm giá, ngày giao, thanh toán, ghi chú.", "OrderFormPage",
        [
            new("Khách hàng", "Gõ tên / số điện thoại rồi chọn. Bắt buộc."),
            new("Thêm món", "Nút Thêm món hoặc Ctrl+M: thêm 1 dòng trống, con trỏ vào ngay ô Món và danh sách hiện sẵn tất cả các món (tên · giá). Số lượng bằng − / +, mũi tên lên / xuống trong ô, hoặc gõ thẳng số (nhỏ hơn 1 tự về 1). X = xoá dòng.", "Ctrl+M", "↑", "↓"),
            new("Gõ để chọn món", "Gõ vài chữ của tên món: không cần dấu, các từ không cần liền nhau (vd \"bao xiu\" → Bánh bao xá xíu/trứng sữa); món có tên bắt đầu bằng chữ vừa gõ lên đầu. Enter = chọn món đầu danh sách, hoặc bấm chuột vào món. Gõ đủ tên món (không dấu cũng được) rồi rời ô thì tự nhận món đó.", "Enter"),
            new("Bắt buộc chọn trong danh sách", "Gõ món không có trong danh sách: dưới dòng hiện lỗi đỏ \"Không có món … trong danh sách\", chữ đã gõ giữ nguyên; bấm Lưu thì báo lỗi và đưa con trỏ về đúng ô đó. Dòng để trống mà Lưu: báo \"Chưa chọn món ăn.\". Món ngoài danh sách thì dùng Thêm món tự do."),
            new("Thêm món tự do", "Món không có trong danh sách Món ăn: tự nhập Tên món và Đơn giá. Không gắn với món nào (không bị ảnh hưởng khi sửa Món ăn). Để trống tên hoặc đơn giá ≤ 0 thì không lưu được và con trỏ nhảy về đúng ô thiếu."),
            new("Dòng bị khoá", "Khi sửa đơn: món đã đổi giá so với lúc đặt thì dòng đó mờ đi, có biểu tượng ổ khoá, không đổi món / số lượng / xoá được - giữ nguyên đơn giá cũ. Muốn đổi phải xoá đơn và tạo lại. Món tự do không bao giờ bị khoá."),
            new("Tiền", "Tổng = Tạm tính − Giảm giá (không âm), tự tính khi đổi món / số lượng / giảm giá. Tổng số lượng món (cả món tự do) hiện ngay trên Tạm tính."),
            new("Ngày giao / thanh toán", "Chọn Ngày giao hàng. Bật Đã thanh toán trước nếu khách trả tiền ngay - hiện thêm Ngày thanh toán (mặc định = ngày giao); lưu thành Đã thanh toán, tắt = Đã lên đơn."),
            new("Lưu", "Nút Lưu hoặc Ctrl+S. Khách đã có đơn khác cùng ngày giao (không tính đơn huỷ) thì hỏi Cảnh báo trùng đơn: Huỷ hoặc Vẫn lưu. Ctrl+Shift+N = lưu xong mở luôn đơn mới cùng ngày giao (nhập liên tiếp). Esc = quay lại không lưu.", "Ctrl+S", "Ctrl+Shift+N", "Esc"),
            new("Huỷ đơn / Xoá đơn", "Huỷ đơn: chuyển sang Huỷ, giữ lại để tra cứu, không tính vào báo cáo. Xoá đơn: xoá hẳn đơn và các dòng món, không hoàn tác. Đơn đã huỷ sửa và Lưu vẫn giữ Huỷ."),
            new("Sao chép đơn", "Khách quen đặt lặp lại: chuột phải 1 đơn (trang Đặt hàng, Chi tiết, Theo món ăn; hoặc Shift+F10) → Sao chép đơn, hoặc nút Sao chép đơn trong form sửa (Ctrl+Shift+C). Mở form Thêm đặt hàng điền sẵn khách, các món + số lượng, món tự do, giảm giá, ghi chú; giữ nguyên ngày giờ giao của đơn gốc (đơn gốc không có ngày giao → hôm nay), Đã lên đơn, chưa thanh toán; món tính giá hiện tại - giá đổi so với đơn gốc thì báo ở đầu form; món không còn trong danh sách chép thành món tự do. Chưa lưu gì cho đến khi bấm Lưu.", "Chuột phải", "Ctrl+Shift+C"),
        ]),

        new("Chi tiết đơn", "", "Toàn bộ đơn khớp bộ lọc, gộp theo từng khách.", "OrderDetailPage",
        [
            new("Mở", "Nút Chi tiết ở trang Đặt hàng - dùng đúng bộ lọc đang chọn (trạng thái, khách, khoảng ngày). Mỗi khách 1 khung, cuối khung là Tổng đơn của khách."),
            new("Sửa đơn", "Bấm vào 1 đơn để mở form sửa; Lưu / Quay lại thì về lại Chi tiết (đã cập nhật).", "Click"),
            new("Thanh toán toàn bộ", "Hiện khi còn đơn Đã lên đơn. Hỏi xác nhận rồi chuyển mọi đơn Đã lên đơn trong danh sách sang Đã thanh toán. Trong danh sách có đơn đã thanh toán từ trước thì từ chối cả lần (tránh thanh toán trùng) - lọc trạng thái Đã lên đơn trước khi mở Chi tiết. Đơn Huỷ không bị đổi."),
            new("Chụp ảnh", "Chụp toàn bộ danh sách (cả phần phải cuộn) → chép vào clipboard để dán (Ctrl+V) vào Zalo / Messenger, và lưu file PNG trong Pictures\\MeRon."),
        ]),

        new("Mua hàng", "", "Phiếu nhập / mua nguyên liệu theo ngày.", "PurchasesPage",
        [
            new("Danh sách", "Phiếu nhập trong khoảng ngày (mặc định hôm nay), mới nhất trước: thẻ có vạch xanh dương bên trái, ngày, số khoản, các khoản (tên, giá), Tổng. Nhãn xanh dương cạnh tiêu đề: số phiếu và tổng tiền. Bấm 1 phiếu để sửa.", "Click"),
            new("Thêm phiếu", "Nút Thêm (Ctrl+N). Đang lọc đúng 1 ngày thì phiếu mới lấy ngày đó, ngược lại là hôm nay.", "Ctrl+N"),
        ]),

        new("Thêm / sửa phiếu nhập", "", "Ngày nhập + các khoản hàng.", "PurchaseFormPage",
        [
            new("Các bước", "Chọn Ngày nhập. Mỗi khoản nhập Tên hàng và Giá tiền; Thêm hàng (Ctrl+M) để thêm dòng, X để xoá dòng. Tổng tự cộng. Esc = quay lại không lưu.", "Ctrl+M", "Esc"),
            new("Lưu", "Nút Lưu hoặc Ctrl+S. Phải có ít nhất 1 khoản, mỗi khoản có tên và giá lớn hơn 0 - thiếu thì báo lỗi cạnh nút Lưu và đưa con trỏ về đúng ô.", "Ctrl+S"),
            new("Xoá phiếu", "Trong phiếu đang sửa: xoá cả phiếu và mọi khoản bên trong (có hỏi xác nhận)."),
        ]),

        new("Thống kê theo đơn hàng", "", "Tổng đơn / tiền, theo trạng thái, theo món, theo khách.", "ReportOrderPage",
        [
            new("Bộ lọc", "Trạng thái (mặc định Đã lên đơn), khách hàng (tuỳ chọn), khoảng ngày giao."),
            new("Tổng cộng", "4 ô: Tổng số đơn, Tổng thành tiền, TB mỗi đơn (tổng tiền ÷ số đơn), Tổng số món - không tính đơn Huỷ. Bên dưới: từng trạng thái có số đơn, tiền và thanh % số đơn trong kỳ (Huỷ vẫn hiện riêng để biết)."),
            new("Tổng cộng theo món", "Số lượng + thành tiền từng món (không tính đơn Huỷ), nhiều nhất trước, thanh màu dưới tên so với món bán nhiều nhất; món tự do gộp theo tên. Tổng số món ở trên bảng."),
            new("Chi tiết đơn hàng", "Mỗi khách 1 dòng: số đơn, tổng tiền. Sắp xếp: tiền giảm / tăng dần, ngày giao tăng dần (theo đơn sớm nhất của khách) / giảm dần (theo đơn mới nhất). Bấm 1 khách → màn Chi tiết chỉ đơn của khách đó trong đúng bộ lọc này (không đổi bộ lọc trang Đặt hàng).", "Click"),
        ]),

        new("Thống kê theo món ăn", "", "Món nào bán bao nhiêu.", "ReportProductPage",
        [
            new("Bộ lọc", "Món ăn (mặc định tất cả) + khoảng ngày giao. Không tính đơn Huỷ."),
            new("Kết quả", "3 ô: Tổng số món, Tổng thành tiền, Số món khác nhau; bảng từng món (số lượng, thành tiền, thanh so với món bán nhiều nhất) nhiều nhất trước - món tự do mỗi tên 1 dòng (chọn 1 món cụ thể thì không lọc được món tự do)."),
            new("Đơn hàng liên quan", "Các đơn trong khoảng ngày; chọn 1 món thì chỉ hiện đơn có món đó và chỉ dòng của món đó. Bấm đơn để mở form sửa.", "Click"),
        ]),

        new("Thống kê tổng hợp", "", "Bán (đơn hàng) và mua (phiếu nhập) trong cùng khoảng ngày.", "ReportSummaryPage",
        [
            new("Đơn hàng", "Ô cam Bán: tổng thành tiền + số đơn (không tính đơn Huỷ); bảng Bán theo món (có thanh so với món bán nhiều nhất)."),
            new("Mua hàng", "Ô xanh dương Mua: tổng tiền + số phiếu nhập; bảng Mua theo khoản hàng (gộp theo tên, không phân biệt hoa thường): số lần mua, tổng tiền, thanh so với khoản tốn nhiều nhất."),
            new("Chênh lệch", "Ô thứ 3: tiền bán − tiền mua của khoảng ngày - xanh lá khi lãi, đỏ khi lỗ (số âm = mua nhiều hơn bán)."),
        ]),

        new("Khách hàng tiềm năng", "", "Xếp hạng khách theo tổng tiền đã đặt.", "ReportCustomerPage",
        [
            new("Xếp hạng", "Khách đặt nhiều tiền nhất trong khoảng ngày lên đầu (không tính đơn Huỷ), tối đa 100 khách; mỗi dòng: hạng, tên, số đơn, tổng tiền."),
            new("Xem đơn", "Bấm 1 khách → màn Chi tiết các đơn của khách trong khoảng ngày (mọi trạng thái).", "Click"),
        ]),

        new("Khách lâu không đặt", "", "Khách trước đây hay đặt mà dạo này không đặt nữa - để nhắn hỏi thăm, mời đặt lại.", "LapsedCustomersPage",
        [
            new("Bộ lọc", "Không đặt từ … ngày (mặc định 30; nút đồng hồ chọn nhanh 14 / 30 / 60 / 90) và Đã đặt ít nhất … đơn (mặc định 2, loại khách chỉ thử 1 lần). Tính theo ngày giao của đơn gần nhất, không tính đơn Huỷ; khách có đơn giao trong tương lai coi như vẫn đang đặt. Bộ lọc giữ nguyên khi chuyển trang, đến khi tắt app."),
            new("Mỗi khách", "Tên, SĐT, địa chỉ; ngày đặt lần cuối + cách đây bao nhiêu ngày; thói quen (trung bình bao nhiêu ngày đặt 1 lần, cần từ 3 lần đặt ở 3 ngày khác nhau), số đơn + tổng tiền đã mua, 3 món hay đặt nhất. Nhãn cam \"Nghỉ gấp … lần thói quen\" khi đã nghỉ gấp 2 lần thói quen trở lên - khách tuần nào cũng đặt mà 3 tuần nay im thì đáng hỏi hơn khách tháng mới đặt 1 lần."),
            new("Sắp xếp", "Lâu không đặt nhất trước (mặc định) / Tổng tiền đã mua nhiều nhất trước / Quá thói quen nhiều nhất trước."),
            new("Tìm", "Gõ tên hoặc số điện thoại (không dấu cũng được).", "Ctrl+F"),
            new("Xem đơn", "Mọi đơn của khách (mọi ngày, mọi trạng thái) ở màn Chi tiết."),
            new("Đặt lại", "Mở form đơn mới giống đơn gần nhất của khách (món, số lượng, ghi chú...), giao hôm nay, giá hiện tại. Chưa lưu gì cho đến khi bấm Lưu."),
            new("Tin nhắn", "Chép tin nhắn hỏi thăm soạn sẵn (gọi theo tên khách, bỏ mã phòng ở đầu / cuối tên như \"908\", \"W3\", tên viết hoa hết thì đổi thành chữ hoa đầu từ; nhắc món hay đặt nhất) rồi dán vào Zalo bằng Ctrl+V. Nút điện thoại: chép số điện thoại.", "Ctrl+V"),
        ]),

        new("Nhập dữ liệu", "", "Nạp bộ backup .tsv của điện thoại, thay toàn bộ dữ liệu trên máy này.", "ImportPage",
        [
            new("Chọn thư mục", "Chọn chính thư mục 1 lần backup (vd 20261011_231039 - chứa address_….tsv, users_….tsv, orders_….tsv...), hoặc thư mục cha chứa nhiều lần backup: app tự lấy thư mục con mới nhất có tên dạng yyyyMMdd_HHmmss (bỏ qua bản phụ như …_bk). Có thể dán đường dẫn vào ô rồi Enter.", "Enter"),
            new("Lấy thẳng từ Google Drive", "Dán link folder Google Drive vào ô (vd https://drive.google.com/drive/folders/…) - dán vào là app tự đọc (hoặc Enter / Kiểm tra lại), không cần đăng nhập. Link là folder cha điện thoại tải backup lên thì app tự lấy folder con mới nhất dạng yyyyMMdd_HHmmss, tải 8 file .tsv về thư mục tạm rồi kiểm tra như thư mục trên máy; đang tải bấm Huỷ được. Folder phải chia sẻ \"Bất kỳ ai có đường liên kết\" (lưu ý: ai có link đều xem được dữ liệu khách hàng). App nhớ link cho lần sau.", "Enter"),
            new("Chọn bản khác bản mới nhất", "Thư mục cha / link folder Google Drive có nhiều bản: app tự kiểm tra bản mới nhất, đồng thời liệt kê TẤT CẢ các bản (mới nhất trước, của mọi tài khoản đã tải lên folder chung) ngay dưới ô thư mục - bấm 1 bản để kiểm tra + xem trước bản đó, nhãn \"Đang xem\" là bản đang hiện ở dưới.", "Click"),
            new("Chọn bản trên Google Drive (đăng nhập)", "Khung \"Hoặc lấy từ Google Drive\": Đăng nhập Google (dùng chung với trang Xuất - nhớ tick ô quyền Google Drive) → app liệt kê các bản backup yyyyMMdd_HHmmss mới nhất trước: thời điểm, nơi lưu (folder chung của điện thoại, hay folder \"MeRon backup\" của máy tính), đủ 8 bảng hay thiếu. Bấm 1 bản → tải về thư mục tạm rồi kiểm tra + xem trước như thư mục trên máy. Folder KHÔNG cần chia sẻ công khai. App chỉ thấy bản do app điện thoại / MeRon tải lên (folder tự tạo hoặc file tự kéo lên trang web Drive thì không thấy). Xem các bản trên Drive: đọc lại danh sách (vd điện thoại vừa tải lên bản mới).", "Click"),
            new("Kiểm tra trước, chưa ghi gì", "Chọn xong app đọc + kiểm tra cả 8 file: thiếu file, sai dòng tiêu đề, số sai, trùng mã... đều báo rõ file nào, dòng nào - khi đó dữ liệu hiện tại không bị động tới. Bảng xem trước cho biết mỗi bảng sẽ nhập bao nhiêu dòng và đang có bao nhiêu."),
            new("Lưu ý (vẫn nhập được)", "Cột lạ trong file (điện thoại bản mới hơn) bị bỏ qua; đơn hàng trỏ tới khách hàng / món ăn không có trong file... được nhập nguyên văn và liệt kê để bạn biết."),
            new("Nhập (thay toàn bộ)", "Hỏi xác nhận rồi xoá dữ liệu cũ của 8 bảng và nạp dữ liệu mới - trong 1 lần ghi duy nhất: lỗi giữa chừng thì dữ liệu giữ nguyên như trước."),
            new("Lấy lại dữ liệu cũ", "Trước mỗi lần nhập, database hiện tại được chép sang thư mục Backups cạnh file database (me_ron_truoc-import_<thời gian>.db, giữ 10 bản gần nhất). Đường dẫn database hiện ở cuối trang."),
            new("Kiểm tra lại", "Đọc lại thư mục / folder Google Drive - dùng khi vừa có bản backup mới."),
            new("File rỗng", "Bảng không có dữ liệu thì điện thoại xuất file rỗng (vd chưa có phiếu nhập nào) - nhập bình thường thành bảng trống."),
        ]),

        new("Xuất / Xoá dữ liệu", "", "Xuất bộ backup TSV đúng định dạng điện thoại; xoá toàn bộ dữ liệu ở máy.", "ExportPage",
        [
            new("Xuất TSV", "Chọn thư mục lưu (mặc định Documents\\MeRon, nhớ lần trước) rồi bấm Xuất TSV: tạo thư mục yyyyMMdd_HHmmss chứa 8 file .tsv giống hệt file điện thoại xuất. Mở thư mục vừa xuất để chép đi."),
            new("Đưa sang điện thoại", "Chép thư mục vừa xuất vào điện thoại; trên điện thoại vào Cài đặt → Sao lưu → Nhập dữ liệu từ tệp sao lưu và chọn file của từng bảng. Lưu ý: điện thoại xoá dữ liệu của bảng đó rồi mới nhập file."),
            new("Xuất lên Google Drive", "Khung Tải bộ backup lên Google Drive: bấm Đăng nhập Google... → trình duyệt mở trang Google, chọn tài khoản, TICK ô quyền Google Drive (\"…tệp cụ thể trên Google Drive mà bạn dùng với ứng dụng này\") rồi Tiếp tục (Continue) - quên tick thì app báo và bảo đăng nhập lại → quay lại MeRon thấy \"Đã đăng nhập: email\". Lần sau không phải đăng nhập lại (cất trong Thông tin xác thực Windows); Đăng xuất thì xoá và thu hồi quyền. Bấm Xuất lên Google Drive: xuất bộ TSV vào thư mục bên trên rồi tải lên Drive thành thư mục yyyyMMdd_HHmmss (như điện thoại); xong có nút Mở trên Google Drive. Ô Folder: để trống = folder riêng \"MeRon backup\" ở My Drive (app tự tạo); muốn chung folder với điện thoại thì dán link folder đó - không ghi được thì app báo và bảo để trống. App chỉ có quyền với file do chính nó / app điện thoại tạo (quyền drive.file)."),
            new("Đưa sang máy tính khác", "Trên máy kia: trang Nhập dữ liệu, chọn thư mục vừa xuất (hoặc thư mục cha) - như nhập backup của điện thoại."),
            new("Khác file điện thoại 2 chỗ nhỏ", "Bảng trống ghi dòng tiêu đề (điện thoại ghi file rỗng, và chính điện thoại không nhập được file rỗng); xuống dòng trong ghi chú đổi thành dấu cách (như điện thoại làm với tab / xuống dòng)."),
            new("Xoá toàn bộ dữ liệu", "Xoá sạch dữ liệu bán hàng trên máy này (đơn, khách, món, danh mục, địa chỉ, phiếu nhập). Hỏi xác nhận 2 lần. Bản trước khi xoá lưu ở thư mục Backups (me_ron_truoc-xoa_<thời gian>.db); nút Mở thư mục Backups."),
            new("Lấy lại dữ liệu cũ", "Thư mục Backups có bản trước mỗi lần Nhập (me_ron_truoc-import_…) và mỗi lần Xoá (me_ron_truoc-xoa_…), giữ 10 bản gần nhất mỗi loại. Lấy lại: đóng MeRon, chép bản cần lấy đè lên file me_ron.db (đường dẫn ở cuối trang Nhập dữ liệu)."),
        ]),

        new("Khách hàng", "", "Danh sách khách hàng dùng cho đơn hàng và báo cáo.", "CustomersPage",
        [
            new("Tìm", "Gõ vào ô tìm - lọc ngay theo tên hoặc số điện thoại, không phân biệt hoa thường và dấu (\"chi an\" khớp \"Chị An\")."),
            new("Danh sách", "Ô tròn màu cạnh tên = chữ cái đầu của tên (bỏ qua số phòng / mã toà: \"908 Chị Huyền\" → H); địa chỉ hiện thành nhãn xám."),
            new("Thêm / sửa", "Bấm Thêm, hoặc bấm vào 1 dòng để sửa: Tên, Số điện thoại, Địa chỉ (gõ tự do hoặc chọn gợi ý từ danh mục Địa chỉ), Ghi chú.", "Click"),
            new("Trùng tên", "Tên khách hàng không được trùng với khách khác (không phân biệt hoa thường, như điện thoại). Sửa mà giữ nguyên tên cũ thì vẫn lưu được."),
            new("Xoá", "Nút Xoá trong hộp thoại sửa. Khách hàng còn đơn hàng thì không xoá được (để báo cáo không bị sai)."),
        ]),

        new("Món ăn", "", "Danh sách món ăn và giá bán.", "ProductsPage",
        [
            new("Tìm", "Gõ tên món vào ô tìm - lọc ngay, không phân biệt hoa thường và dấu."),
            new("Thêm / sửa", "Bấm Thêm, hoặc bấm vào 1 dòng: Danh mục, Tên món ăn, Giá bán, Mô tả.", "Click"),
            new("Đổi giá", "Đơn hàng cũ giữ nguyên đơn giá lúc đặt - đổi giá món chỉ áp dụng cho đơn mới."),
            new("Không có Xoá / ảnh", "Giống app điện thoại hiện tại: món ăn không xoá được, và bản máy tính không quản lý ảnh món (ảnh đã có trên điện thoại vẫn giữ nguyên khi sửa)."),
        ]),

        new("Danh mục món ăn", "", "Nhóm món ăn (Bánh bao, Đồ uống...).", "CategoriesPage",
        [
            new("Thêm / sửa", "Bấm Thêm, bấm vào 1 dòng hoặc nút bút chì: Tên, Mô tả."),
            new("Xoá", "Nút thùng rác trên dòng, có hỏi xác nhận. Danh mục còn món ăn thì không xoá được - chuyển các món sang danh mục khác trước."),
        ]),

        new("Địa chỉ", "", "Toà nhà / khu vực giao hàng.", "AddressesPage",
        [
            new("Thêm / sửa", "Bấm Thêm: Mã (vd W1) và Tên (vd Tòa nhà W1). Mã không trùng nhau và không đổi được sau khi tạo - muốn đổi mã thì xoá rồi thêm lại."),
            new("Xoá", "Nút Xoá trong hộp thoại sửa. Khách hàng đã ghi địa chỉ này vẫn giữ nguyên chữ đã ghi."),
        ]),
    ];

    /// <summary>Mục của trang đang xem (không có thì mục đầu).</summary>
    public static HelpSection ForPage(string? pageTag) => Sections.FirstOrDefault(s => s.PageTag == pageTag) ?? Sections[0];
}
