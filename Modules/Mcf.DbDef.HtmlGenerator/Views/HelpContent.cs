using SharedUI.Help;

namespace Mcf.DbDef.HtmlGenerator.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút Hướng dẫn / F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi tính năng thì cập nhật
/// cả 2 chỗ. Nằm ở Views (không ở Models / Services) vì 2 thư mục đó được biên dịch kèm vào Mcf.DbDef.Tests - project test
/// không reference SharedUI.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Tra cứu định nghĩa bảng / cột từ các workbook DBDef (Excel tiếng Nhật) bằng 1 trang HTML mở trong trình duyệt.",
        [
            new("1. Đặt file Excel", "Chép các workbook định nghĩa DB (*.xlsm) vào thư mục Data\\Excel cạnh Mcf.DbDef.HtmlGenerator.exe."),
            new("2. Bấm \"1. Doc Excel -> SQLite\"", "Đọc toàn bộ file trong Data\\Excel, lưu vào Data\\Database\\Mcf.DbDef.HtmlGenerator.db. Khung log bên dưới hiện file đang đọc và cảnh báo (bảng / cột không đọc được)."),
            new("3. Bấm \"2. Xuat HTML\"", "Sinh 1 file HTML tự chứa: Data\\Database\\Mcf.DbDef.HtmlGenerator.html (không cần mạng, gửi cho người khác được)."),
            new("4. Bấm \"Mo file HTML\"", "Mở trang tra cứu bằng trình duyệt mặc định."),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Tra cứu trong trang HTML", "", "Menu trái là danh sách bảng (bấm để xem), bên phải là định nghĩa bảng đang chọn. Ô \"Ten bang...\" và \"Ten cot (tuy chon)...\" + nút Tim kiem kết hợp như dưới đây.",
        [
            new("Chỉ nhập Tên bảng", "Hiện toàn bộ cấu trúc bảng: 説明, 管理タイプ, các cột cần lưu ý khi thay đổi, 改廃, rồi bảng cột (Level, tên cột, kiểu, xác định, Null, tên tiếng Nhật, mô tả, nhóm dùng chung)."),
            new("Tên bảng + Tên cột", "Chỉ hiện các dòng cột khớp Tên cột trong đúng bảng đó."),
            new("Chỉ nhập Tên cột", "Tìm mọi bảng có cột khớp, kết quả nhóm theo từng bảng - dùng khi không nhớ cột thuộc bảng nào."),
            new("Màu nền dòng cột", "Vàng nhạt = khoá chính; xanh nhạt = cột dùng chung (nhóm $...$ như $EXCTRL_COLS$, được bung ra thành các cột thật)."),
            new("Khoá ngoại (FOREIGN)", "Bảng khoá ngoại dưới phần cột: bấm tên bảng tham chiếu để mở bảng đó ở tab mới."),
            new("Chia sẻ link tới 1 bảng", "Thêm #table=TÊN_BẢNG vào cuối đường dẫn file HTML (vd Mcf.DbDef.HtmlGenerator.html#table=MST_ITEM) - mở ra là chọn sẵn bảng đó."),
        ]),

        new("Cập nhật dữ liệu", "", "Khi định nghĩa DB trong Excel thay đổi.",
        [
            new("Làm lại 2 bước", "Thay / thêm file .xlsm trong Data\\Excel, rồi bấm lại \"1. Doc Excel -> SQLite\" và \"2. Xuat HTML\". Mỗi lần đọc là dựng lại toàn bộ dữ liệu (không cộng dồn bản cũ)."),
            new("Bảng trùng giữa 2 file", "Giữ định nghĩa ở file đọc trước, bảng trùng ở file sau được ghi vào log."),
            new("File / sheet lỗi", "File hỏng hoặc thiếu sheet index テーブル・ビュー一覧 được báo trong log và bỏ qua, các file khác vẫn đọc tiếp."),
        ]),

        new("Workbook nguồn cần có gì", "", "Cấu trúc workbook DBDef mà công cụ đọc được.",
        [
            new("Sheet index", "テーブル・ビュー一覧: tên bảng, tên tiếng Nhật, loại, ghi chú, tên sheet chứa định nghĩa."),
            new("Khối định nghĩa bảng", "Dòng marker (* hoặc *w + tên bảng + alias + tên tiếng Nhật), các mục 【説明】【管理タイプ】【運用後の変更に注意が必要な項目】【改廃】 (thứ tự tuỳ ý, có thể thiếu), rồi bảng cột レベル / 項目名 / 型 / 桁 / Null / 日本語 / 説明. Vị trí cột được dò theo tên tiêu đề nên layout lệch nhẹ giữa các sheet vẫn đọc được."),
            new("Nhóm cột dùng chung $...$", "Định nghĩa thật nằm ở sheet 制御用 (hoặc EXCTRL) của chính file đó. Nhóm không tìm thấy định nghĩa thì bị bỏ qua và ghi log."),
            new("Dòng FOREIGN", "FOREIGN | cột cục bộ | bảng tham chiếu | cột tham chiếu (để trống nếu trùng tên cột cục bộ); nhiều cột cách nhau bằng dấu phẩy."),
        ]),
    ];
}
