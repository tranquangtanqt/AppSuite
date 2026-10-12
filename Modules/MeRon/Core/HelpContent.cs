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
    public static IReadOnlyList<string> Pages { get; } = ["ImportPage", "CustomersPage", "ProductsPage", "CategoriesPage", "AddressesPage"];

    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "MeRon bản máy tính: xem và quản lý dữ liệu bán hàng của app điện thoại me-ron (khách hàng, món ăn, đơn hàng...). Dữ liệu lấy từ file backup .tsv mà điện thoại xuất ra.", null,
        [
            new("1. Lấy file backup từ điện thoại", "Trên điện thoại: Cài đặt → Sao lưu → Xuất file sao lưu. App lưu 8 file .tsv vào Download/MeRon/<thời gian> và tải lên Google Drive. Chép cả thư mục đó (hoặc tải từ Google Drive) về máy tính."),
            new("2. Nhập vào máy tính", "Mở trang Nhập dữ liệu, bấm Chọn thư mục..., kiểm tra số dòng từng bảng rồi bấm Nhập (thay toàn bộ)."),
            new("3. Xem / sửa danh mục", "Các trang Khách hàng, Món ăn, Danh mục món ăn, Địa chỉ ở menu bên trái."),
            new("Điện thoại là nơi nhập liệu chính", "Mỗi lần nhập là THAY TOÀN BỘ dữ liệu trên máy tính bằng bản backup - những gì thêm / sửa trên máy tính sẽ mất ở lần nhập sau. Bản cũ luôn được lưu lại trong thư mục Backups trước khi nhập."),
            new("Mở lại hướng dẫn này", "Mục Hướng dẫn cuối menu trái, hoặc phím F1 (mở thẳng phần của trang đang xem).", "F1"),
        ]),

        new("Nhập dữ liệu", "", "Nạp bộ backup .tsv của điện thoại, thay toàn bộ dữ liệu trên máy này.", "ImportPage",
        [
            new("Chọn thư mục", "Chọn chính thư mục 1 lần backup (vd 20261011_231039 - chứa address_….tsv, users_….tsv, orders_….tsv...), hoặc thư mục cha chứa nhiều lần backup: app tự lấy thư mục con mới nhất có tên dạng yyyyMMdd_HHmmss (bỏ qua bản phụ như …_bk). Có thể dán đường dẫn vào ô rồi Enter.", "Enter"),
            new("Kiểm tra trước, chưa ghi gì", "Chọn xong app đọc + kiểm tra cả 8 file: thiếu file, sai dòng tiêu đề, số sai, trùng mã... đều báo rõ file nào, dòng nào - khi đó dữ liệu hiện tại không bị động tới. Bảng xem trước cho biết mỗi bảng sẽ nhập bao nhiêu dòng và đang có bao nhiêu."),
            new("Lưu ý (vẫn nhập được)", "Cột lạ trong file (điện thoại bản mới hơn) bị bỏ qua; đơn hàng trỏ tới khách hàng / món ăn không có trong file... được nhập nguyên văn và liệt kê để bạn biết."),
            new("Nhập (thay toàn bộ)", "Hỏi xác nhận rồi xoá dữ liệu cũ của 8 bảng và nạp dữ liệu mới - trong 1 lần ghi duy nhất: lỗi giữa chừng thì dữ liệu giữ nguyên như trước."),
            new("Lấy lại dữ liệu cũ", "Trước mỗi lần nhập, database hiện tại được chép sang thư mục Backups cạnh file database (me_ron_truoc-import_<thời gian>.db, giữ 10 bản gần nhất). Đường dẫn database hiện ở cuối trang."),
            new("Kiểm tra lại", "Đọc lại thư mục - dùng khi vừa chép thêm bản backup mới vào thư mục cha."),
            new("File rỗng", "Bảng không có dữ liệu thì điện thoại xuất file rỗng (vd chưa có phiếu nhập nào) - nhập bình thường thành bảng trống."),
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
