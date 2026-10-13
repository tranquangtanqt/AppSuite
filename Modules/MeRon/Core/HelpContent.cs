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
         "ReportOrderPage", "ReportProductPage", "ReportSummaryPage", "ReportCustomerPage", "ImportPage", "ExportPage", "CustomersPage", "ProductsPage", "CategoriesPage", "AddressesPage"];

    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "MeRon bản máy tính: xem và quản lý dữ liệu bán hàng của app điện thoại me-ron (khách hàng, món ăn, đơn hàng...). Dữ liệu lấy từ file backup .tsv mà điện thoại xuất ra.", null,
        [
            new("1. Lấy file backup từ điện thoại", "Trên điện thoại: Cài đặt → Sao lưu → Xuất file sao lưu. App lưu 8 file .tsv vào Download/MeRon/<thời gian> và tải lên Google Drive. Chép cả thư mục đó về máy tính, hoặc dùng thẳng link folder Google Drive."),
            new("2. Nhập vào máy tính", "Mở trang Nhập dữ liệu, bấm Chọn thư mục... (hoặc dán link folder Google Drive rồi Enter), kiểm tra số dòng từng bảng rồi bấm Nhập (thay toàn bộ)."),
            new("3. Đặt hàng", "Trang Đặt hàng: xem / lọc đơn, thêm / sửa đơn, Chi tiết gộp theo khách + Thanh toán toàn bộ."),
            new("4. Mua hàng, Thống kê", "Mua hàng: phiếu nhập nguyên liệu theo ngày. Thống kê: Theo đơn hàng, Theo món ăn, Tổng hợp (bán + mua), Khách hàng tiềm năng."),
            new("5. Xem / sửa danh mục", "Các trang Khách hàng, Món ăn, Danh mục món ăn, Địa chỉ ở menu bên trái."),
            new("Khoảng ngày", "Mọi trang có Từ ngày – Đến ngày (tính cả 2 ngày) và nút lịch nhỏ để chọn nhanh: Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày. Mở trang từ menu thì về hôm nay (như điện thoại); quay lại từ trang con thì giữ nguyên."),
            new("Điện thoại là nơi nhập liệu chính", "Mỗi lần nhập là THAY TOÀN BỘ dữ liệu trên máy tính bằng bản backup - những gì thêm / sửa trên máy tính sẽ mất ở lần nhập sau. Bản cũ luôn được lưu lại trong thư mục Backups trước khi nhập. Muốn đưa phần sửa trên máy tính sang điện thoại: trang Xuất / Xoá dữ liệu → Xuất TSV, rồi nhập trên điện thoại."),
            new("Mở lại hướng dẫn này", "Mục Hướng dẫn cuối menu trái, hoặc phím F1 (mở thẳng phần của trang đang xem).", "F1"),
        ]),

        new("Đặt hàng", "", "Danh sách đơn đặt hàng: lọc, sắp xếp, thêm / sửa đơn.", "OrdersPage",
        [
            new("Lọc theo trạng thái", "Tất cả / Đã lên đơn / Đã thanh toán / Huỷ. Mở trang từ menu thì mặc định là Đã lên đơn, giao hôm nay."),
            new("Lọc theo khách", "Gõ tên hoặc số điện thoại vào ô Lọc theo khách hàng (không phân biệt dấu) rồi chọn trong gợi ý (Enter = gợi ý đầu). Nút X bỏ lọc."),
            new("Khoảng ngày giao", "Từ ngày – Đến ngày, tính theo ngày giao hàng (cả 2 ngày). Nút Khoảng ngày chọn nhanh: Hôm nay, Hôm qua, 7 ngày qua, Tháng này, Tháng trước, Mọi ngày (bỏ lọc ngày). Đổi bộ lọc là tìm lại ngay."),
            new("Sắp xếp", "Tăng / Giảm dần theo thành tiền hoặc theo ngày giao đơn. Mặc định: ngày giao mới nhất trước. Nút Sắp xếp in đậm khi đang dùng cách khác mặc định."),
            new("Thẻ đơn", "Tên khách, trạng thái, ngày giao, các món (đơn giá, số lượng, thành tiền), ghi chú, Tạm tính / Giảm giá (khi có giảm giá), Tổng. Nền xanh = Đã thanh toán, đỏ = Huỷ. Bấm vào thẻ để sửa.", "Click"),
            new("Thêm đơn", "Nút Thêm (Ctrl+N). Đang lọc 1 khách thì đơn mới điền sẵn khách đó; Từ ngày = Đến ngày thì điền sẵn ngày giao đó.", "Ctrl+N"),
            new("Giữ bộ lọc", "Thêm / sửa đơn hoặc xem Chi tiết rồi quay lại: bộ lọc giữ nguyên. Bấm Đặt hàng ở menu: bộ lọc về mặc định."),
            new("Làm mới", "Nút ⟳ hoặc F5 đọc lại danh sách.", "F5"),
        ]),

        new("Thêm / sửa đơn", "", "Form đơn hàng: khách, các món, giảm giá, ngày giao, thanh toán, ghi chú.", "OrderFormPage",
        [
            new("Khách hàng", "Gõ tên / số điện thoại rồi chọn. Bắt buộc."),
            new("Thêm món", "Thêm 1 dòng trống, con trỏ vào ngay ô Món và danh sách hiện sẵn tất cả các món (tên · giá). Số lượng bằng − / + hoặc gõ thẳng số (nhỏ hơn 1 tự về 1). X = xoá dòng."),
            new("Gõ để chọn món", "Gõ vài chữ của tên món: không cần dấu, các từ không cần liền nhau (vd \"bao xiu\" → Bánh bao xá xíu/trứng sữa); món có tên bắt đầu bằng chữ vừa gõ lên đầu. Enter = chọn món đầu danh sách, hoặc bấm chuột vào món. Gõ đủ tên món (không dấu cũng được) rồi rời ô thì tự nhận món đó.", "Enter"),
            new("Bắt buộc chọn trong danh sách", "Gõ món không có trong danh sách: dưới dòng hiện lỗi đỏ \"Không có món … trong danh sách\", chữ đã gõ giữ nguyên; bấm Lưu thì báo lỗi và đưa con trỏ về đúng ô đó. Dòng để trống mà Lưu: báo \"Chưa chọn món ăn.\". Món ngoài danh sách thì dùng Thêm món tự do."),
            new("Thêm món tự do", "Món không có trong danh sách Món ăn: tự nhập Tên món và Đơn giá. Không gắn với món nào (không bị ảnh hưởng khi sửa Món ăn). Để trống tên hoặc đơn giá ≤ 0 thì không lưu được và con trỏ nhảy về đúng ô thiếu."),
            new("Dòng bị khoá", "Khi sửa đơn: món đã đổi giá so với lúc đặt thì dòng đó mờ đi, có biểu tượng ổ khoá, không đổi món / số lượng / xoá được - giữ nguyên đơn giá cũ. Muốn đổi phải xoá đơn và tạo lại. Món tự do không bao giờ bị khoá."),
            new("Tiền", "Tổng = Tạm tính − Giảm giá (không âm), tự tính khi đổi món / số lượng / giảm giá. Tổng số lượng món (cả món tự do) hiện ngay trên Tạm tính."),
            new("Ngày giao / thanh toán", "Chọn Ngày giao hàng. Bật Đã thanh toán trước nếu khách trả tiền ngay - hiện thêm Ngày thanh toán (mặc định = ngày giao); lưu thành Đã thanh toán, tắt = Đã lên đơn."),
            new("Lưu", "Nút Lưu hoặc Ctrl+S. Khách đã có đơn khác cùng ngày giao (không tính đơn huỷ) thì hỏi Cảnh báo trùng đơn: Huỷ hoặc Vẫn lưu.", "Ctrl+S"),
            new("Huỷ đơn / Xoá đơn", "Huỷ đơn: chuyển sang Huỷ, giữ lại để tra cứu, không tính vào báo cáo. Xoá đơn: xoá hẳn đơn và các dòng món, không hoàn tác. Đơn đã huỷ sửa và Lưu vẫn giữ Huỷ."),
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
            new("Danh sách", "Phiếu nhập trong khoảng ngày (mặc định hôm nay), mới nhất trước: ngày, các khoản (tên, giá), Tổng. Dòng trên cùng: số phiếu và tổng tiền. Bấm 1 phiếu để sửa.", "Click"),
            new("Thêm phiếu", "Nút Thêm (Ctrl+N). Đang lọc đúng 1 ngày thì phiếu mới lấy ngày đó, ngược lại là hôm nay.", "Ctrl+N"),
        ]),

        new("Thêm / sửa phiếu nhập", "", "Ngày nhập + các khoản hàng.", "PurchaseFormPage",
        [
            new("Các bước", "Chọn Ngày nhập. Mỗi khoản nhập Tên hàng và Giá tiền; Thêm hàng để thêm dòng, X để xoá dòng. Tổng tự cộng."),
            new("Lưu", "Nút Lưu hoặc Ctrl+S. Phải có ít nhất 1 khoản, mỗi khoản có tên và giá lớn hơn 0 - thiếu thì báo lỗi cạnh nút Lưu và đưa con trỏ về đúng ô.", "Ctrl+S"),
            new("Xoá phiếu", "Trong phiếu đang sửa: xoá cả phiếu và mọi khoản bên trong (có hỏi xác nhận)."),
        ]),

        new("Thống kê theo đơn hàng", "", "Tổng đơn / tiền, theo trạng thái, theo món, theo khách.", "ReportOrderPage",
        [
            new("Bộ lọc", "Trạng thái (mặc định Đã lên đơn), khách hàng (tuỳ chọn), khoảng ngày giao."),
            new("Tổng cộng", "Tổng số đơn và Tổng thành tiền - không tính đơn Huỷ. Bên dưới: số đơn + tiền của từng trạng thái (Huỷ vẫn hiện riêng để biết)."),
            new("Tổng cộng theo món", "Số lượng + thành tiền từng món (không tính đơn Huỷ), nhiều nhất trước; món tự do gộp theo tên. Tổng số món ở trên bảng."),
            new("Chi tiết đơn hàng", "Mỗi khách 1 dòng: số đơn, tổng tiền. Sắp xếp: tiền giảm / tăng dần, ngày giao tăng dần (theo đơn sớm nhất của khách) / giảm dần (theo đơn mới nhất). Bấm 1 khách → màn Chi tiết chỉ đơn của khách đó trong đúng bộ lọc này (không đổi bộ lọc trang Đặt hàng).", "Click"),
        ]),

        new("Thống kê theo món ăn", "", "Món nào bán bao nhiêu.", "ReportProductPage",
        [
            new("Bộ lọc", "Món ăn (mặc định tất cả) + khoảng ngày giao. Không tính đơn Huỷ."),
            new("Kết quả", "Tổng số món; bảng từng món (số lượng, thành tiền) nhiều nhất trước - món tự do mỗi tên 1 dòng (chọn 1 món cụ thể thì không lọc được món tự do)."),
            new("Đơn hàng liên quan", "Các đơn trong khoảng ngày; chọn 1 món thì chỉ hiện đơn có món đó và chỉ dòng của món đó. Bấm đơn để mở form sửa.", "Click"),
        ]),

        new("Thống kê tổng hợp", "", "Bán (đơn hàng) và mua (phiếu nhập) trong cùng khoảng ngày.", "ReportSummaryPage",
        [
            new("Đơn hàng", "Tổng số đơn, Tổng thành tiền (không tính đơn Huỷ) và bảng theo từng món."),
            new("Mua hàng", "Tổng số phiếu nhập, Tổng thành tiền và bảng theo từng khoản hàng (gộp theo tên, không phân biệt hoa thường): số lần mua, tổng tiền."),
            new("Chênh lệch", "Dòng trên cùng: tiền bán − tiền mua của khoảng ngày (âm = mua nhiều hơn bán)."),
        ]),

        new("Khách hàng tiềm năng", "", "Xếp hạng khách theo tổng tiền đã đặt.", "ReportCustomerPage",
        [
            new("Xếp hạng", "Khách đặt nhiều tiền nhất trong khoảng ngày lên đầu (không tính đơn Huỷ), tối đa 100 khách; mỗi dòng: hạng, tên, số đơn, tổng tiền."),
            new("Xem đơn", "Bấm 1 khách → màn Chi tiết các đơn của khách trong khoảng ngày (mọi trạng thái).", "Click"),
        ]),

        new("Nhập dữ liệu", "", "Nạp bộ backup .tsv của điện thoại, thay toàn bộ dữ liệu trên máy này.", "ImportPage",
        [
            new("Chọn thư mục", "Chọn chính thư mục 1 lần backup (vd 20261011_231039 - chứa address_….tsv, users_….tsv, orders_….tsv...), hoặc thư mục cha chứa nhiều lần backup: app tự lấy thư mục con mới nhất có tên dạng yyyyMMdd_HHmmss (bỏ qua bản phụ như …_bk). Có thể dán đường dẫn vào ô rồi Enter.", "Enter"),
            new("Lấy thẳng từ Google Drive", "Dán link folder Google Drive vào ô (vd https://drive.google.com/drive/folders/…) rồi Enter / Kiểm tra lại - không cần đăng nhập. Link là folder cha điện thoại tải backup lên thì app tự lấy folder con mới nhất dạng yyyyMMdd_HHmmss, tải 8 file .tsv về thư mục tạm rồi kiểm tra như thư mục trên máy; đang tải bấm Huỷ được. Folder phải chia sẻ \"Bất kỳ ai có đường liên kết\" (lưu ý: ai có link đều xem được dữ liệu khách hàng). App nhớ link cho lần sau.", "Enter"),
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
            new("Đưa sang máy tính khác", "Trên máy kia: trang Nhập dữ liệu, chọn thư mục vừa xuất (hoặc thư mục cha) - như nhập backup của điện thoại."),
            new("Khác file điện thoại 2 chỗ nhỏ", "Bảng trống ghi dòng tiêu đề (điện thoại ghi file rỗng, và chính điện thoại không nhập được file rỗng); xuống dòng trong ghi chú đổi thành dấu cách (như điện thoại làm với tab / xuống dòng)."),
            new("Xoá toàn bộ dữ liệu", "Xoá sạch dữ liệu bán hàng trên máy này (đơn, khách, món, danh mục, địa chỉ, phiếu nhập). Hỏi xác nhận 2 lần. Bản trước khi xoá lưu ở thư mục Backups (me_ron_truoc-xoa_<thời gian>.db); nút Mở thư mục Backups."),
            new("Lấy lại dữ liệu cũ", "Thư mục Backups có bản trước mỗi lần Nhập (me_ron_truoc-import_…) và mỗi lần Xoá (me_ron_truoc-xoa_…), giữ 10 bản gần nhất mỗi loại. Lấy lại: đóng MeRon, chép bản cần lấy đè lên file me_ron.db (đường dẫn ở cuối trang Nhập dữ liệu)."),
        ]),

        new("Khách hàng", "", "Danh sách khách hàng dùng cho đơn hàng và báo cáo.", "CustomersPage",
        [
            new("Tìm", "Gõ vào ô tìm - lọc ngay theo tên hoặc số điện thoại, không phân biệt hoa thường và dấu (\"chi an\" khớp \"Chị An\")."),
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
