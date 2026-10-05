using SharedUI.Help;

namespace ModuleC.Views;

/// <summary>
/// Nội dung cửa sổ Hướng dẫn (nút "Huong dan su dung" / F1) cho người dùng cuối. Nguồn: README.md; thêm / đổi tính năng thì
/// cập nhật cả 2 chỗ.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Bắt đầu nhanh", "", "Đổi danh sách cột viết bằng nhãn tiếng Nhật (vd SS1.基準単位数量) sang tên cột thật trong database, theo từ điển dữ liệu có sẵn.",
        [
            new("1. Nhập bảng + Alias", "Mỗi dòng bên trái: Tên bảng (vd MST_XFR_INST_SEND_IF) và Alias dùng trong SQL (vd SS1). Nút + thêm dòng, nút đỏ xoá dòng."),
            new("2. Nhập danh sách cột", "Ô \"Nhap danh sach cot (tieng Nhat)\": mỗi dòng 1 cột dạng Alias.NhãnTiếngNhật, vd SS1.基準単位数量."),
            new("3. Bấm Tim kiem", "Bảng bên phải: STT, Tên bảng, Tên cột (Alias.TÊN_CỘT thật), Chú thích tên cột."),
            new("4. Bấm Copy", "Copy cả bảng kết quả (cột cách nhau bằng Tab) để dán sang Excel."),
            new("Mở lại hướng dẫn này", "Nút \"Huong dan su dung\" trên cùng, hoặc phím F1.", "F1"),
        ]),

        new("Quy tắc tra cột", "", "Cách 1 dòng Alias.Nhãn được đổi thành tên cột.",
        [
            new("Alias → bảng", "Phần trước dấu chấm là Alias, tra ra Tên bảng ở danh sách bên trái (không phân biệt hoa / thường). Alias không có ở bên trái thì dòng đó bị bỏ qua."),
            new("Nhãn → cột", "Phần sau dấu chấm phải trùng đúng nhãn tiếng Nhật của cột - là phần chú thích cột đứng trước \"//\" trong từ điển. Nhãn có nhiều cột trùng thì liệt kê hết."),
            new("Alias không có dòng cột nào", "Bảng có Alias nhưng không dòng nào trong ô danh sách cột dùng Alias đó (kể cả khi ô để trống) → liệt kê toàn bộ cột của bảng."),
            new("Dòng không hợp lệ", "Dòng trống, không có dấu chấm, hoặc dấu chấm ở đầu / cuối dòng bị bỏ qua."),
        ]),

        new("Tìm tên bảng", "", "Không nhớ tên bảng (tiếng Anh) - tra từ tên hoặc chú thích tiếng Nhật.",
        [
            new("Cách dùng", "Nút \"Tim kiem ten bang\" trên cùng: nhập 1 phần tên bảng (tiếng Anh) hoặc chú thích bảng (tiếng Nhật), bấm Tim kiem. Kết quả: STT, Tên bảng, Chú thích tên bảng."),
            new("So khớp", "Kiểu \"chứa chuỗi\", không phân biệt hoa / thường, trên cả tên bảng lẫn chú thích. Chọn chữ trong kết quả để copy sang ô Tên bảng."),
        ]),

        new("Từ điển dữ liệu", "", "Nguồn tra cứu của module.",
        [
            new("File dữ liệu", "Data\\table_columns.json cạnh ModuleC.exe (tên bảng, tên cột, chú thích cột, chú thích bảng), nạp 1 lần lúc mở module - dòng trạng thái dưới tiêu đề báo số dòng đã nạp."),
            new("Cập nhật từ điển", "Thay file Data\\table_columns.json bằng bản mới (UTF-8) rồi mở lại module."),
        ]),
    ];
}
