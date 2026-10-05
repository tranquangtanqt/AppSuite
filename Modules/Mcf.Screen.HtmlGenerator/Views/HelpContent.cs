using SharedUI.Help;

namespace Mcf.Screen.HtmlGenerator.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút Hướng dẫn / F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi tính năng thì cập nhật
/// cả 2 chỗ. Nằm ở Views (không ở Models / Services) vì 2 thư mục đó được biên dịch kèm vào Mcf.Screen.Tests - project test
/// không reference SharedUI.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Gom toàn bộ tài liệu màn hình 画面説明書 (Excel) thành 1 trang HTML tra cứu: tìm màn hình theo mã / tên / nội dung, xem ngay trong trình duyệt.",
        [
            new("1. Chọn thư mục nguồn", "Nút \"Chon thu muc nguon...\": chọn thư mục gốc chứa các file *.xlsx 画面説明書 (đọc cả thư mục con). Thư mục được nhớ cho lần mở sau."),
            new("2. Bấm \"1. Doc Excel -> SQLite\"", "Đọc mọi file .xlsx, dựng 1 trang HTML cho từng màn hình (Data\\Database\\Html\\{mã màn hình}.html) và lưu danh mục vào Data\\Database\\01_画面説明書.db. File lỗi được ghi log và bỏ qua, không dừng cả lượt."),
            new("3. Bấm \"2. Xuat HTML\"", "Sinh trang mục lục Data\\Database\\Html\\01_画面説明書.html."),
            new("4. Bấm \"Mo file HTML\"", "Mở trang mục lục bằng trình duyệt mặc định. Gửi cho người khác: gửi cả thư mục Data\\Database\\Html (gồm các trang màn hình và thư mục ảnh Images)."),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Tra cứu trong trang HTML", "", "Cột trái tìm và chọn màn hình, bên phải hiện tài liệu của màn hình đó.",
        [
            new("Ô \"Ma man hinh / ten man hinh...\"", "Lọc theo mã hoặc tên màn hình (chứa chuỗi, không phân biệt hoa / thường)."),
            new("Ô \"Tim theo noi dung...\"", "Lọc theo toàn bộ chữ trong mọi sheet của màn hình - vd tìm màn hình nào dùng 1 SQLID, 1 tên field. 2 ô kết hợp với nhau (phải khớp cả 2)."),
            new("Xem 1 màn hình", "Bấm 1 màn hình trong danh sách: tài liệu hiện ngay ở khung bên phải, mục đang xem được tô đậm."),
        ]),

        new("Tài liệu màn hình hiển thị gì", "", "Mỗi sheet của 画面説明書 thành 1 phần trong trang của màn hình.",
        [
            new("概要 / 画面遷移", "Đọc theo nội dung (mục đích, thao tác, SQLID, sắp xếp, phân quyền dữ liệu; điều hướng màn hình) và dựng thành bảng có nhãn rõ ràng."),
            new("Sơ đồ 処理関連図 / サービス関連図", "Hộp + mũi tên vẽ bằng hình của Excel được vẽ lại thành ảnh - đọc được luồng xử lý, không cần cài Excel. Hình dạng riêng của từng loại hộp không giữ nguyên (đều vẽ thành hình chữ nhật)."),
            new("項目説明", "Rút gọn thành 4 cột: bắt buộc / 項目名 / 説明 / 型, nhóm theo từng nhóm (操作種別, 検索, 登録...) - gọn và dễ lướt hơn lưới ô gốc."),
            new("表紙 / 画面イメージ và sheet khác", "Hiện như lưới ô Excel (ô gộp, màu nền, chữ đậm, ảnh nhúng). Sheet có bố cục lạ cũng dùng cách này nên không mất nội dung, chỉ không giống 100% bản Excel."),
            new("変更来歴", "Lịch sử sửa đổi không được đưa vào trang."),
        ]),

        new("Tên file và cập nhật", "", "Quy ước tên file nguồn và khi tài liệu thay đổi.",
        [
            new("Quy ước tên file", "{Số văn bản}_{Mã màn hình}_{Revision}_画面説明書（{Tên màn hình}）.xlsx - mã, tên, số văn bản, revision lấy từ tên file."),
            new("Cập nhật", "Sửa / thêm file trong thư mục nguồn rồi bấm lại \"1. Doc Excel -> SQLite\" và \"2. Xuat HTML\"."),
            new("Mở trang mục lục bằng file:// vẫn chạy", "Danh mục được nhúng sẵn trong trang (không đọc file ngoài), nên mở thẳng file HTML trên ổ đĩa vẫn tìm kiếm được."),
        ]),
    ];
}
