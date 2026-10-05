using SharedUI.Help;

namespace Rdbms.HtmlGenerator.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút Hướng dẫn / F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi tính năng thì cập nhật
/// cả 2 chỗ.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Đọc cấu trúc bảng / cột / khoá từ PostgreSQL hoặc Oracle, xuất thành 1 trang HTML tra cứu mở trong trình duyệt.",
        [
            new("1. Thiết lập kết nối", "Nút \"Thiet lap thong tin database\": nhập thông tin ở tab PostgreSQL và / hoặc Oracle, bấm Luu (lưu cả 2 tab, tự điền lại lần mở sau)."),
            new("2. Chọn Nguon", "PostgreSQL hoặc Oracle. Nút \"1. Doc Database -> SQLite\" chỉ bật khi nguồn đang chọn đã có đủ thông tin (PostgreSQL: Host + Database; Oracle: Host + Service Name hoặc SID)."),
            new("3. Bấm \"1. Doc Database -> SQLite\"", "Kết nối, đọc schema, lưu vào Data\\Database\\{tên database}.db. Khung log bên dưới hiện tiến trình và lỗi kết nối (nếu có)."),
            new("4. Bấm \"2. Xuat HTML\" rồi \"Mo file HTML\"", "Sinh và mở Data\\Database\\{tên database}.html - 1 file tự chứa, không cần mạng, gửi cho người khác được."),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Thiết lập kết nối", "", "Hộp thoại có 2 tab, mỗi tab lưu riêng thông tin của 1 loại database.",
        [
            new("PostgreSQL", "Host, Port (mặc định 5432), Database, Username, Password, Schema (để trống = mọi schema)."),
            new("Oracle", "Host, Port (mặc định 1521), chọn kết nối bằng Service Name hoặc SID rồi nhập ô tương ứng, Username, Password, Schema (để trống = schema trùng tên Username, theo quy ước Oracle)."),
            new("Lưu ý mật khẩu", "Mật khẩu lưu dạng chữ thường trong Data\\Config\\config.xml cạnh exe - chỉ dùng trên máy cá nhân, không chép file này ra ngoài."),
        ]),

        new("Tra cứu trong trang HTML", "", "Menu trái là danh sách bảng (bấm để xem), bên phải là định nghĩa bảng đang chọn. Ô \"Ten bang...\" và \"Ten cot (tuy chon)...\" + nút Tim kiem kết hợp như dưới đây.",
        [
            new("Chỉ nhập Tên bảng", "Hiện toàn bộ cột của bảng: STT, tên cột, kiểu (đã gồm độ dài, vd VARCHAR2(100)), Null, mô tả (comment của cột trong database)."),
            new("Tên bảng + Tên cột", "Chỉ hiện các cột khớp Tên cột trong đúng bảng đó."),
            new("Chỉ nhập Tên cột", "Tìm mọi bảng có cột khớp, kết quả nhóm theo từng bảng - dùng khi không nhớ cột thuộc bảng nào."),
            new("Màu nền", "Vàng nhạt = cột khoá chính."),
            new("Khoá ngoại", "Bảng khoá ngoại dưới phần cột: bấm tên bảng tham chiếu để mở bảng đó ở tab mới. Thêm #table=TÊN_BẢNG vào cuối đường dẫn file HTML để chia sẻ link tới 1 bảng."),
        ]),

        new("Nhiều database và cập nhật", "", "Mỗi database có file dữ liệu riêng.",
        [
            new("Tên file theo database", "File .db / .html đặt theo tên database (PostgreSQL) hoặc Service Name / SID (Oracle) - đọc nhiều database khác nhau không ghi đè lên nhau."),
            new("Đã đọc trước đó", "Mở lại module (hoặc đổi Nguon) vẫn nhận file .db / .html đã có của database đang chọn - không cần đọc lại nếu schema chưa đổi."),
            new("Lấy dữ liệu mới nhất", "Bấm lại \"1. Doc Database -> SQLite\" rồi \"2. Xuat HTML\". Mỗi lần đọc là dựng lại toàn bộ."),
        ]),
    ];
}
