using SharedUI.Help;

namespace Mcf.CrudDiagram.HtmlGenerator.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút Hướng dẫn / F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi tính năng thì cập nhật
/// cả 2 chỗ. Nằm ở Views (không ở Models / Services) vì 2 thư mục đó được biên dịch kèm vào Mcf.CrudDiagram.Tests - project
/// test không reference SharedUI.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Gom các file CRUD図 (Excel) thành 1 trang HTML tra cứu: logic nào đọc / ghi bảng nào, nhảy qua lại giữa các logic gọi nhau.",
        [
            new("1. Chọn thư mục nguồn", "Nút \"Chon thu muc nguon...\": chọn thư mục gốc chứa các file *.xlsx CRUD図 (đọc cả thư mục con). Thư mục được nhớ cho lần mở sau."),
            new("2. Bấm \"1. Doc Excel -> SQLite\"", "Đọc mọi file .xlsx; mỗi sheet logic thành 1 trang Data\\Database\\Html\\{mã logic}.html, danh mục lưu vào Data\\Database\\02_CRUD図.db. Bỏ qua sheet 表紙 / 変更来歴 / 本ドキュメントについて và sheet trống; file lỗi được ghi log và bỏ qua."),
            new("3. Bấm \"2. Xuat HTML\"", "Sinh trang mục lục Data\\Database\\Html\\02_CRUD図.html."),
            new("4. Bấm \"Mo file HTML\"", "Mở trang mục lục bằng trình duyệt mặc định. Gửi cho người khác: gửi cả thư mục Data\\Database\\Html."),
            new("Mở lại hướng dẫn này", "Nút Hướng dẫn trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Tra cứu trong trang HTML", "", "Cột trái tìm và chọn logic, bên phải hiện bảng CRUD của logic đó.",
        [
            new("Ô \"Ma logic / ten...\"", "Lọc theo mã logic (tên sheet) hoặc tên hiển thị (chứa chuỗi, không phân biệt hoa / thường)."),
            new("Ô \"Tim theo ten bang/object...\"", "Lọc theo toàn bộ chữ trong sheet - nhập tên 1 bảng để biết logic nào dùng bảng đó (nhu cầu chính của CRUD図). 2 ô kết hợp với nhau (phải khớp cả 2)."),
            new("Xem 1 logic", "Bấm 1 logic trong danh sách: bảng CRUD hiện ngay ở khung bên phải, mục đang xem được tô đậm."),
            new("Tiêu đề bảng luôn hiện", "Cuộn xuống dòng dưới, hàng tiêu đề (ID / 名称 / 使用オブジェクト / C / R / U / D / 種 / 備考) vẫn giữ ở trên cùng."),
        ]),

        new("Bảng CRUD hiển thị gì", "", "Mỗi sheet logic thành 1 bảng giống bố cục Excel gốc.",
        [
            new("Thông tin đầu trang", "モジュールID / モジュール名, ｻﾌﾞﾓｼﾞｭｰﾙID / ｻﾌﾞﾓｼﾞｭｰﾙ名, ロジック名, 文書番号 / Version / Rev."),
            new("Bảng CRUD", "Mỗi hàng Excel là 1 hàng trong bảng (giữ cả hàng trống ngăn cách). Hàng có ID mở 1 khối mới (cột ID / 名称 in đậm); các hàng sau là đối tượng được dùng kèm cờ C / R / U / D, 種, 備考."),
            new("Link sang logic khác", "使用オブジェクト dạng {Mã logic}.{ID} (vd MSBBL6020.Slo_Chk03) là lời gọi tới 1 khối của logic khác: bấm vào để mở đúng khối đó. Tên bảng thường, hoặc mã không có trong bộ tài liệu, giữ nguyên chữ."),
            new("Sheet bố cục lạ", "Sheet không có hàng tiêu đề 使用オブジェクト thì hiện dạng lưới ô thô để không mất nội dung."),
        ]),

        new("Cập nhật", "", "Khi tài liệu CRUD図 thay đổi.",
        [
            new("Làm lại 2 bước", "Sửa / thêm file trong thư mục nguồn rồi bấm lại \"1. Doc Excel -> SQLite\" và \"2. Xuat HTML\". Trang HTML cũ đã mở trong trình duyệt cần tải lại (F5) để thấy bản mới.", "F5"),
            new("Mở trang mục lục bằng file:// vẫn chạy", "Danh mục được nhúng sẵn trong trang (không đọc file ngoài), nên mở thẳng file HTML trên ổ đĩa vẫn tìm kiếm được."),
        ]),
    ];
}
